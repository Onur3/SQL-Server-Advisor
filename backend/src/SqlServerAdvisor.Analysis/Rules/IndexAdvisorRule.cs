using System.Security.Cryptography;
using System.Text;
using SqlServerAdvisor.Application.Contracts;
using SqlServerAdvisor.Domain.Entities;
using SqlServerAdvisor.Domain.Enums;

namespace SqlServerAdvisor.Analysis.Rules;

public sealed class IndexAdvisorRule : IIndexAnalysisRule
{
    public IReadOnlyCollection<string> RuleIds =>
        ["IDX-001", "IDX-002", "IDX-003", "IDX-004", "IDX-005", "IDX-006", "IDX-007"];

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
        AddPrefixConsolidationFindings(findings, serverProfileId, capturedAt, indexList);
        AddWriteHotspotFindings(findings, serverProfileId, capturedAt, indexList);
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
            var reads = Reads(index);
            if (index.PageCount is null || index.PageCount < 1000 ||
                index.AvgFragmentationPercent is null || index.AvgFragmentationPercent < 30m ||
                index.IsDisabled)
                continue;

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
                TechnicalDescription = $"{index.IndexName} indeksi %{index.AvgFragmentationPercent:N1} fragmentation, {index.PageCount:N0} page (~{index.SizeMb:N1} MB), {reads:N0} read ve {index.UserUpdates:N0} update gösteriyor. Kullanım penceresi yaklaşık {index.UsageSinceDays} gün. FillFactor={index.FillFactor}, Compression={index.DataCompression ?? "bilinmiyor"}. Fragmentation tek başına rebuild gerekçesi değildir; page count, kullanım ve storage latency birlikte değerlendirilmelidir.",
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
                index.UsageSinceDays < 14 || index.UserUpdates < 5000)
                continue;

            var reads = Reads(index);
            var operationalWrites = OperationalWrites(index);
            var allowedReads = Math.Max(25L, index.UserUpdates / 200L);
            if (reads > allowedReads)
                continue;

            if (operationalWrites > 0 && operationalWrites < 1000 && index.UserUpdates < 10000)
                continue;

            var writePressure = Math.Min(35m, (decimal)Math.Log10(index.UserUpdates + 1) * 7m);
            var sizePressure = Math.Min(25m, (decimal)Math.Log10(Math.Max(1000, index.PageCount.Value)) * 5m);
            var impact = Math.Min(94m, Math.Round(38m + writePressure + sizePressure - Math.Min(12m, reads / 5m), 2));
            var severity = impact >= 82m ? FindingSeverity.High : FindingSeverity.Medium;
            var confidence = index.UsageSinceDays >= 30 ? 94m : index.UsageSinceDays >= 21 ? 88m : 82m;

            findings.Add(new Finding
            {
                ServerProfileId = serverProfileId,
                DatabaseName = index.DatabaseName,
                ObjectName = index.TableName,
                RuleId = "IDX-003",
                Category = "Index",
                Severity = severity,
                Title = $"DROP / konsolidasyon adayı indeks: {index.DatabaseName} · {index.TableName} · {index.IndexName}",
                TechnicalDescription = $"Yaklaşık {index.UsageSinceDays} günlük kesintisiz SQL kullanım penceresinde indeks {reads:N0} read, {index.UserUpdates:N0} usage-update ve {operationalWrites:N0} leaf insert/delete/update işlemi göstermiş; boyutu ~{index.SizeMb:N1} MB. Key={index.KeyDefinition ?? index.KeyColumns}; Include={index.IncludeColumns}. Bu güçlü bir kaldırma/konsolidasyon adayıdır ancak otomatik DROP değildir. Dönemsel workload, plan bağımlılıkları ve restart geçmişi doğrulanmalıdır.",
                ConfidenceScore = confidence,
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
            var tableIndexes = tableGroup.ToList();
            var processed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var candidate in tableIndexes)
            {
                var candidateKeys = ParseColumns(candidate.KeyColumns);
                var candidateIncludes = ParseColumns(candidate.IncludeColumns).ToHashSet(StringComparer.OrdinalIgnoreCase);

                var coveringCandidates = tableIndexes
                    .Where(x => x.IndexId != candidate.IndexId && FiltersEquivalent(candidate, x))
                    .Where(x => candidateKeys.SequenceEqual(ParseColumns(x.KeyColumns), StringComparer.OrdinalIgnoreCase))
                    .Where(x => ParseColumns(x.IncludeColumns).ToHashSet(StringComparer.OrdinalIgnoreCase).IsSupersetOf(candidateIncludes))
                    .ToList();

                if (coveringCandidates.Count == 0)
                    continue;

                var covering = coveringCandidates
                    .OrderByDescending(Reads)
                    .ThenBy(x => x.UserUpdates)
                    .ThenBy(x => x.SizeMb)
                    .First();

                var coveringIncludes = ParseColumns(covering.IncludeColumns).ToHashSet(StringComparer.OrdinalIgnoreCase);
                var exactDuplicate = coveringIncludes.SetEquals(candidateIncludes);

                if (exactDuplicate)
                {
                    var candidateValue = Reads(candidate);
                    var coveringValue = Reads(covering);
                    if (candidateValue > coveringValue ||
                        (candidateValue == coveringValue && candidate.IndexId < covering.IndexId))
                        continue;
                }
                else if (Reads(candidate) > Math.Max(100L, Reads(covering) / 2L))
                {
                    continue;
                }

                var pairKey = $"{Math.Min(candidate.IndexId, covering.IndexId)}:{Math.Max(candidate.IndexId, covering.IndexId)}";
                if (!processed.Add(pairKey))
                    continue;

                var reads = Reads(candidate);
                var impact = Math.Min(88m, 45m + Math.Min(20m, candidate.SizeMb / 250m * 10m) +
                                           Math.Min(23m, candidate.UserUpdates / 10000m * 5m));
                var relationship = exactDuplicate ? "aynı key/include/filter yapısını" : "aynı key yapısını ve daha geniş include kapsamını";

                findings.Add(new Finding
                {
                    ServerProfileId = serverProfileId,
                    DatabaseName = candidate.DatabaseName,
                    ObjectName = candidate.TableName,
                    RuleId = "IDX-004",
                    Category = "Index",
                    Severity = impact >= 75m ? FindingSeverity.Medium : FindingSeverity.Low,
                    Title = $"Duplicate / örtüşen indeks konsolidasyon adayı: {candidate.DatabaseName} · {candidate.TableName} · {candidate.IndexName}",
                    TechnicalDescription = $"{candidate.IndexName}, {covering.IndexName} ile {relationship} paylaşıyor. Aday indeks ~{candidate.SizeMb:N1} MB, {reads:N0} read ve {candidate.UserUpdates:N0} update; kapsayan indeks {Reads(covering):N0} read gösteriyor. Daha düşük değerli olan indeks kaldırma/konsolidasyon adayıdır; farklı plan bağımlılıkları doğrulanmadan DROP uygulanmamalıdır.",
                    ConfidenceScore = exactDuplicate ? 94m : 84m,
                    ImpactScore = Math.Round(impact, 2),
                    FindingScore = Math.Round(impact, 2),
                    Fingerprint = $"IDX-004:{serverProfileId:N}:{candidate.DatabaseName}:{candidate.ObjectId}:{candidate.IndexId}:{covering.IndexId}",
                    FirstDetectedAt = capturedAt,
                    LastDetectedAt = capturedAt
                });
            }
        }
    }

    private static void AddPrefixConsolidationFindings(
        ICollection<Finding> findings,
        Guid serverProfileId,
        DateTimeOffset capturedAt,
        IReadOnlyCollection<IndexSnapshot> indexes)
    {
        var groups = indexes
            .Where(x => !x.IsDisabled && !x.IsPrimaryKey && !x.IsUnique && !string.IsNullOrWhiteSpace(x.KeyColumns))
            .GroupBy(x => new { x.DatabaseName, x.ObjectId });

        foreach (var group in groups)
        {
            var tableIndexes = group.ToList();
            foreach (var candidate in tableIndexes)
            {
                if (candidate.UsageSinceDays < 14 || candidate.UserUpdates < 1000)
                    continue;

                var candidateKeys = ParseColumns(candidate.KeyColumns);
                if (candidateKeys.Length == 0)
                    continue;

                foreach (var covering in tableIndexes.Where(x => x.IndexId != candidate.IndexId && FiltersEquivalent(candidate, x)))
                {
                    var coveringKeys = ParseColumns(covering.KeyColumns);
                    if (coveringKeys.Length <= candidateKeys.Length)
                        continue;

                    if (!coveringKeys.Take(candidateKeys.Length).SequenceEqual(candidateKeys, StringComparer.OrdinalIgnoreCase))
                        continue;

                    var candidateRequired = candidateKeys
                        .Concat(ParseColumns(candidate.IncludeColumns))
                        .ToHashSet(StringComparer.OrdinalIgnoreCase);
                    var coveringAvailable = coveringKeys
                        .Concat(ParseColumns(covering.IncludeColumns))
                        .ToHashSet(StringComparer.OrdinalIgnoreCase);
                    if (!coveringAvailable.IsSupersetOf(candidateRequired))
                        continue;

                    var candidateReads = Reads(candidate);
                    var coveringReads = Reads(covering);
                    var lowRelativeUse = candidateReads <= Math.Max(50L, coveringReads / 20L);
                    var lowWriteValue = candidateReads <= Math.Max(25L, candidate.UserUpdates / 200L);
                    if (!lowRelativeUse && !lowWriteValue)
                        continue;

                    var impact = Math.Min(88m, 42m + Math.Min(20m, candidate.SizeMb / 100m * 4m) +
                                               Math.Min(26m, candidate.UserUpdates / 5000m * 4m));

                    findings.Add(new Finding
                    {
                        ServerProfileId = serverProfileId,
                        DatabaseName = candidate.DatabaseName,
                        ObjectName = candidate.TableName,
                        RuleId = "IDX-005",
                        Category = "Index",
                        Severity = impact >= 75m ? FindingSeverity.Medium : FindingSeverity.Low,
                        Title = $"Prefix indeks konsolidasyon adayı: {candidate.DatabaseName} · {candidate.TableName} · {candidate.IndexName}",
                        TechnicalDescription = $"{candidate.IndexName} key=[{candidate.KeyDefinition ?? candidate.KeyColumns}] yapısı, {covering.IndexName} key=[{covering.KeyDefinition ?? covering.KeyColumns}] indeksinin sol prefix'i ve ihtiyaç duyduğu kolonlar kapsayan indekste mevcut. Aday {candidateReads:N0} read / {candidate.UserUpdates:N0} update, kapsayan indeks {coveringReads:N0} read gösteriyor. Daha dar indeks bazı workload'larda daha ucuz olabileceğinden bu bir otomatik DROP kararı değildir; plan bazında konsolidasyon doğrulanmalıdır.",
                        ConfidenceScore = candidate.UsageSinceDays >= 30 ? 90m : 80m,
                        ImpactScore = Math.Round(impact, 2),
                        FindingScore = Math.Round(impact, 2),
                        Fingerprint = $"IDX-005:{serverProfileId:N}:{candidate.DatabaseName}:{candidate.ObjectId}:{candidate.IndexId}:{covering.IndexId}",
                        FirstDetectedAt = capturedAt,
                        LastDetectedAt = capturedAt
                    });
                    break;
                }
            }
        }
    }

    private static void AddWriteHotspotFindings(
        ICollection<Finding> findings,
        Guid serverProfileId,
        DateTimeOffset capturedAt,
        IReadOnlyCollection<IndexSnapshot> indexes)
    {
        foreach (var index in indexes.Where(x => !x.IsDisabled && x.PageCount >= 1000))
        {
            if (index.LeafInsertCount < 10_000)
                continue;

            var allocationRate = index.LeafInsertCount == 0
                ? 0m
                : index.LeafAllocationCount * 100m / index.LeafInsertCount;
            var splitPressure = index.LeafAllocationCount >= 500 && allocationRate >= 5m;
            var latchPressure = index.PageLatchWaitMs >= 30_000;
            if (!splitPressure && !latchPressure)
                continue;

            var impact = Math.Min(92m,
                48m + Math.Min(24m, allocationRate) + Math.Min(20m, index.PageLatchWaitMs / 10000m));

            findings.Add(new Finding
            {
                ServerProfileId = serverProfileId,
                DatabaseName = index.DatabaseName,
                ObjectName = index.TableName,
                RuleId = "IDX-007",
                Category = "Index",
                Severity = impact >= 80m ? FindingSeverity.High : FindingSeverity.Medium,
                Title = $"Write-hot / page allocation tuning adayı: {index.DatabaseName} · {index.TableName} · {index.IndexName}",
                TechnicalDescription = $"{index.IndexName}: {index.LeafInsertCount:N0} leaf insert, {index.LeafAllocationCount:N0} leaf allocation (%{allocationRate:N1}), {index.PageLatchWaitMs:N0} ms page-latch wait. FillFactor={index.FillFactor}, Key={index.KeyDefinition ?? index.KeyColumns}, Compression={index.DataCompression ?? "bilinmiyor"}. Page allocation tek başına zararlı split kanıtı değildir; key dağılımı, sequential/random insert paterni, latch hotspot ve gerçek page split etkisi birlikte incelenmelidir.",
                ConfidenceScore = latchPressure && splitPressure ? 92m : 82m,
                ImpactScore = Math.Round(impact, 2),
                FindingScore = Math.Round(impact, 2),
                Fingerprint = $"IDX-007:{serverProfileId:N}:{index.DatabaseName}:{index.ObjectId}:{index.IndexId}",
                FirstDetectedAt = capturedAt,
                LastDetectedAt = capturedAt
            });
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

            var extension = FindExtensionCandidate(missing, indexes);
            var improvementLog = (decimal)Math.Log10((double)Math.Max(10m, missing.ImprovementMeasure));
            var impact = Math.Min(95m, Math.Round(
                45m + Math.Min(25m, missing.AvgUserImpact / 4m) +
                Math.Min(25m, improvementLog * 3m), 2));
            var severity = impact >= 85m ? FindingSeverity.High : FindingSeverity.Medium;
            var signature = $"{missing.DatabaseName}|{missing.TableName}|{missing.EqualityColumns}|{missing.InequalityColumns}|{missing.IncludedColumns}";
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(signature)))[..24];

            if (extension is not null)
            {
                var requiredIncludes = ParseColumns(missing.IncludedColumns).ToHashSet(StringComparer.OrdinalIgnoreCase);
                var available = ParseColumns(extension.KeyColumns)
                    .Concat(ParseColumns(extension.IncludeColumns))
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                var additions = requiredIncludes.Where(x => !available.Contains(x)).ToArray();

                findings.Add(new Finding
                {
                    ServerProfileId = serverProfileId,
                    DatabaseName = missing.DatabaseName,
                    ObjectName = missing.TableName,
                    RuleId = "IDX-006",
                    Category = "Index",
                    Severity = severity,
                    Title = $"Yeni indeks yerine mevcut indeksi modifiye etme adayı: {missing.DatabaseName} · {missing.TableName} · {extension.IndexName}",
                    TechnicalDescription = $"Missing-index workload {reads:N0} seek/scan ve %{missing.AvgUserImpact:N1} tahmini etki gösteriyor. {extension.IndexName} aynı key sırasına sahip: [{extension.KeyDefinition ?? extension.KeyColumns}]. Mevcut INCLUDE=[{extension.IncludeColumns}], ihtiyaç duyulan ek INCLUDE=[{string.Join(", ", additions)}]. İkinci aynı-key indeks oluşturmak yerine mevcut indeksin kontrollü yeniden tasarımı daha düşük toplam write/storage maliyeti sağlayabilir. Yeniden oluşturma/DROP_EXISTING yalnız test ve DBA onayıyla yapılmalıdır.",
                    ConfidenceScore = reads >= 500 ? 93m : 85m,
                    ImpactScore = impact,
                    FindingScore = impact,
                    Fingerprint = $"IDX-006:{serverProfileId:N}:{hash}:{extension.IndexId}",
                    FirstDetectedAt = capturedAt,
                    LastDetectedAt = capturedAt
                });
                continue;
            }

            findings.Add(new Finding
            {
                ServerProfileId = serverProfileId,
                DatabaseName = missing.DatabaseName,
                ObjectName = missing.TableName,
                RuleId = "IDX-002",
                Category = "Index",
                Severity = severity,
                Title = $"Yeni indeks oluşturma tuning adayı: {missing.DatabaseName} · {missing.TableName}",
                TechnicalDescription = $"Missing index DMV kaydı {reads:N0} seek/scan, %{missing.AvgUserImpact:N1} tahmini kullanıcı etkisi ve {missing.ImprovementMeasure:N0} improvement measure gösteriyor. Equality=[{missing.EqualityColumns}], Inequality=[{missing.InequalityColumns}], Include=[{missing.IncludedColumns}]. Mevcut indeks kapsama ve aynı-key modifikasyon kontrolleri yapıldı; yine de doğrudan CREATE INDEX çalıştırılmamalıdır.",
                ConfidenceScore = reads >= 500 ? 92m : 82m,
                ImpactScore = impact,
                FindingScore = impact,
                Fingerprint = $"IDX-002:{serverProfileId:N}:{hash}",
                FirstDetectedAt = capturedAt,
                LastDetectedAt = capturedAt
            });
        }
    }

    private static IndexSnapshot? FindExtensionCandidate(
        MissingIndexSnapshot missing,
        IReadOnlyCollection<IndexSnapshot> indexes)
    {
        var requiredKeys = ParseColumns(missing.EqualityColumns)
            .Concat(ParseColumns(missing.InequalityColumns))
            .ToArray();
        var requiredIncludes = ParseColumns(missing.IncludedColumns).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (requiredKeys.Length == 0 || requiredIncludes.Count == 0)
            return null;

        return indexes
            .Where(x => x.DatabaseName.Equals(missing.DatabaseName, StringComparison.OrdinalIgnoreCase) &&
                        x.TableName.Equals(missing.TableName, StringComparison.OrdinalIgnoreCase) &&
                        !x.IsDisabled && !x.HasFilter && !x.IsPrimaryKey && !x.IsUnique)
            .Where(x => ParseColumns(x.KeyColumns).SequenceEqual(requiredKeys, StringComparer.OrdinalIgnoreCase))
            .Select(x => new
            {
                Index = x,
                Available = ParseColumns(x.KeyColumns)
                    .Concat(ParseColumns(x.IncludeColumns))
                    .ToHashSet(StringComparer.OrdinalIgnoreCase)
            })
            .Where(x => !requiredIncludes.All(x.Available.Contains))
            .OrderByDescending(x => requiredIncludes.Count(x.Available.Contains))
            .ThenByDescending(x => Reads(x.Index))
            .Select(x => x.Index)
            .FirstOrDefault();
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

            if (!existingKeys.Take(requiredKeys.Length).SequenceEqual(requiredKeys, StringComparer.OrdinalIgnoreCase))
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

    private static long Reads(IndexSnapshot index) =>
        index.UserSeeks + index.UserScans + index.UserLookups;

    private static long OperationalWrites(IndexSnapshot index) =>
        index.LeafInsertCount + index.LeafDeleteCount + index.LeafUpdateCount;

    private static string[] ParseColumns(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(x => x.Trim().Trim('[', ']'))
                .Where(x => x.Length > 0)
                .ToArray();
}
