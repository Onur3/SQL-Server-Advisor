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

        var databaseResolved = !IsUnresolvedDatabaseName(query.DatabaseName);
        var target = query.ObjectName is not null
            ? (databaseResolved ? $"{query.DatabaseName} · {query.ObjectName}" : query.ObjectName)
            : (databaseResolved ? query.DatabaseName : "Ad-hoc sorgu");

        var title = $"Pahalı sorgu: {target} · {runtime.AverageDurationMs:N0} ms · {runtime.AverageLogicalReads:N0} logical read";
        var databaseContext = databaseResolved
            ? $"Veritabanı={query.DatabaseName}."
            : "Plan cache bu ad-hoc sorgu için veritabanı bağlamını kesin çözemedi; SQL metni ve execution plan üzerinden nesne bağlamı doğrulanmalıdır.";
        var description = $"{databaseContext} Ortalama CPU={runtime.AverageCpuMs:N1} ms, süre={runtime.AverageDurationMs:N1} ms, logical read={runtime.AverageLogicalReads:N0}, execution={runtime.ExecutionCount:N0}, total CPU={runtime.TotalCpuMs:N0} ms. Bu bulgu salt-okunur DMV verisinden üretilmiştir.";

        IReadOnlyCollection<Finding> result =
        [
            new Finding
            {
                ServerProfileId = serverProfileId,
                QueryId = query.Id,
                DatabaseName = databaseResolved ? query.DatabaseName : null,
                ObjectName = query.ObjectName,
                RuleId = "QRY-001",
                Category = "QueryPerformance",
                Severity = severity,
                Title = title,
                TechnicalDescription = description,
                ConfidenceScore = confidence,
                ImpactScore = score,
                FindingScore = score,
                Fingerprint = $"QRY-001:{serverProfileId:N}:{query.QueryHash}",
                FirstDetectedAt = capturedAt,
                LastDetectedAt = capturedAt
            }
        ];

        return Task.FromResult(result);
    }

    private static bool IsUnresolvedDatabaseName(string? databaseName) =>
        string.IsNullOrWhiteSpace(databaseName) ||
        databaseName.Equals("<unknown>", StringComparison.OrdinalIgnoreCase) ||
        databaseName.Contains("DB bağlamı yok", StringComparison.OrdinalIgnoreCase);
}
