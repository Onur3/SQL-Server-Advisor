using System.Security.Cryptography;
using System.Text;
using SqlServerAdvisor.Application.Contracts;
using SqlServerAdvisor.Domain.Entities;
using SqlServerAdvisor.Domain.Enums;

namespace SqlServerAdvisor.Analysis.Rules;

public sealed class IndexAdvisorRule : IIndexAnalysisRule
{
    public IReadOnlyCollection<string> RuleIds => ["IDX-001", "IDX-002"];

    public Task<IReadOnlyCollection<Finding>> EvaluateAsync(
        Guid serverProfileId,
        DateTimeOffset capturedAt,
        IReadOnlyCollection<IndexSnapshot> indexes,
        IReadOnlyCollection<MissingIndexSnapshot> missingIndexes,
        CancellationToken cancellationToken)
    {
        var findings = new List<Finding>();

        foreach (var index in indexes)
        {
            if (index.PageCount is null || index.PageCount < 1000 ||
                index.AvgFragmentationPercent is null || index.AvgFragmentationPercent < 30m ||
                index.IsDisabled)
                continue;

            var sizeFactor = Math.Min(25m, (decimal)Math.Log10(Math.Max(1000, index.PageCount.Value)) * 5m);
            var impact = Math.Min(95m, Math.Round(35m + (index.AvgFragmentationPercent.Value - 30m) * 0.8m + sizeFactor, 2));
            var severity = impact >= 80m ? FindingSeverity.High : impact >= 60m ? FindingSeverity.Medium : FindingSeverity.Low;

            findings.Add(new Finding
            {
                ServerProfileId = serverProfileId,
                DatabaseName = index.DatabaseName,
                ObjectName = index.TableName,
                RuleId = "IDX-001",
                Category = "Index",
                Severity = severity,
                Title = $"Yüksek indeks fragmentation: {index.DatabaseName} · {index.IndexName} · %{index.AvgFragmentationPercent:N1}",
                TechnicalDescription = $"{index.TableName} üzerindeki {index.IndexName} indeksi LIMITED fiziksel istatistikte %{index.AvgFragmentationPercent:N1} fragmentation, {index.PageCount:N0} page ve yaklaşık {index.SizeMb:N1} MB boyut gösteriyor. Fragmentation tek başına rebuild gerekçesi değildir; storage latency, page density ve workload ile birlikte değerlendirilmelidir.",
                ConfidenceScore = index.PageCount >= 10000 ? 90m : 80m,
                ImpactScore = impact,
                FindingScore = impact,
                Fingerprint = $"IDX-001:{serverProfileId:N}:{index.DatabaseName}:{index.ObjectId}:{index.IndexId}",
                FirstDetectedAt = capturedAt,
                LastDetectedAt = capturedAt
            });
        }

        foreach (var missing in missingIndexes)
        {
            var reads = missing.UserSeeks + missing.UserScans;
            if (reads < 50 || missing.AvgUserImpact < 70m || missing.ImprovementMeasure < 500000m)
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
                Title = $"Missing-index tuning adayı: {missing.DatabaseName} · {missing.TableName}",
                TechnicalDescription = $"Missing index DMV kaydı {reads:N0} seek/scan, %{missing.AvgUserImpact:N1} tahmini kullanıcı etkisi ve {missing.ImprovementMeasure:N0} improvement measure gösteriyor. Equality=[{missing.EqualityColumns}], Inequality=[{missing.InequalityColumns}], Include=[{missing.IncludedColumns}]. DMV önerisi mevcut overlapping indeksler ve write maliyeti incelenmeden uygulanmamalıdır.",
                ConfidenceScore = reads >= 500 ? 90m : 80m,
                ImpactScore = impact,
                FindingScore = impact,
                Fingerprint = $"IDX-002:{serverProfileId:N}:{hash}",
                FirstDetectedAt = capturedAt,
                LastDetectedAt = capturedAt
            });
        }

        return Task.FromResult<IReadOnlyCollection<Finding>>(findings);
    }
}
