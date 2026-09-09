using SqlServerAdvisor.Application.Contracts;
using SqlServerAdvisor.Domain.Entities;
using SqlServerAdvisor.Domain.Enums;

namespace SqlServerAdvisor.Analysis.Rules;

public sealed class StatisticsAdvisorRule : IStatisticsAnalysisRule
{
    public IReadOnlyCollection<string> RuleIds => ["STATS-001"];

    public Task<IReadOnlyCollection<Finding>> EvaluateAsync(
        Guid serverProfileId,
        DateTimeOffset capturedAt,
        IReadOnlyCollection<StatisticsSnapshot> statistics,
        CancellationToken cancellationToken)
    {
        var findings = new List<Finding>();

        foreach (var item in statistics)
        {
            if (item.Rows < 5000 || item.ModificationCounter < 5000)
                continue;

            var modificationPercent = item.Rows == 0 ? 0m : item.ModificationCounter * 100m / item.Rows;
            var ageDays = item.LastUpdated.HasValue
                ? Math.Max(0d, (capturedAt.UtcDateTime - DateTime.SpecifyKind(item.LastUpdated.Value, DateTimeKind.Utc)).TotalDays)
                : 365d;

            var materiallyChanged = modificationPercent >= 20m ||
                                    (modificationPercent >= 10m && ageDays >= 1d) ||
                                    (modificationPercent >= 5m && item.Rows >= 1_000_000 && ageDays >= 3d);

            if (!materiallyChanged)
                continue;

            var impact = Math.Min(95m,
                40m +
                Math.Min(30m, modificationPercent) +
                Math.Min(15m, (decimal)Math.Log10(Math.Max(5000, item.Rows)) * 2m) +
                Math.Min(10m, (decimal)Math.Min(10d, ageDays)));

            var severity = impact >= 85m ? FindingSeverity.High : FindingSeverity.Medium;
            var confidence = item.ModificationCounter >= 50_000 ? 95m : 85m;

            findings.Add(new Finding
            {
                ServerProfileId = serverProfileId,
                DatabaseName = item.DatabaseName,
                ObjectName = item.TableName,
                RuleId = "STATS-001",
                Category = "Statistics",
                Severity = severity,
                Title = $"Yoğun değişen statistics: {item.DatabaseName} · {item.StatisticsName}",
                TechnicalDescription = $"{item.TableName} üzerindeki {item.StatisticsName} statistics kaydı {item.Rows:N0} satır, {item.ModificationCounter:N0} modification (%{modificationPercent:N1}) ve yaklaşık {ageDays:N1} günlük stats yaşı gösteriyor. SamplePercent={(item.SamplePercent.HasValue ? $"%{item.SamplePercent:N1}" : "bilinmiyor")}. Bu bulgu otomatik UPDATE STATISTICS komutu çalıştırmaz; execution plan ve auto-update davranışı ile birlikte değerlendirilmelidir.",
                ConfidenceScore = confidence,
                ImpactScore = Math.Round(impact, 2),
                FindingScore = Math.Round(impact, 2),
                Fingerprint = $"STATS-001:{serverProfileId:N}:{item.DatabaseName}:{item.ObjectId}:{item.StatisticsId}",
                FirstDetectedAt = capturedAt,
                LastDetectedAt = capturedAt
            });
        }

        return Task.FromResult<IReadOnlyCollection<Finding>>(findings);
    }
}
