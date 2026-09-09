using SqlServerAdvisor.Application.Contracts;
using SqlServerAdvisor.Domain.Entities;
using SqlServerAdvisor.Domain.Enums;

namespace SqlServerAdvisor.Analysis.Rules;

public sealed class StatisticsAdvisorRule : IStatisticsAnalysisRule
{
    public IReadOnlyCollection<string> RuleIds => ["STATS-001", "STATS-002", "STATS-003"];

    public Task<IReadOnlyCollection<Finding>> EvaluateAsync(
        Guid serverProfileId,
        DateTimeOffset capturedAt,
        IReadOnlyCollection<StatisticsSnapshot> statistics,
        CancellationToken cancellationToken)
    {
        var findings = new List<Finding>();

        foreach (var item in statistics)
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
        }

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
            TechnicalDescription = $"{type} statistics {item.Rows:N0} satır üzerinde {item.ModificationCounter:N0} modification (%{modificationPercent:N1}) ve yaklaşık {ageDays:N1} günlük yaş gösteriyor. Sample={(item.SamplePercent.HasValue ? $"%{item.SamplePercent:N1}" : "bilinmiyor")}, Filtered={(item.HasFilter ? "Evet" : "Hayır")}, NoRecompute={(item.NoRecompute ? "Evet" : "Hayır")}. Execution plan cardinality tahminleriyle birlikte değerlendirilmelidir.",
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
            TechnicalDescription = $"Statistics NORECOMPUTE durumda ve {item.ModificationCounter:N0} satır değişimi (%{modificationPercent:N1}) birikmiş. Yaş yaklaşık {ageDays:N1} gün. Otomatik statistics güncellemesi bu nesne için devre dışı olabileceğinden estimated/actual row sapmaları ve ilgili sorgu planları özellikle kontrol edilmelidir.",
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
            Title = $"Statistics sample inceleme adayı: {item.DatabaseName} · {item.TableName} · {item.StatisticsName}",
            TechnicalDescription = $"Büyük tablo statistics'i {item.Rows:N0} satır üzerinde %{item.SamplePercent:N2} sample ({item.RowsSampled:N0} satır) ve %{modificationPercent:N1} modification gösteriyor. Düşük sample tek başına hata değildir; histogram kalitesi ve estimated/actual row farkı doğrulanmadan FULLSCAN veya yüksek sample uygulanmamalıdır.",
            ConfidenceScore = 78m,
            ImpactScore = Math.Round(impact, 2),
            FindingScore = Math.Round(impact, 2),
            Fingerprint = $"STATS-003:{serverProfileId:N}:{item.DatabaseName}:{item.ObjectId}:{item.StatisticsId}",
            FirstDetectedAt = capturedAt,
            LastDetectedAt = capturedAt
        });
    }
}
