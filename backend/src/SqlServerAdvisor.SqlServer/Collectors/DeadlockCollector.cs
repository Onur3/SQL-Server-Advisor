using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using Dapper;
using Microsoft.Data.SqlClient;
using SqlServerAdvisor.Application.Contracts;
using SqlServerAdvisor.Application.DTOs;
using SqlServerAdvisor.Domain.Entities;

namespace SqlServerAdvisor.SqlServer.Collectors;

public sealed class DeadlockCollector(IMonitoredConnectionStringFactory connectionStringFactory) : IAdvisorCollector
{
    public string Name => "Deadlock";
    public int DefaultIntervalSeconds => 60;
    public int DefaultTimeoutSeconds => 15;

    private const string DeadlockSql = """
        WITH TargetData AS
        (
            SELECT CAST(t.target_data AS xml) AS TargetXml
            FROM sys.dm_xe_session_targets AS t
            JOIN sys.dm_xe_sessions AS s
              ON s.address = t.event_session_address
            WHERE s.name = N'system_health'
              AND t.target_name = N'ring_buffer'
        )
        SELECT TOP (50)
            event_node.value('@timestamp', 'datetimeoffset(3)') AS EventTime,
            CONVERT(nvarchar(max), event_node.query('(data[@name="xml_report"]/value/deadlock)[1]')) AS DeadlockXml
        FROM TargetData
        CROSS APPLY TargetXml.nodes('/RingBufferTarget/event[@name="xml_deadlock_report"]') AS n(event_node)
        WHERE event_node.value('@timestamp', 'datetimeoffset(3)') >= DATEADD(minute, -10, SYSDATETIMEOFFSET())
        ORDER BY EventTime DESC;
        """;

    private const string DatabaseSql = "SELECT database_id AS DatabaseId, name AS DatabaseName FROM sys.databases;";

    public async Task<CollectorBatch> CollectAsync(ServerProfile server, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(connectionStringFactory.Create(server));
        await connection.OpenAsync(cancellationToken);

        var databaseMap = (await connection.QueryAsync<DatabaseRow>(new CommandDefinition(
            DatabaseSql,
            commandTimeout: 5,
            cancellationToken: cancellationToken)))
            .ToDictionary(x => x.DatabaseId, x => x.DatabaseName);

        var rows = (await connection.QueryAsync<DeadlockRow>(new CommandDefinition(
            DeadlockSql,
            commandTimeout: DefaultTimeoutSeconds,
            cancellationToken: cancellationToken))).AsList();

        var capturedAt = DateTimeOffset.UtcNow;
        var events = new List<DeadlockEvent>(rows.Count);

        foreach (var row in rows)
        {
            if (string.IsNullOrWhiteSpace(row.DeadlockXml)) continue;

            try
            {
                var doc = XDocument.Parse(row.DeadlockXml, LoadOptions.None);
                var victim = doc.Descendants("victimProcess")
                    .Select(x => (string?)x.Attribute("id"))
                    .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));

                var dbNames = doc.Descendants("process")
                    .Select(x => (int?)x.Attribute("currentdb"))
                    .Where(x => x.HasValue)
                    .Select(x => databaseMap.GetValueOrDefault(x!.Value, $"dbid:{x.Value}"))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                var objectNames = doc.Descendants()
                    .Select(x => (string?)x.Attribute("objectname"))
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Select(x => x!)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                var statements = doc.Descendants("inputbuf")
                    .Select(x => NormalizeSql(x.Value))
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                var pattern = string.Join("|", dbNames) + "||" + string.Join("|", objectNames) + "||" + string.Join("|", statements);
                if (string.IsNullOrWhiteSpace(pattern.Replace("|", string.Empty)))
                    pattern = row.DeadlockXml;

                var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(pattern)));

                events.Add(new DeadlockEvent
                {
                    ServerProfileId = server.Id,
                    EventTime = row.EventTime,
                    Fingerprint = fingerprint,
                    VictimProcessId = victim,
                    DatabaseNames = dbNames.Length == 0 ? null : string.Join(", ", dbNames),
                    ObjectNames = objectNames.Length == 0 ? null : string.Join(", ", objectNames),
                    DeadlockXml = row.DeadlockXml,
                    CapturedAt = capturedAt
                });
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
            {
                // Ignore malformed ring-buffer entries; a later poll may return a complete event.
            }
        }

        return new CollectorBatch { Deadlocks = events };
    }

    private static string NormalizeSql(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        return string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).Trim().ToUpperInvariant();
    }

    private sealed record DeadlockRow(DateTimeOffset EventTime, string DeadlockXml);
    private sealed record DatabaseRow(int DatabaseId, string DatabaseName);
}
