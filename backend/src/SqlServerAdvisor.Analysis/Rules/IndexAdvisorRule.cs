using System.Security.Cryptography;
using System.Text;
using SqlServerAdvisor.Application.Contracts;
using SqlServerAdvisor.Domain.Entities;
using SqlServerAdvisor.Domain.Enums;

namespace SqlServerAdvisor.Analysis.Rules;

public sealed class IndexAdvisorRule : IIndexAnalysisRule
{
    public IReadOnlyCollection<string> RuleIds => ["IDX-001", "IDX-002", "IDX-003", "IDX-004"];

    public Task<IReadOnlyCollection<Finding>> EvaluateAsync(
        Guid serverProfileId,
        DateTimeOffset capturedAt,
        IReadOnlyCollection<IndexSnapshot> indexes,
        IReadOnlyCollection<MissingIndexSnapshot> missingIndexes,
        CancellationToken cancellationToken)
    {
        var findings = new List<Finding>();
        var indexList = indexes.ToList();

        AddFragmentationFindings(findings, serverProfileId, capturedAt, indexList);
        AddLowValueIndexFindings(findings, serverProfileId, capturedAt, indexList);
        AddOverlapFindings(findings, serverProfileId, capturedAt, indexList);
        AddMissingIndexFindings(findings, serverProfileId, capturedAt, indexList, missingIndexes);

        return Task.FromResult<IReadOnlyCollection<Finding>>(findings);
    }

    private static void AddFragmentationFindings(
        ICollection<Finding> findings,
        Guid serverProfileId,
        DateTimeOffset capturedAt,
        IReadOnlyCollection<IndexSnapshot> indexes)
    {
        foreach (var index in indexes)
        {
            var reads = index.UserSeeks + index.UserScans + index.UserLookups;
            if (index.PageCount is null || index.PageCount < 1000 ||
                index.AvgFragmentationPercent is null || index.AvgFragmentationPercent < 30m ||
                index.IsDisabled)
                continue;

            // A mature usage window with virtually no reads should be treated as a value/cost question,
            // not as an automatic maintenance candidate.
            if (index.UsageSinceDays >= 7 && reads <= 10 && index.UserUpdates >= 1000)
                continue;

            var sizeFactor = Math.Min(20m, (decimal)Math.Log10(Math.Max(1000, index.PageCount.Value)) * 4m);
            var readFactor = reads == 0 ? 0m : Math.Min(15m, (decimal)Math.Log10(reads + 1) * 3m);
            var impact = Math.Min(95m, Math.Round(
                28m + (index.AvgFragmentationPercent.Value - 30m) * 0.75m + sizeFactor + readFactor, 2));
            var severity = impact >= 82m ? FindingSeverity.High : impact >= 60m ? FindingSeverity.Medium : FindingSeverity.Low;
            var confidence = index.UsageSinceDays >= 7
                ? (index.PageCount >= 10000 ? 92m : 85m)
                : 70m;

            findings.Add(new Finding
            {
                ServerProfileId = serverProfileId,
                DatabaseName = index.DatabaseName,
                ObjectName = index.TableName,
                RuleId = "IDX-001",
                Category = "Index",
                Severity = severity,
                Title = $"İndeks bakım adayı: {index.DatabaseName} · {index.TableName} · {index.IndexName}",
                TechnicalDescription = $"{index.IndexName} indeksi %{index.AvgFragmentationPercent:N1} fragmentation, {index.PageCount:N0} page (~{index.SizeMb:N1} MB), {reads:N0} read ve {index.UserUpdates:N0} update gösteriyor. Kullanım penceresi yaklaşık {index.UsageSinceDays} gün. Fragmentation tek başına rebuild gerekçesi değildir; page count, kullanım ve storage latency birlikte değerlendirilmelidir.",
                ConfidenceScore = confidence,
                ImpactScore = impact,
                FindingScore = impact,
                Fingerprint = $"IDX-001:{serverProfileId:N}:{index.DatabaseName}:{index.ObjectId}:{index.IndexId}",
                FirstDetectedAt = capturedAt,
                LastDetectedAt = capturedAt
            });
        }
    }

    private static void AddLowValueIndexFindings(
        ICollection<Finding> findings,
        Guid serverProfileId,
        DateTimeOffset capturedAt,
        IReadOnlyCollection<IndexSnapshot> indexes)
    {
        foreach (var index in indexes)
        {
            if (index.IsDisabled || index.IsPrimaryKey || index.IsUnique ||
                index.PageCount is null || index.PageCount < 1000 ||
                index.UsageSinceDays < 7 || index.UserUpdates < 5000)
                continue;

            var reads = index.UserSeeks + index.UserScans + index.UserLookups;
            var allowedReads = Math.Max(10L, index.UserUpdates / 100L); // at most ~1 read per 100 writes
            if (reads > allowedReads)
                continue;

            var writePressure = Math.Min(35m, (decimal)Math.Log10(index.UserUpdates + 1) * 7m);
            var sizePressure = Math.Min(25m, (decimal)Math.Log10(Math.Max(1000, index.PageCount.Value)) * 5m);
            var impact = Math.Min(92m, Math.Round(35m + writePressure + sizePressure - Math.Min(10m, reads), 2));
            var severity = impact >= 80m ? FindingSeverity.High : FindingSeverity.Medium;

            findings.Add(new Finding
            {
                ServerProfileId = serverProfileId,
                DatabaseName = index.DatabaseName,
                ObjectName = index.TableName,
                RuleId = "IDX-003",
                Category = "Index",
                Severity = severity,
                Title = $"Düşük kullanım / yüksek bakım maliyetli indeks: {index.DatabaseName} · {index.TableName} · {index.IndexName}",
                TechnicalDescription = $"Yaklaşık {index.UsageSinceDays} günlük kullanım penceresinde indeks {reads:N0} read ve {index.UserUpdates:N0} update gösteriyor; boyutu ~{index.SizeMb:N1} MB. Bu bir DROP INDEX talimatı değildir. Query plan bağımlılıkları, raporlama/ay sonu gibi seyrek workload ve restart sonrası kullanım penceresi doğrulanmalıdır.",
                ConfidenceScore = index.UsageSinceDays >= 30 ? 92m : index.UsageSinceDays >= 14 ? 85m : 78m,
                ImpactScore = impact,
                FindingScore = impact,
                Fingerprint = $"IDX-003:{serverProfileId:N}:{index.DatabaseName}:{index.ObjectId}:{index.IndexId}",
                FirstDetectedAt = capturedAt,
                LastDetectedAt = capturedAt
            });
        }
    }

    private static void AddOverlapFindings(
        ICollection<Finding> findings,
        Guid serverProfileId,
        DateTimeOffset capturedAt,
        IReadOnlyCollection<IndexSnapshot> indexes)
    {
        var eligible = indexes
            .Where(x => !x.IsDisabled && !x.IsPrimaryKey && !x.IsUnique && !string.IsNullOrWhiteSpace(x.KeyColumns))
            .GroupBy(x => new { x.DatabaseName, x.ObjectId });

        foreach (var tableGroup in eligible)
        {
            var tableIndexes = tableGroup.OrderBy(x => x.IndexId).ToList();
            for (var i = 0; i < tableIndexes.Count; i++)
            {
                var candidate = tableIndexes[i];
                var candidateKeys = ParseColumns(candidate.KeyColumns);
                var candidateIncludes = ParseColumns(candidate.IncludeColumns).ToHashSet(StringComparer.OrdinalIgnoreCase);

                for (var j = 0; j < tableIndexes.Count; j++)
                {
                    if (i == j) continue;
                    var covering = tableIndexes[j];
                    if (!FiltersEquivalent(candidate, covering)) continue;

                    var coveringKeys = ParseColumns(covering.KeyColumns);
                    if (!candidateKeys.SequenceEqual(coveringKeys, StringComparer.OrdinalIgnoreCase))
                        continue;

                    var coveringIncludes = ParseColumns(covering.IncludeColumns).ToHashSet(StringComparer.OrdinalIgnoreCase);
                    if (!coveringIncludes.IsSupersetOf(candidateIncludes))
                        continue;

                    // For exact duplicates, flag only the higher index_id to avoid mirrored findings.
                    var exactDuplicate = coveringIncludes.SetEquals(candidateIncludes);
                    if (exactDuplicate && candidate.IndexId < covering.IndexId)
                        continue;

                    var reads = candidate.UserSeeks + candidate.UserScans + candidate.UserLookups;
                    var impact = Math.Min(85m, 45m + Math.Min(20m, candidate.SizeMb / 250m * 10m) +
                                               Math.Min(20m, candidate.UserUpdates / 10000m * 5m));
                    var relationship = exactDuplicate ? "aynı key/include yapısını" : "aynı key yapısını ve daha geniş include kapsamını";

                    findings.Add(new Finding
                    {
                        ServerProfileId = serverProfileId,
                        DatabaseName = candidate.DatabaseName,
                        ObjectName = candidate.TableName,
                        RuleId = "IDX-004",
                        Category = "Index",
                        Severity = impact >= 75m ? FindingSeverity.Medium : FindingSeverity.Low,
                        Title = $"Örtüşen indeks adayı: {candidate.DatabaseName} · {candidate.TableName} · {candidate.IndexName}",
                        TechnicalDescription = $"{candidate.IndexName}, {covering.IndexName} ile {relationship} paylaşıyor. Aday indeks ~{candidate.SizeMb:N1} MB, {reads:N0} read ve {candidate.UserUpdates:N0} update gösteriyor. Bu bulgu otomatik DROP önermez; query plan kullanımı ve farklı INCLUDE/filtre amaçları doğrulanmalıdır.",
                        ConfidenceScore = exactDuplicate ? 90m : 78m,
                        ImpactScore = Math.Round(impact, 2),
                        FindingScore = Math.Round(impact, 2),
                        Fingerprint = $"IDX-004:{serverProfileId:N}:{candidate.DatabaseName}:{candidate.ObjectId}:{candidate.IndexId}:{covering.IndexId}",
                        FirstDetectedAt = capturedAt,
                        LastDetectedAt = capturedAt
                    });
                    break;
                }
            }
        }
    }

    private static void AddMissingIndexFindings(
        ICollection<Finding> findings,
        Guid serverProfileId,
        DateTimeOffset capturedAt,
        IReadOnlyCollection<IndexSnapshot> indexes,
        IReadOnlyCollection<MissingIndexSnapshot> missingIndexes)
    {
        foreach (var missing in missingIndexes)
        {
            var reads = missing.UserSeeks + missing.UserScans;
            if (reads < 50 || missing.AvgUserImpact < 70m || missing.ImprovementMeasure < 500000m)
                continue;

            if (IsCoveredByExistingIndex(missing, indexes))
                continue;

            var improvementLog = (decimal)Math.Log10((double)Math.Max(10m, missing.ImprovementMeasure));
            var impact = Math.Min(95m, Math.Round(
                45m + Math.Min(25m, missing.AvgUserImpact / 4m) +
                Math.Min(25m, improvementLog * 3m), 2));
            var severity = impact >= 85m ? FindingSeverity.High : FindingSeverity.Medium;
            var signature = $"{missing.DatabaseName}|{missing.TableName}|{missing.EqualityColumns}|{missing.InequalityColumns}|{missing.IncludedColumns}";
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(signature)))[..24];

            findings.Add(new Finding
            {
                ServerProfileId = serverProfileId,
                DatabaseName = missing.DatabaseName,
                ObjectName = missing.TableName,
                RuleId = "IDX-002",
                Category = "Index",
                Severity = severity,
                Title = $"Eksik indeks tuning adayı: {missing.DatabaseName} · {missing.TableName}",
                TechnicalDescription = $"Missing index DMV kaydı {reads:N0} seek/scan, %{missing.AvgUserImpact:N1} tahmini kullanıcı etkisi ve {missing.ImprovementMeasure:N0} improvement measure gösteriyor. Equality=[{missing.EqualityColumns}], Inequality=[{missing.InequalityColumns}], Include=[{missing.IncludedColumns}]. Mevcut indeksler için konservatif kapsama kontrolü yapıldı; doğrudan CREATE INDEX çalıştırılmamalıdır.",
                ConfidenceScore = reads >= 500 ? 92m : 82m,
                ImpactScore = impact,
                FindingScore = impact,
                Fingerprint = $"IDX-002:{serverProfileId:N}:{hash}",
                FirstDetectedAt = capturedAt,
                LastDetectedAt = capturedAt
            });
        }
    }

    private static bool IsCoveredByExistingIndex(
        MissingIndexSnapshot missing,
        IReadOnlyCollection<IndexSnapshot> indexes)
    {
        var equality = ParseColumns(missing.EqualityColumns);
        var inequality = ParseColumns(missing.InequalityColumns);
        var include = ParseColumns(missing.IncludedColumns);
        var requiredKeys = equality.Concat(inequality).ToArray();
        if (requiredKeys.Length == 0) return false;

        foreach (var index in indexes.Where(x =>
                     x.DatabaseName.Equals(missing.DatabaseName, StringComparison.OrdinalIgnoreCase) &&
                     x.TableName.Equals(missing.TableName, StringComparison.OrdinalIgnoreCase) &&
                     !x.IsDisabled && !x.HasFilter))
        {
            var existingKeys = ParseColumns(index.KeyColumns);
            if (existingKeys.Length < requiredKeys.Length)
                continue;

            var prefix = existingKeys.Take(requiredKeys.Length).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (!requiredKeys.All(prefix.Contains))
                continue;

            var availableColumns = existingKeys
                .Concat(ParseColumns(index.IncludeColumns))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (include.All(availableColumns.Contains))
                return true;
        }

        return false;
    }

    private static bool FiltersEquivalent(IndexSnapshot left, IndexSnapshot right)
    {
        if (left.HasFilter != right.HasFilter) return false;
        if (!left.HasFilter) return true;
        return string.Equals(
            left.FilterDefinition?.Trim(),
            right.FilterDefinition?.Trim(),
            StringComparison.OrdinalIgnoreCase);
    }

    private static string[] ParseColumns(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(x => x.Trim().Trim('[', ']'))
                .Where(x => x.Length > 0)
                .ToArray();
}
