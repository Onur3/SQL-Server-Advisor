using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using SqlServerAdvisor.Application.DTOs;
using SqlServerAdvisor.Application.Presentation;
using SqlServerAdvisor.Domain.Entities;
using SqlServerAdvisor.Infrastructure.Data;

namespace SqlServerAdvisor.Api.Controllers;

[ApiController]
[Route("api/recommendations")]
public sealed class RecommendationsController(AdvisorDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyCollection<RecommendationListItemDto>>> Get(
        [FromQuery] Guid? serverId,
        [FromQuery] string? status,
        CancellationToken cancellationToken)
    {
        var source =
            from recommendation in db.Recommendations.AsNoTracking()
            join finding in db.Findings.AsNoTracking()
                on recommendation.FindingId equals finding.Id
            join server in db.Servers.AsNoTracking()
                on finding.ServerProfileId equals server.Id
            select new { recommendation, finding, ServerName = server.Name };

        if (serverId.HasValue)
            source = source.Where(x => x.finding.ServerProfileId == serverId.Value);

        if (!string.IsNullOrWhiteSpace(status) && !status.Equals("All", StringComparison.OrdinalIgnoreCase))
            source = source.Where(x => x.recommendation.Status == status);

        var rows = await source
            .OrderByDescending(x => x.recommendation.Status == "New")
            .ThenByDescending(x => x.recommendation.PriorityScore)
            .ThenByDescending(x => x.recommendation.CreatedAt)
            .Take(500)
            .ToListAsync(cancellationToken);

        var queryIds = rows
            .Where(x => x.finding.QueryId.HasValue)
            .Select(x => x.finding.QueryId!.Value)
            .Distinct()
            .ToArray();

        var queryDefinitions = queryIds.Length == 0
            ? new Dictionary<long, QueryDefinition>()
            : await db.Queries.AsNoTracking()
                .Where(x => queryIds.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id, cancellationToken);

        var planQueryIds = queryIds.Length == 0
            ? new HashSet<long>()
            : (await db.QueryPlans.AsNoTracking()
                .Where(x => queryIds.Contains(x.QueryId) && x.PlanXml != string.Empty)
                .Select(x => x.QueryId)
                .Distinct()
                .ToListAsync(cancellationToken))
                .ToHashSet();

        var items = rows.Select(x =>
        {
            var narrative = AdvisorNarrativeCatalog.For(x.finding.RuleId);
            queryDefinitions.TryGetValue(x.finding.QueryId ?? 0, out var queryDefinition);

            var effectiveDatabase = NormalizeDatabase(x.finding.DatabaseName ?? queryDefinition?.DatabaseName);
            var effectiveObject = x.finding.ObjectName ?? queryDefinition?.ObjectName;
            var queryText = queryDefinition is null ? null : Truncate(queryDefinition.StatementText, 6000);

            return new RecommendationListItemDto(
                x.recommendation.Id,
                x.recommendation.FindingId,
                x.finding.ServerProfileId,
                x.ServerName,
                x.finding.QueryId,
                queryDefinition?.QueryHash,
                queryText,
                x.finding.QueryId.HasValue && planQueryIds.Contains(x.finding.QueryId.Value),
                effectiveDatabase,
                effectiveObject,
                AdvisorNarrativeCatalog.BuildScope(x.ServerName, effectiveDatabase, effectiveObject),
                x.finding.RuleId,
                narrative.RuleName,
                x.finding.Category,
                narrative.CategoryLabel,
                narrative.Icon,
                (int)x.finding.Severity,
                x.finding.Title,
                x.finding.TechnicalDescription,
                narrative.WhatWasFound,
                narrative.WhyItMatters,
                x.recommendation.PriorityScore,
                x.recommendation.Title,
                x.recommendation.Explanation,
                x.recommendation.ExpectedBenefit,
                x.recommendation.RiskLevel,
                x.recommendation.ConfidenceScore,
                x.recommendation.RecommendedAction,
                x.recommendation.ScriptText,
                x.recommendation.CanExecute,
                x.recommendation.Status,
                x.recommendation.CreatedAt);
        }).ToList();

        return Ok(items);
    }

    [HttpGet("{id:long}/ai-prompt")]
    public async Task<ActionResult<AiPromptDto>> GetAiPrompt(long id, CancellationToken cancellationToken)
    {
        var row = await (
            from recommendation in db.Recommendations.AsNoTracking()
            join finding in db.Findings.AsNoTracking() on recommendation.FindingId equals finding.Id
            join server in db.Servers.AsNoTracking() on finding.ServerProfileId equals server.Id
            where recommendation.Id == id
            select new { recommendation, finding, server })
            .FirstOrDefaultAsync(cancellationToken);

        if (row is null)
            return NotFound();

        QueryDefinition? query = null;
        QueryPlan? plan = null;
        QueryRuntimeSnapshot? runtime = null;
        if (row.finding.QueryId.HasValue)
        {
            var queryId = row.finding.QueryId.Value;
            query = await db.Queries.AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == queryId, cancellationToken);
            plan = await db.QueryPlans.AsNoTracking()
                .Where(x => x.QueryId == queryId && x.PlanXml != string.Empty)
                .OrderByDescending(x => x.LastSeenAt)
                .FirstOrDefaultAsync(cancellationToken);
            runtime = await db.QueryRuntimeSnapshots.AsNoTracking()
                .Where(x => x.QueryId == queryId)
                .OrderByDescending(x => x.CapturedAt)
                .FirstOrDefaultAsync(cancellationToken);
        }

        var serverSnapshot = await db.ServerSnapshots.AsNoTracking()
            .Where(x => x.ServerProfileId == row.finding.ServerProfileId)
            .OrderByDescending(x => x.CapturedAt)
            .FirstOrDefaultAsync(cancellationToken);

        var databaseName = NormalizeDatabase(row.finding.DatabaseName ?? query?.DatabaseName);
        var objectContext = row.finding.ObjectName ?? query?.ObjectName;
        var targetTables = ParseTargetTables(objectContext);

        var indexContext = await LoadIndexContextAsync(
            row.finding.ServerProfileId,
            databaseName,
            targetTables,
            cancellationToken);
        var statisticsContext = await LoadStatisticsContextAsync(
            row.finding.ServerProfileId,
            databaseName,
            targetTables,
            cancellationToken);
        var missingIndexContext = await LoadMissingIndexContextAsync(
            row.finding.ServerProfileId,
            databaseName,
            targetTables,
            cancellationToken);
        var workloadContext = await LoadWorkloadContextAsync(
            row.finding.ServerProfileId,
            databaseName,
            targetTables,
            cancellationToken);

        var narrative = AdvisorNarrativeCatalog.For(row.finding.RuleId);
        var prompt = BuildAiPrompt(
            row.recommendation,
            row.finding,
            row.server,
            serverSnapshot,
            query,
            runtime,
            plan,
            databaseName,
            objectContext,
            narrative,
            indexContext,
            statisticsContext,
            missingIndexContext,
            workloadContext);

        return Ok(new AiPromptDto(prompt, DateTimeOffset.UtcNow));
    }

    private async Task<List<IndexSnapshot>> LoadIndexContextAsync(
        Guid serverId,
        string? databaseName,
        IReadOnlyCollection<string> targetTables,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(databaseName)) return [];

        var capturedAt = await db.IndexSnapshots.AsNoTracking()
            .Where(x => x.ServerProfileId == serverId && x.DatabaseName == databaseName)
            .MaxAsync(x => (DateTimeOffset?)x.CapturedAt, cancellationToken);
        if (!capturedAt.HasValue) return [];

        var indexes = await db.IndexSnapshots.AsNoTracking()
            .Where(x => x.ServerProfileId == serverId && x.DatabaseName == databaseName && x.CapturedAt == capturedAt.Value)
            .ToListAsync(cancellationToken);

        return targetTables.Count == 0
            ? indexes.OrderByDescending(x => x.UserSeeks + x.UserScans + x.UserLookups).Take(15).ToList()
            : indexes.Where(x => TableMatches(x.TableName, targetTables)).Take(40).ToList();
    }

    private async Task<List<StatisticsSnapshot>> LoadStatisticsContextAsync(
        Guid serverId,
        string? databaseName,
        IReadOnlyCollection<string> targetTables,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(databaseName)) return [];

        var capturedAt = await db.StatisticsSnapshots.AsNoTracking()
            .Where(x => x.ServerProfileId == serverId && x.DatabaseName == databaseName)
            .MaxAsync(x => (DateTimeOffset?)x.CapturedAt, cancellationToken);
        if (!capturedAt.HasValue) return [];

        var stats = await db.StatisticsSnapshots.AsNoTracking()
            .Where(x => x.ServerProfileId == serverId && x.DatabaseName == databaseName && x.CapturedAt == capturedAt.Value)
            .ToListAsync(cancellationToken);

        return targetTables.Count == 0
            ? stats.OrderByDescending(x => x.ModificationCounter).Take(15).ToList()
            : stats.Where(x => TableMatches(x.TableName, targetTables)).Take(40).ToList();
    }

    private async Task<List<MissingIndexSnapshot>> LoadMissingIndexContextAsync(
        Guid serverId,
        string? databaseName,
        IReadOnlyCollection<string> targetTables,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(databaseName)) return [];

        var capturedAt = await db.MissingIndexSnapshots.AsNoTracking()
            .Where(x => x.ServerProfileId == serverId && x.DatabaseName == databaseName)
            .MaxAsync(x => (DateTimeOffset?)x.CapturedAt, cancellationToken);
        if (!capturedAt.HasValue) return [];

        var missing = await db.MissingIndexSnapshots.AsNoTracking()
            .Where(x => x.ServerProfileId == serverId && x.DatabaseName == databaseName && x.CapturedAt == capturedAt.Value)
            .ToListAsync(cancellationToken);

        return targetTables.Count == 0
            ? missing.OrderByDescending(x => x.ImprovementMeasure).Take(10).ToList()
            : missing.Where(x => TableMatches(x.TableName, targetTables))
                .OrderByDescending(x => x.ImprovementMeasure)
                .Take(20)
                .ToList();
    }

    private async Task<List<WorkloadPromptRow>> LoadWorkloadContextAsync(
        Guid serverId,
        string? databaseName,
        IReadOnlyCollection<string> targetTables,
        CancellationToken cancellationToken)
    {
        var connectionString = db.Database.GetConnectionString();
        if (string.IsNullOrWhiteSpace(connectionString)) return [];

        var rows = new List<WorkloadPromptRow>();
        try
        {
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT TOP (100)
                    ServerProfileId, DatabaseName, FileName, ReferencedObjects,
                    PredicateColumns, QueryText, ParseMessage
                FROM QRY.WorkloadFile
                WHERE IsActive = 1
                  AND (ServerProfileId IS NULL OR ServerProfileId = @ServerId)
                ORDER BY LastScannedAt DESC;
                """;
            command.Parameters.AddWithValue("@ServerId", serverId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var dbName = reader.IsDBNull(1) ? null : reader.GetString(1);
                if (!string.IsNullOrWhiteSpace(databaseName) &&
                    !string.IsNullOrWhiteSpace(dbName) &&
                    !dbName.Equals(databaseName, StringComparison.OrdinalIgnoreCase))
                    continue;

                var referencedObjects = reader.IsDBNull(3) ? string.Empty : reader.GetString(3);
                if (targetTables.Count > 0 && !ReferencesAnyTable(referencedObjects, targetTables))
                    continue;

                rows.Add(new WorkloadPromptRow(
                    reader.GetString(2),
                    dbName,
                    referencedObjects,
                    reader.IsDBNull(4) ? string.Empty : reader.GetString(4),
                    Truncate(reader.GetString(5), 3000),
                    reader.IsDBNull(6) ? null : reader.GetString(6)));
                if (rows.Count >= 5) break;
            }
        }
        catch (SqlException ex) when (ex.Number == 208)
        {
            // Workload migration not deployed yet.
        }

        return rows;
    }

    private static string BuildAiPrompt(
        Recommendation recommendation,
        Finding finding,
        ServerProfile server,
        ServerSnapshot? serverSnapshot,
        QueryDefinition? query,
        QueryRuntimeSnapshot? runtime,
        QueryPlan? plan,
        string? databaseName,
        string? objectContext,
        RuleNarrative narrative,
        IReadOnlyCollection<IndexSnapshot> indexes,
        IReadOnlyCollection<StatisticsSnapshot> statistics,
        IReadOnlyCollection<MissingIndexSnapshot> missingIndexes,
        IReadOnlyCollection<WorkloadPromptRow> workloadFiles)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Bir SQL Server performans uzmanı gibi aşağıdaki SQL Server Advisor bulgusunu bağımsız olarak değerlendir.");
        sb.AppendLine("Advisor'ın önerisini doğru kabul etme; kanıtları sorgula, olası kök nedeni belirt ve daha güvenli/iyi bir çözüm varsa öner.");
        sb.AppendLine("Üretimde hiçbir komutu otomatik çalıştırmayacağım. Değişiklik önerirsen önce test, doğrulama ve geri dönüş planı ver.");
        sb.AppendLine();
        sb.AppendLine("## İSTEDİĞİM ÇIKTI");
        sb.AppendLine("1. Problemin kısa teşhisi ve muhtemel kök neden.");
        sb.AppendLine("2. Advisor önerisinin doğru/eksik/yanlış yönleri.");
        sb.AppendLine("3. Sorgu varsa execution plan, logical read, CPU ve duration yorumun.");
        sb.AppendLine("4. Mevcut indeksleri karşılaştır: mevcut indeksi kullan / konsolide et / yeni indeks oluştur / indeks değişikliği yapma kararlarından birini net seç.");
        sb.AppendLine("5. Statistics etkisini değerlendir.");
        sb.AppendLine("6. Yeni indeks gerekiyorsa key/include sırasını gerekçelendir ve test amaçlı CREATE INDEX taslağı ver; duplicate/overlap ve write maliyetini dikkate al.");
        sb.AppendLine("7. Uygulamadan önce ve sonra ölçmem gereken metrikleri ve rollback planını ver.");
        sb.AppendLine("8. Eksik kanıt varsa hangi ek DMV/plan bilgisinin gerekli olduğunu açıkça yaz.");
        sb.AppendLine();

        sb.AppendLine("## SUNUCU");
        sb.AppendLine($"Advisor profili: {server.Name}");
        sb.AppendLine($"SQL Server: {serverSnapshot?.ServerName ?? server.Host}");
        sb.AppendLine($"Sürüm: {serverSnapshot?.ProductVersion ?? "bilinmiyor"}");
        sb.AppendLine($"Edition: {serverSnapshot?.Edition ?? "bilinmiyor"}");
        if (serverSnapshot is not null)
        {
            sb.AppendLine($"Son health: SQL CPU={serverSnapshot.SqlCpuPercent?.ToString() ?? "?"}%, AvailableMemory={serverSnapshot.AvailableMemoryMb?.ToString() ?? "?"} MB, ActiveRequests={serverSnapshot.ActiveRequests}, BlockedRequests={serverSnapshot.BlockedRequests}");
        }
        sb.AppendLine();

        sb.AppendLine("## BULGU");
        sb.AppendLine($"Finding #{finding.Id} / Rule={finding.RuleId} ({narrative.RuleName})");
        sb.AppendLine($"Kategori: {narrative.CategoryLabel}");
        sb.AppendLine($"Severity: {finding.Severity}");
        sb.AppendLine($"Öncelik/Etki/Güven: {finding.FindingScore:N0} / {finding.ImpactScore:N0} / %{finding.ConfidenceScore:N0}");
        sb.AppendLine($"Veritabanı: {databaseName ?? "çözümlenemedi"}");
        sb.AppendLine($"Nesne(ler): {objectContext ?? "plan/sorgu üzerinden doğrulanmalı"}");
        sb.AppendLine($"Başlık: {finding.Title}");
        sb.AppendLine($"Teknik kanıt: {finding.TechnicalDescription}");
        sb.AppendLine();

        sb.AppendLine("## ADVISOR ÖNERİSİ");
        sb.AppendLine($"Recommendation #{recommendation.Id}: {recommendation.Title}");
        sb.AppendLine($"Açıklama: {recommendation.Explanation}");
        sb.AppendLine($"Önerilen aksiyon: {recommendation.RecommendedAction}");
        sb.AppendLine($"Beklenen fayda: {recommendation.ExpectedBenefit}; Risk: {recommendation.RiskLevel}; Güven: %{recommendation.ConfidenceScore:N0}");
        sb.AppendLine("Not: Advisor CanExecute=false; production değişikliği otomatik uygulanmaz.");
        if (!string.IsNullOrWhiteSpace(recommendation.ScriptText))
        {
            sb.AppendLine("Advisor tanı SQL'i:");
            sb.AppendLine("```sql");
            sb.AppendLine(Truncate(recommendation.ScriptText, 6000));
            sb.AppendLine("```");
        }
        sb.AppendLine();

        if (query is not null)
        {
            sb.AppendLine("## SORGU");
            sb.AppendLine($"QueryId: {query.Id}");
            sb.AppendLine($"QueryHash: {query.QueryHash}");
            sb.AppendLine($"Object: {query.ObjectName ?? "ad-hoc / plan içinden çıkarılmalı"}");
            if (runtime is not null)
            {
                sb.AppendLine($"ExecutionCount={runtime.ExecutionCount:N0}; AvgCPU={runtime.AverageCpuMs:N1} ms; AvgDuration={runtime.AverageDurationMs:N1} ms; AvgLogicalReads={runtime.AverageLogicalReads:N0}; TotalCPU={runtime.TotalCpuMs:N0} ms; TotalLogicalReads={runtime.TotalLogicalReads:N0}; TotalLogicalWrites={runtime.TotalLogicalWrites:N0}; ImpactScore={runtime.ImpactScore:N1}");
            }
            sb.AppendLine("SQL:");
            sb.AppendLine("```sql");
            sb.AppendLine(Truncate(query.StatementText, 10000));
            sb.AppendLine("```");
            sb.AppendLine();
        }

        if (plan is not null)
        {
            sb.AppendLine("## EXECUTION PLAN XML");
            sb.AppendLine($"PlanId={plan.Id}; PlanHash={plan.PlanHash}; Source={plan.Source}; LastSeen={plan.LastSeenAt:O}");
            sb.AppendLine("```xml");
            sb.AppendLine(Truncate(plan.PlanXml, 18000));
            sb.AppendLine("```");
            sb.AppendLine();
        }

        sb.AppendLine("## MEVCUT İNDEKSLER");
        if (indexes.Count == 0)
        {
            sb.AppendLine("İlgili nesne için güncel indeks snapshot'ı bulunamadı veya nesne bağlamı kesin değil.");
        }
        else
        {
            foreach (var index in indexes.Take(40))
            {
                sb.AppendLine($"- {index.TableName} / {index.IndexName}: KEY=({index.KeyColumns}); INCLUDE=({index.IncludeColumns}); Unique={index.IsUnique}; PK={index.IsPrimaryKey}; Filter={index.FilterDefinition ?? "-"}; Size={index.SizeMb:N1}MB; Reads={index.UserSeeks + index.UserScans + index.UserLookups:N0}; Writes={index.UserUpdates:N0}; Frag={index.AvgFragmentationPercent?.ToString("N1") ?? "?"}%");
            }
        }
        sb.AppendLine();

        sb.AppendLine("## STATISTICS");
        if (statistics.Count == 0)
        {
            sb.AppendLine("İlgili nesne için güncel statistics snapshot'ı bulunamadı veya nesne bağlamı kesin değil.");
        }
        else
        {
            foreach (var stat in statistics.Take(40))
            {
                var modificationPercent = stat.Rows == 0 ? 0m : stat.ModificationCounter * 100m / stat.Rows;
                sb.AppendLine($"- {stat.TableName} / {stat.StatisticsName}: Rows={stat.Rows:N0}; Sample={stat.SamplePercent?.ToString("N1") ?? "?"}%; Modified={stat.ModificationCounter:N0} ({modificationPercent:N1}%); LastUpdated={stat.LastUpdated?.ToString("O") ?? "?"}; Auto={stat.AutoCreated}; User={stat.UserCreated}; NoRecompute={stat.NoRecompute}; Filter={stat.FilterDefinition ?? "-"}");
            }
        }
        sb.AppendLine();

        sb.AppendLine("## SQL SERVER MISSING-INDEX SİNYALLERİ");
        if (missingIndexes.Count == 0)
        {
            sb.AppendLine("İlgili tablo için güncel missing-index DMV sinyali yok.");
        }
        else
        {
            foreach (var missing in missingIndexes.Take(20))
            {
                sb.AppendLine($"- {missing.TableName}: Equality=({missing.EqualityColumns}); Inequality=({missing.InequalityColumns}); Include=({missing.IncludedColumns}); Seeks={missing.UserSeeks:N0}; Scans={missing.UserScans:N0}; AvgCost={missing.AvgTotalUserCost:N2}; UserImpact={missing.AvgUserImpact:N1}%; Improvement={missing.ImprovementMeasure:N0}");
            }
        }
        sb.AppendLine();

        sb.AppendLine("## UYGULAMA TXT WORKLOAD KANITI");
        if (workloadFiles.Count == 0)
        {
            sb.AppendLine("İlgili nesneyle eşleşen aktif TXT workload kaydı bulunamadı veya workload klasörü henüz yapılandırılmadı.");
        }
        else
        {
            foreach (var workload in workloadFiles)
            {
                sb.AppendLine($"### {workload.FileName}");
                sb.AppendLine($"DB={workload.DatabaseName ?? "belirtilmemiş"}; Objects={workload.ReferencedObjects}; Columns={workload.ReferencedColumns}");
                if (!string.IsNullOrWhiteSpace(workload.ParseMessage))
                    sb.AppendLine($"Parser notu: {workload.ParseMessage}");
                sb.AppendLine("```sql");
                sb.AppendLine(workload.QueryText);
                sb.AppendLine("```");
            }
        }

        sb.AppendLine();
        sb.AppendLine("Sonuçta tek bir öncelikli aksiyon öner ve nedenini kanıtlarla açıkla. Emin değilsen değişiklik önermek yerine hangi verinin eksik olduğunu belirt.");
        return sb.ToString();
    }

    private static IReadOnlyCollection<string> ParseTargetTables(string? objectContext)
    {
        if (string.IsNullOrWhiteSpace(objectContext)) return [];
        return objectContext.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(NormalizeTableName)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool TableMatches(string tableName, IReadOnlyCollection<string> targets)
    {
        var normalized = NormalizeTableName(tableName);
        var bare = normalized.Split('.').LastOrDefault() ?? normalized;
        return targets.Any(target =>
        {
            var targetBare = target.Split('.').LastOrDefault() ?? target;
            return normalized.Equals(target, StringComparison.OrdinalIgnoreCase) ||
                   normalized.EndsWith('.' + target, StringComparison.OrdinalIgnoreCase) ||
                   bare.Equals(targetBare, StringComparison.OrdinalIgnoreCase);
        });
    }

    private static bool ReferencesAnyTable(string referencedObjects, IReadOnlyCollection<string> targets) =>
        referencedObjects.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(candidate => TableMatches(candidate, targets));

    private static string NormalizeTableName(string value) =>
        string.Join('.', value.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(x => x.Trim().Trim('[', ']', '"')));

    private static string? NormalizeDatabase(string? value) =>
        string.IsNullOrWhiteSpace(value) ||
        value.Contains("Ad-hoc", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("bağlamı yok", StringComparison.OrdinalIgnoreCase)
            ? null
            : value;

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength] + "\n-- ... kısaltıldı";

    private sealed record WorkloadPromptRow(
        string FileName,
        string? DatabaseName,
        string ReferencedObjects,
        string ReferencedColumns,
        string QueryText,
        string? ParseMessage);
}
