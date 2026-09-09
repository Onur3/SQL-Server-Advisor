using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using SqlServerAdvisor.Analysis.Rules;
using SqlServerAdvisor.Analysis.Recommendations;
using SqlServerAdvisor.Domain.Entities;
using SqlServerAdvisor.Infrastructure.Data;

var count = 0;
void Check(bool condition, string name) { if (!condition) throw new Exception(name); Console.WriteLine($"PASS {name}"); count++; }
var now = DateTimeOffset.UtcNow;
WaitSnapshot Row(string type, long total, int seconds = 0) => new() { WaitType = type, WaitTimeMs = total,
    WaitingTasks = total, SignalWaitTimeMs = total / 10, CapturedAt = now.AddSeconds(seconds), SqlServerStartTime = new DateTime(2026, 1, 1) };
var rows = new List<WaitSnapshot> { Row("WRITELOG", 900000) };
Check(!WaitBlockingAnalysis.ApplyDeltas(rows, []) && rows[0].DeltaWaitTimeMs == 0, "first sample baseline");
var previous = rows;
rows = [Row("WRITELOG", 960000, 60)];
Check(WaitBlockingAnalysis.ApplyDeltas(rows, previous) && rows[0].DeltaWaitTimeMs == 60000 && rows[0].DeltaSignalWaitTimeMs == 6000, "interval deltas");
Check(WaitBlockingAnalysis.Waits(rows).Count == 1, "actionable pressure finding");
foreach (var type in new[] { "SLEEP_TASK", "WAITFOR", "CXCONSUMER", "BROKER_RECEIVE_WAITFOR", "LAZYWRITER_SLEEP", "UNKNOWN_WAIT" })
    Check(!WaitBlockingAnalysis.IsActionable(type), $"noise excluded {type}");
rows = [Row("WRITELOG", 1, 60)];
Check(!WaitBlockingAnalysis.ApplyDeltas(rows, previous) && rows[0].IsBaseline, "counter reset");
rows = [Row("WRITELOG", 960000, 60)]; rows[0].SqlServerStartTime = new DateTime(2026, 2, 1);
Check(!WaitBlockingAnalysis.ApplyDeltas(rows, previous), "restart with higher counters");
rows = [Row("WRITELOG", 990000, 600)];
Check(!WaitBlockingAnalysis.ApplyDeltas(rows, previous), "stale baseline");
rows = [Row("WRITELOG", 900001, 60)];
WaitBlockingAnalysis.ApplyDeltas(rows, previous);
Check(WaitBlockingAnalysis.Waits(rows).Count == 0, "tiny waits do not alert");
Check(WaitBlockingAnalysis.Blocking(Guid.Empty, [], now).Count == 0, "empty blocking is healthy");
Check(WaitBlockingAnalysis.Blocking(Guid.Empty, [new() { WaitTimeMs = 14999 }], now).Count == 0, "short blocking suppressed");
var finding = WaitBlockingAnalysis.Blocking(Guid.Empty, [new() { WaitTimeMs = 15000, BlockingSessionId = -2 }], now).Single();
Check(finding.RuleId == "BLK-002", "long blocking includes special owners");
var factory = new RecommendationFactory();
foreach (var rule in new[] { "WAIT-001", "BLK-002" }) {
    finding.RuleId = rule;
    var rec = factory.Create(finding)!;
    Check(!rec.CanExecute && rec.ScriptText!.StartsWith("SELECT"), $"read-only recommendation {rule}");
}
using var db = new AdvisorDbContext(new DbContextOptionsBuilder<AdvisorDbContext>().UseSqlServer("Server=unused;Database=unused;Integrated Security=true").Options);
var entity = db.Model.FindEntityType(typeof(WaitSnapshot))!;
Check(entity.FindProperty(nameof(WaitSnapshot.SignalWaitTimeMs))!.GetColumnName(StoreObjectIdentifier.Table("Wait", "SNP")) == "SignalWaitMs", "wait EF column alignment");
var root = args.Length > 0 ? args[0] : ".";
foreach (var file in Directory.GetFiles(Path.Combine(root, "database"), "*.sql")) {
    using var reader = File.OpenText(file);
    new TSql150Parser(true).Parse(reader, out var errors);
    Check(errors.Count == 0, $"SQL Server 2019 syntax {Path.GetFileName(file)}: {string.Join(";", errors.Select(x => x.Message))}");
}
Console.WriteLine($"{count} checks passed");

var integration = Environment.GetEnvironmentVariable("SQLADVISOR_TEST_CONNECTION");
if (!string.IsNullOrEmpty(integration))
{
    await using var connection = new Microsoft.Data.SqlClient.SqlConnection(integration);
    await connection.OpenAsync();
    for (var pass = 0; pass < 2; pass++)
        foreach (var file in Directory.GetFiles(Path.Combine(root, "database"), "*.sql").Where(x => !x.Contains("template")).Order())
            foreach (var batch in System.Text.RegularExpressions.Regex.Split(File.ReadAllText(file), @"^GO\s*$", System.Text.RegularExpressions.RegexOptions.Multiline | System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(batch)) continue;
                using var command = connection.CreateCommand(); command.CommandText = batch; command.CommandTimeout = 60;
                await command.ExecuteNonQueryAsync();
            }
    var builder = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(integration) { InitialCatalog = "SQLAdvisor" };
    await using var actual = new AdvisorDbContext(new DbContextOptionsBuilder<AdvisorDbContext>().UseSqlServer(builder.ConnectionString).Options);
    var server = new ServerProfile { Id = Guid.NewGuid(), Name = "integration-" + Guid.NewGuid(), Host = "localhost" };
    actual.Servers.Add(server);
    await actual.SaveChangesAsync();
    actual.WaitSnapshots.Add(new WaitSnapshot { ServerProfileId = server.Id, WaitType = "WRITELOG", CapturedAt = now, IsBaseline = true });
    actual.BlockingEvents.Add(new BlockingEvent { ServerProfileId = server.Id, SessionId = 51, BlockingSessionId = -2, CapturedAt = now, BlockerStatus = "sleeping" });
    await actual.SaveChangesAsync(); actual.ChangeTracker.Clear();
    Check(await actual.WaitSnapshots.AnyAsync(x => x.ServerProfileId == server.Id && x.IsBaseline), "SQL migration + EF wait roundtrip");
    Check((await actual.BlockingEvents.SingleAsync(x => x.ServerProfileId == server.Id)).BlockingSessionId == -2, "SQL migration + EF blocking roundtrip");
    Check(true, "all migrations applied twice on SQL Server");
    using (var grant = connection.CreateCommand())
    {
        grant.CommandText = "USE master; CREATE LOGIN AdvisorTestReader WITH PASSWORD='ReadOnly_CI_947!pass'; GRANT VIEW SERVER STATE TO AdvisorTestReader;";
        await grant.ExecuteNonQueryAsync();
    }
    var readBuilder = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(integration)
        { InitialCatalog = "master", UserID = "AdvisorTestReader", Password = "ReadOnly_CI_947!pass" };
    var collector = new SqlServerAdvisor.SqlServer.Collectors.WaitBlockingCollector(new TestConnectionFactory(readBuilder.ConnectionString));
    Check((await collector.WaitsAsync(server, 5, default)).Count > 0, "wait collector with read-only login");
    Check((await collector.BlockingAsync(server, 5, default)).Count == 0, "blocking collector empty sample with read-only login");
    using (var revoke = connection.CreateCommand())
    {
        revoke.CommandText = "USE master; REVOKE VIEW SERVER STATE FROM AdvisorTestReader;";
        await revoke.ExecuteNonQueryAsync();
    }
    var denied = false;
    try { await collector.BlockingAsync(server, 5, default); } catch (UnauthorizedAccessException) { denied = true; }
    Check(denied, "missing visibility cannot masquerade as healthy empty blocking");
}

sealed class TestConnectionFactory(string connectionString) : SqlServerAdvisor.Application.Contracts.IMonitoredConnectionStringFactory
{
    public string Create(ServerProfile server) => connectionString;
}
