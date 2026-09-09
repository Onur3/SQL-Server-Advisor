namespace SqlServerAdvisor.Analysis.Scoring;

public static class QueryPerformanceScorer
{
    public static decimal Calculate(
        long executionCount,
        decimal averageCpuMs,
        decimal averageDurationMs,
        decimal averageLogicalReads)
    {
        var cpu = Math.Min(35m, averageCpuMs / 500m * 35m);
        var duration = Math.Min(30m, averageDurationMs / 2000m * 30m);
        var reads = Math.Min(25m, averageLogicalReads / 100000m * 25m);
        var frequency = Math.Min(10m, (decimal)Math.Log10(Math.Max(1, executionCount) + 1) * 5m);
        return Math.Min(100m, Math.Round(cpu + duration + reads + frequency, 2));
    }

    public static bool IsSignificant(
        long executionCount,
        decimal totalCpuMs,
        decimal totalDurationMs,
        decimal averageCpuMs,
        decimal averageDurationMs,
        decimal averageLogicalReads)
    {
        var expensiveAverage = averageCpuMs >= 500m || averageDurationMs >= 2000m || averageLogicalReads >= 100000m;
        var enoughEvidence = executionCount >= 3 || totalCpuMs >= 10000m || totalDurationMs >= 30000m;
        return expensiveAverage && enoughEvidence;
    }
}
