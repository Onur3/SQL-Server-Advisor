using SqlServerAdvisor.Application.Contracts;
using SqlServerAdvisor.Domain.Entities;
using SqlServerAdvisor.Domain.Enums;

namespace SqlServerAdvisor.Analysis.Rules;

public sealed class StatisticsAdvisorRule : IStatisticsAnalysisRule
{
    public IReadOnlyCollection<string> RuleIds =>
        ["STATS-001", "STATS-002", "STATS-003", "STATS-004", "STATS-005"];

    public Task<IReadOnlyCollection<Finding>> EvaluateAsync(
        Guid serverProfileId,
        DateTimeOffset capturedAt,
        IReadOnlyCollection<StatisticsSnapshot> statistics,
        CancellationToken cancellationToken)
    {
        var findings = new List<Finding>();
        var visibleStatistics = statistics.Where(x => x.PropertiesVisible).ToList();

        foreach (var item in visibleStatistics)
        {
            if (item.Rows < 5000)
                continue;

            var modificationPercent = item.Rows == 0 ? 0m : item.ModificationCounter * 100m / item.Rows;
            var ageDays = item.LastUpdated.HasValue
                ? Math.Max(0d, (capturedAt.UtcDateTime - DateTime.SpecifyKind(item.LastUpdated.Value, DateTimeKind.Utc)).TotalDays)
                : 365d;

            AddFreshnessFinding(findings, serverProfileId, capturedAt, item, modificationPercent, ageDays);
            AddNoRecomputeFinding(findings, serverProfileId, capturedAt, item, modificationPercent, ageDays);
            AddSamplingFinding(findings, serverProfileId, capturedAt, item, modificationPercent, ageDays);
            AddPersistedSamplingFinding(findings, serverProfileId, capturedAt, item, modificationPercent, ageDays);
        }

        AddDuplicateStatisticsFindings(findings, serverProfileId, capturedAt, visibleStatistics);

        return Task.FromResult<IReadOnlyCollection<Finding>>(findings);
    }

    private static void AddFreshnessFinding(
        ICollection<Finding> findings,
        Guid serverProfileId,
        DateTimeOffset capturedAt,
        StatisticsSnapshot item,
        decimal modificationPercent,
        double ageDays)
    {
        if (item.ModificationCounter < 5000)
            return;

        var materiallyChanged = modificationPercent >= 20m ||
                                (modificationPercent >= 10m && ageDays >= 1d) ||
                                (modificationPercent >= 5m && item.Rows >= 1_000_000 && ageDays >= 3d);

        if (!materiallyChanged)
            return;

        var impact = Math.Min(95m,
            38m +
            Math.Min(32m, modificationPercent) +
            Math.Min(15m, (decimal)Math.Log10(Math.Max(5000, item.Rows)) * 2m) +
            Math.Min(10m, (decimal)Math.Min(10d, ageDays)));

        var severity = impact >= 85m ? FindingSeverity.High : FindingSeverity.Medium;
        var confidence = item.ModificationCounter >= 50_000 ? 95m : 86m;
        var type = item.AutoCreated ? "AUTO" : item.UserCreated ? "USER" : "INDEX";

        findings.Add(new Finding
        {
            ServerProfileId = serverProfileId,
            DatabaseName = item.DatabaseName,
            ObjectName = item.TableName,
            RuleId = "STATS-001",
            Category = "Statistics",
            Severity = severity,
            Title = $"Statistics freshness riski: {item.DatabaseName} · {item.TableName} · {item.StatisticsName}",
            TechnicalDescription = $"{type} statistics {item.Rows:N0} satır üzerinde {item.ModificationCounter:N0} modification (%{modificationPercent:N1}) ve yaklaşık {ageDays:N1} günlük yaş gösteriyor. Columns=[{item.StatisticsColumns}], Sample={(item.SamplePercent.HasValue ? $"%{item.SamplePercent:N1}" : "bilinmiyor")}, Steps={item.Steps?.ToString() ?? "?"}, Filtered={(item.HasFilter ? "Evet" : "Hayır")}, NoRecompute={(item.NoRecompute ? "Evet" : "Hayır")}. Execution plan cardinality tahminleriyle birlikte değerlendirilmelidir.",
            ConfidenceScore = confidence,
            ImpactScore = Math.Round(impact, 2),
            FindingScore = Math.Round(impact, 2),
            Fingerprint = $"STATS-001:{serverProfileId:N}:{item.DatabaseName}:{item.ObjectId}:{item.StatisticsId}",
            FirstDetectedAt = capturedAt,
            LastDetectedAt = capturedAt
        });
    }

    private static void AddNoRecomputeFinding(
        ICollection<Finding> findings,
        Guid serverProfileId,
        DateTimeOffset capturedAt,
        StatisticsSnapshot item,
        decimal modificationPercent,
        double ageDays)
    {
        if (!item.NoRecompute || item.ModificationCounter < 1000 || modificationPercent < 5m)
            return;

        var impact = Math.Min(90m,
            50m + Math.Min(25m, modificationPercent) + Math.Min(15m, (decimal)Math.Min(15d, ageDays)));
        var severity = impact >= 80m ? FindingSeverity.High : FindingSeverity.Medium;

        findings.Add(new Finding
        {
            ServerProfileId = serverProfileId,
            DatabaseName = item.DatabaseName,
            ObjectName = item.TableName,
            RuleId = "STATS-002",
            Category = "Statistics",
            Severity = severity,
            Title = $"NORECOMPUTE altında değişen statistics: {item.DatabaseName} · {item.TableName} · {item.StatisticsName}",
            TechnicalDescription = $"Statistics NORECOMPUTE durumda ve {item.ModificationCounter:N0} satır değişimi (%{modificationPercent:N1}) birikmiş. Yaş yaklaşık {ageDays:N1} gün. Columns=[{item.StatisticsColumns}]. Otomatik statistics güncellemesi bu nesne için devre dışı olabileceğinden estimated/actual row sapmaları ve ilgili sorgu planları özellikle kontrol edilmelidir.",
            ConfidenceScore = 94m,
            ImpactScore = Math.Round(impact, 2),
            FindingScore = Math.Round(impact, 2),
            Fingerprint = $"STATS-002:{serverProfileId:N}:{item.DatabaseName}:{item.ObjectId}:{item.StatisticsId}",
            FirstDetectedAt = capturedAt,
            LastDetectedAt = capturedAt
        });
    }

    private static void AddSamplingFinding(
        ICollection<Finding> findings,
        Guid serverProfileId,
        DateTimeOffset capturedAt,
        StatisticsSnapshot item,
        decimal modificationPercent,
        double ageDays)
    {
        if (item.Rows < 1_000_000 || !item.SamplePercent.HasValue || item.SamplePercent.Value >= 10m)
            return;

        var meaningfulChange = modificationPercent >= 5m && item.ModificationCounter >= 10_000;
        var veryLowSample = item.SamplePercent.Value < 2m && item.Rows >= 10_000_000;
        if (!(meaningfulChange && ageDays >= 1d) && !veryLowSample)
            return;

        var impact = Math.Min(78m,
            42m +
            Math.Min(18m, modificationPercent) +
            Math.Min(12m, (10m - item.SamplePercent.Value) * 1.5m) +
            Math.Min(6m, (decimal)Math.Log10(item.Rows)));

        findings.Add(new Finding
        {
            ServerProfileId = serverProfileId,
            DatabaseName = item.DatabaseName,
            ObjectName = item.TableName,
            RuleId = "STATS-003",
            Category = "Statistics",
            Severity = impact >= 65m ? FindingSeverity.Medium : FindingSeverity.Low,
            Title = $"Statistics sample kalitesi inceleme adayı: {item.DatabaseName} · {item.TableName} · {item.StatisticsName}",
            TechnicalDescription = $"Büyük tablo statistics'i {item.Rows:N0} satır üzerinde %{item.SamplePercent:N2} sample ({item.RowsSampled:N0} satır), {item.Steps?.ToString() ?? "?"} histogram step ve %{modificationPercent:N1} modification gösteriyor. Columns=[{item.StatisticsColumns}]. Düşük sample tek başına hata değildir; histogram kalitesi ve estimated/actual row farkı doğrulanmadan FULLSCAN veya yüksek sample uygulanmamalıdır.",
            ConfidenceScore = 80m,
            ImpactScore = Math.Round(impact, 2),
            FindingScore = Math.Round(impact, 2),
            Fingerprint = $"STATS-003:{serverProfileId:N}:{item.DatabaseName}:{item.ObjectId}:{item.StatisticsId}",
            FirstDetectedAt = capturedAt,
            LastDetectedAt = capturedAt
        });
    }

    private static void AddPersistedSamplingFinding(
        ICollection<Finding> findings,
        Guid serverProfileId,
        DateTimeOffset capturedAt,
        StatisticsSnapshot item,
        decimal modificationPercent,
        double ageDays)
    {
        if (item.Rows < 1_000_000 || !item.PersistedSamplePercent.HasValue ||
            item.PersistedSamplePercent.Value <= 0m || item.PersistedSamplePercent.Value >= 5m)
            return;

        var materialRisk = item.PersistedSamplePercent.Value < 2m ||
                           modificationPercent >= 5m ||
                           ageDays >= 7d;
        if (!materialRisk)
            return;

        var impact = Math.Min(82m,
            45m +
            Math.Min(15m, (5m - item.PersistedSamplePercent.Value) * 4m) +
            Math.Min(15m, modificationPercent) +
            Math.Min(7m, (decimal)Math.Min(ageDays, 7d)));

        findings.Add(new Finding
        {
            ServerProfileId = serverProfileId,
            DatabaseName = item.DatabaseName,
            ObjectName = item.TableName,
            RuleId = "STATS-004",
            Category = "Statistics",
            Severity = impact >= 70m ? FindingSeverity.Medium : FindingSeverity.Low,
            Title = $"Persisted sample stratejisi inceleme adayı: {item.DatabaseName} · {item.TableName} · {item.StatisticsName}",
            TechnicalDescription = $"Statistics için persisted sample %{item.PersistedSamplePercent:N2}; mevcut sample %{item.SamplePercent:N2}, rows={item.Rows:N0}, modifications=%{modificationPercent:N1}, age≈{ageDays:N1} gün, columns=[{item.StatisticsColumns}]. Persisted düşük sample her otomatik güncellemede tekrar kullanılabileceği için histogram doğruluğu planlarla kontrol edilmelidir. Advisor otomatik UPDATE STATISTICS çalıştırmaz.",
            ConfidenceScore = item.PersistedSamplePercent.Value < 2m ? 88m : 80m,
            ImpactScore = Math.Round(impact, 2),
            FindingScore = Math.Round(impact, 2),
            Fingerprint = $"STATS-004:{serverProfileId:N}:{item.DatabaseName}:{item.ObjectId}:{item.StatisticsId}",
            FirstDetectedAt = capturedAt,
            LastDetectedAt = capturedAt
        });
    }

    private static void AddDuplicateStatisticsFindings(
        ICollection<Finding> findings,
        Guid serverProfileId,
        DateTimeOffset capturedAt,
        IReadOnlyCollection<StatisticsSnapshot> statistics)
    {
        var groups = statistics
            .Where(x => x.Rows >= 5000 && !string.IsNullOrWhiteSpace(x.StatisticsColumns))
            .GroupBy(x => new
            {
                Database = x.DatabaseName.ToUpperInvariant(),
                x.ObjectId,
                Columns = NormalizeColumnSequence(x.StatisticsColumns),
                Filter = NormalizeFilter(x.FilterDefinition)
            })
            .Where(g => g.Count() > 1);

        foreach (var group in groups)
        {
            var items = group.ToList();
            var keeper = items
                .OrderBy(x => x.AutoCreated || x.UserCreated ? 1 : 0)
                .ThenByDescending(x => x.LastUpdated)
                .ThenByDescending(x => x.SamplePercent ?? 0m)
                .First();

            foreach (var candidate in items.Where(x => x.StatisticsId != keeper.StatisticsId))
            {
                if (!candidate.AutoCreated && !candidate.UserCreated &&
                    !keeper.AutoCreated && !keeper.UserCreated)
                    continue;

                var impact = Math.Min(72m, 38m + Math.Min(18m, (decimal)Math.Log10(candidate.Rows + 1) * 3m) +
                                            Math.Min(16m, candidate.ModificationCounter / 10000m));

                findings.Add(new Finding
                {
                    ServerProfileId = serverProfileId,
                    DatabaseName = candidate.DatabaseName,
                    ObjectName = candidate.TableName,
                    RuleId = "STATS-005",
                    Category = "Statistics",
                    Severity = FindingSeverity.Low,
                    Title = $"Redundant statistics inceleme adayı: {candidate.DatabaseName} · {candidate.TableName} · {candidate.StatisticsName}",
                    TechnicalDescription = $"{candidate.StatisticsName} ile {keeper.StatisticsName} aynı kolon dizisi [{candidate.StatisticsColumns}] ve eşdeğer filtre kapsamına sahip görünüyor. Aday türü={(candidate.AutoCreated ? "AUTO" : candidate.UserCreated ? "USER" : "INDEX")}, korunacak aday türü={(keeper.AutoCreated ? "AUTO" : keeper.UserCreated ? "USER" : "INDEX")}. Aynı kolonlarda birden fazla statistics her zaman zararlı değildir; index bağı, query plan kullanımı ve oluşturulma amacı doğrulanmadan DROP STATISTICS uygulanmamalıdır.",
                    ConfidenceScore = 82m,
                    ImpactScore = Math.Round(impact, 2),
                    FindingScore = Math.Round(impact, 2),
                    Fingerprint = $"STATS-005:{serverProfileId:N}:{candidate.DatabaseName}:{candidate.ObjectId}:{candidate.StatisticsId}:{keeper.StatisticsId}",
                    FirstDetectedAt = capturedAt,
                    LastDetectedAt = capturedAt
                });
            }
        }
    }

    private static string NormalizeColumnSequence(string? value) =>
        string.Join('|', ParseColumns(value).Select(x => x.ToUpperInvariant()));

    private static string NormalizeFilter(string? value) =>
        string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToUpperInvariant();

    private static string[] ParseColumns(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(x => x.Trim().Trim('[', ']', '"'))
                .Where(x => x.Length > 0)
                .ToArray();
}
