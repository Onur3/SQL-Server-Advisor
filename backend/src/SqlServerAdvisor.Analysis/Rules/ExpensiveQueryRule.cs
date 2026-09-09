using SqlServerAdvisor.Analysis.Scoring;
using SqlServerAdvisor.Application.Contracts;
using SqlServerAdvisor.Domain.Entities;
using SqlServerAdvisor.Domain.Enums;

namespace SqlServerAdvisor.Analysis.Rules;

public sealed class ExpensiveQueryRule : IQueryAnalysisRule
{
    public IReadOnlyCollection<string> RuleIds => ["QRY-001"];

    public Task<IReadOnlyCollection<Finding>> EvaluateAsync(
        Guid serverProfileId,
        DateTimeOffset capturedAt,
        QueryDefinition query,
        QueryRuntimeSnapshot runtime,
        CancellationToken cancellationToken)
    {
        if (!QueryPerformanceScorer.IsSignificant(
                runtime.ExecutionCount,
                runtime.TotalCpuMs,
                runtime.TotalDurationMs,
                runtime.AverageCpuMs,
                runtime.AverageDurationMs,
                runtime.AverageLogicalReads))
            return Task.FromResult<IReadOnlyCollection<Finding>>([]);

        var score = QueryPerformanceScorer.Calculate(
            runtime.ExecutionCount,
            runtime.AverageCpuMs,
            runtime.AverageDurationMs,
            runtime.AverageLogicalReads);

        var severity = score >= 85m ? FindingSeverity.Critical
            : score >= 70m ? FindingSeverity.High
            : score >= 50m ? FindingSeverity.Medium
            : FindingSeverity.Low;

        var confidence = runtime.ExecutionCount >= 20 ? 95m
            : runtime.ExecutionCount >= 5 ? 85m
            : 70m;

        var title = $"Yüksek maliyetli sorgu: {query.DatabaseName} · {runtime.AverageDurationMs:N0} ms · {runtime.AverageLogicalReads:N0} logical read";
        var description = $"Plan cache verisinde sorgu ortalama {runtime.AverageCpuMs:N1} ms CPU, {runtime.AverageDurationMs:N1} ms süre ve {runtime.AverageLogicalReads:N0} logical read tüketiyor. ExecutionCount={runtime.ExecutionCount:N0}, TotalCpu={runtime.TotalCpuMs:N0} ms. Bu bulgu salt-okunur DMV verisinden üretilmiştir.";

        IReadOnlyCollection<Finding> result =
        [
            new Finding
            {
                ServerProfileId = serverProfileId,
                QueryId = query.Id,
                DatabaseName = query.DatabaseName,
                ObjectName = query.ObjectName,
                RuleId = "QRY-001",
                Category = "QueryPerformance",
                Severity = severity,
                Title = title,
                TechnicalDescription = description,
                ConfidenceScore = confidence,
                ImpactScore = score,
                FindingScore = score,
                Fingerprint = $"QRY-001:{serverProfileId:N}:{query.DatabaseName}:{query.QueryHash}",
                FirstDetectedAt = capturedAt,
                LastDetectedAt = capturedAt
            }
        ];

        return Task.FromResult(result);
    }
}
