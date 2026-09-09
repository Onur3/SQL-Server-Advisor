using SqlServerAdvisor.Domain.Entities;

namespace SqlServerAdvisor.Analysis.Scoring;

public static class ServerHealthScorer
{
    public static decimal? Calculate(ServerSnapshot snapshot)
    {
        var availableSignals = 0;
        decimal penalty = 0;

        if (snapshot.SessionMetricsAvailable)
        {
            availableSignals++;
            if (snapshot.BlockedRequests > 0)
                penalty += Math.Min(35, snapshot.BlockedRequests * 4);
        }

        if (snapshot.CpuMetricsAvailable && snapshot.SqlCpuPercent is not null)
        {
            availableSignals++;
            if (snapshot.SqlCpuPercent >= 90) penalty += 25;
            else if (snapshot.SqlCpuPercent >= 80) penalty += 15;
            else if (snapshot.SqlCpuPercent >= 70) penalty += 8;
        }

        if (snapshot.MemoryMetricsAvailable && snapshot.PhysicalMemoryMb > 0 && snapshot.AvailableMemoryMb is not null)
        {
            availableSignals++;
            var availablePercent = (decimal)snapshot.AvailableMemoryMb.Value / snapshot.PhysicalMemoryMb.Value * 100m;
            if (availablePercent < 5) penalty += 20;
            else if (availablePercent < 10) penalty += 10;
        }

        if (availableSignals == 0) return null;
        return Math.Max(0, Math.Round(100m - penalty, 2));
    }

    public static int DataCoveragePercent(ServerSnapshot snapshot)
    {
        var signals = (snapshot.SessionMetricsAvailable ? 1 : 0)
                    + (snapshot.MemoryMetricsAvailable ? 1 : 0)
                    + (snapshot.CpuMetricsAvailable ? 1 : 0);
        return (int)Math.Round(signals / 3m * 100m, MidpointRounding.AwayFromZero);
    }
}
