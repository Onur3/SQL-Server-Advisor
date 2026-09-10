using System.Security.Cryptography;
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
[Route("api/indexes")]
public sealed class IndexesController(AdvisorDbContext db) : ControllerBase
{
    [HttpGet("current")]
    [HttpGet("fragmented")]
    public async Task<ActionResult<IReadOnlyCollection<FragmentedIndexDto>>> GetCurrentIndexes(
        [FromQuery] Guid? serverId,
        [FromQuery] int take = 100,
        CancellationToken cancellationToken = default)
    {
        take = Math.Clamp(take, 1, 500);

        var source = db.IndexSnapshots.AsNoTracking().AsQueryable();
        if (serverId.HasValue)
            source = source.Where(x => x.ServerProfileId == serverId.Value);

        var latestCaptureByServer = source
            .GroupBy(x => x.ServerProfileId)
            .Select(group => new
            {
                ServerProfileId = group.Key,
                CapturedAt = group.Max(x => x.CapturedAt)
            });

        var currentQuery =
            from index in source
            join capture in latestCaptureByServer
                on new { index.ServerProfileId, index.CapturedAt }
                equals new { capture.ServerProfileId, capture.CapturedAt }
            select index;

        var latest = await currentQuery
            .OrderByDescending(x => x.PageCount)
            .ThenByDescending(x => x.AvgFragmentationPercent)
            .ThenByDescending(x => x.UserSeeks + x.UserScans + x.UserLookups)
            .Take(take)
            .ToListAsync(cancellationToken);

        var serverIds = latest.Select(x => x.ServerProfileId).Distinct().ToArray();
        var servers = await db.Servers.AsNoTracking()
            .Where(x => serverIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken);

        return Ok(latest.Select(x =>
        {
            var interpretation = IndexInterpretation.BuildFragmentation(
                x.AvgFragmentationPercent ?? 0m,
                x.PageCount ?? 0,
                x.SizeMb,
                x.UserSeeks,
                x.UserScans,
                x.UserLookups,
                x.UserUpdates,
                x.UsageSinceDays,
                x.HasFilter);

            return new FragmentedIndexDto(
                x.Id,
                x.ServerProfileId,
                servers.GetValueOrDefault(x.ServerProfileId, x.ServerProfileId.ToString()),
                x.DatabaseName,
                x.TableName,
                x.IndexName,
                x.TypeDesc,
                x.KeyColumns,
                x.IncludeColumns,
                x.SizeMb,
                x.UserSeeks,
                x.UserScans,
                x.UserLookups,
                x.UserUpdates,
                x.HasFilter,
                x.FilterDefinition,
                x.UsageSinceDays,
                x.AvgFragmentationPercent,
                x.PageCount,
                interpretation.Level,
                interpretation.Headline,
                interpretation.Summary,
                interpretation.SuggestedInspection,
                x.CapturedAt);
        }).ToList());
    }

    [HttpGet("missing")]
    public async Task<ActionResult<IReadOnlyCollection<MissingIndexDto>>> GetMissing(
        [FromQuery] Guid? serverId,
        [FromQuery] int take = 100,
        CancellationToken cancellationToken = default)
    {
        take = Math.Clamp(take, 1, 200);

        var query = db.MissingIndexSnapshots.AsNoTracking().AsQueryable();
        if (serverId.HasValue)
            query = query.Where(x => x.ServerProfileId == serverId.Value);

        var recent = await query
            .OrderByDescending(x => x.CapturedAt)
            .ThenByDescending(x => x.ImprovementMeasure)
            .Take(take * 10)
            .ToListAsync(cancellationToken);

        var latest = recent
            .GroupBy(x => new { x.ServerProfileId, x.DatabaseName, x.TableName, x.EqualityColumns, x.InequalityColumns, x.IncludedColumns })
            .Select(x => x.OrderByDescending(y => y.CapturedAt).First())
            .OrderByDescending(x => x.ImprovementMeasure)
            .Take(take)
            .ToList();

        var serverIds = latest.Select(x => x.ServerProfileId).Distinct().ToArray();
        var servers = await db.Servers.AsNoTracking()
            .Where(x => serverIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken);

        var currentIndexes = new List<IndexSnapshot>();
        foreach (var currentServerId in serverIds)
        {
            var latestCapturedAt = await db.IndexSnapshots.AsNoTracking()
                .Where(x => x.ServerProfileId == currentServerId)
                .MaxAsync(x => (DateTimeOffset?)x.CapturedAt, cancellationToken);

            if (!latestCapturedAt.HasValue) continue;

            currentIndexes.AddRange(await db.IndexSnapshots.AsNoTracking()
                .Where(x => x.ServerProfileId == currentServerId && x.CapturedAt == latestCapturedAt.Value)
                .ToListAsync(cancellationToken));
        }

        var workloadFiles = await LoadWorkloadFilesAsync(cancellationToken);

        return Ok(latest.Select(x =>
        {
            var tableIndexes = currentIndexes
                .Where(i => i.ServerProfileId == x.ServerProfileId &&
                            i.DatabaseName.Equals(x.DatabaseName, StringComparison.OrdinalIgnoreCase) &&
                            i.TableName.Equals(x.TableName, StringComparison.OrdinalIgnoreCase) &&
                            !i.IsDisabled)
                .ToList();

            var covering = FindCoveringIndex(x, tableIndexes);
            var near = covering is null ? FindNearestIndex(x, tableIndexes) : null;
            var covered = covering is not null;
            var interpretation = IndexInterpretation.BuildMissing(
                x.UserSeeks,
                x.UserScans,
                x.AvgUserImpact,
                x.ImprovementMeasure,
                covered);

            var decision = BuildDecision(x, covering, near);
            var existingIndexes = tableIndexes
                .OrderByDescending(i => IndexSimilarityScore(x, i))
                .ThenByDescending(i => i.UserSeeks + i.UserScans + i.UserLookups)
                .Take(12)
                .Select(i => new ExistingIndexSummaryDto(
                    i.IndexName,
                    i.KeyColumns,
                    i.IncludeColumns,
                    i.IsUnique,
                    i.IsPrimaryKey,
                    i.HasFilter,
                    i.FilterDefinition,
                    i.UserSeeks + i.UserScans + i.UserLookups,
                    i.UserUpdates,
                    i.SizeMb))
                .ToList();

            var workloadMatches = workloadFiles
                .Where(w => (!w.ServerProfileId.HasValue || w.ServerProfileId == x.ServerProfileId) &&
                            (string.IsNullOrWhiteSpace(w.DatabaseName) || w.DatabaseName.Equals(x.DatabaseName, StringComparison.OrdinalIgnoreCase)) &&
                            ReferencesTable(w.ReferencedObjects, x.TableName))
                .Select(w => w.FileName)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .Take(12)
                .ToList();

            return new MissingIndexDto(
                x.Id,
                x.ServerProfileId,
                servers.GetValueOrDefault(x.ServerProfileId, x.ServerProfileId.ToString()),
                x.DatabaseName,
                x.TableName,
                x.EqualityColumns,
                x.InequalityColumns,
                x.IncludedColumns,
                x.UserSeeks,
                x.UserScans,
                x.AvgTotalUserCost,
                x.AvgUserImpact,
                x.ImprovementMeasure,
                covered,
                decision.Type,
                decision.Title,
                decision.Reason,
                decision.ProposedSql,
                existingIndexes,
                workloadMatches,
                interpretation.Level,
                interpretation.Headline,
                interpretation.Summary,
                interpretation.SuggestedInspection,
                x.CapturedAt);
        })
        .OrderBy(x => x.CoveredByExistingIndex)
        .ThenByDescending(x => x.ImprovementMeasure)
        .ToList());
    }

    private async Task<List<WorkloadFileRow>> LoadWorkloadFilesAsync(CancellationToken cancellationToken)
    {
        var rows = new List<WorkloadFileRow>();
        var connectionString = db.Database.GetConnectionString();
        if (string.IsNullOrWhiteSpace(connectionString)) return rows;

        try
        {
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT ServerProfileId, DatabaseName, FileName, ReferencedObjects FROM QRY.WorkloadFile WHERE IsActive = 1;";
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                rows.Add(new WorkloadFileRow(
                    reader.IsDBNull(0) ? null : reader.GetGuid(0),
                    reader.IsDBNull(1) ? null : reader.GetString(1),
                    reader.GetString(2),
                    reader.IsDBNull(3) ? string.Empty : reader.GetString(3)));
            }
        }
        catch (SqlException ex) when (ex.Number is 208 or 2812)
        {
            // Upgrade migration may not have been deployed yet. Index analysis remains usable.
        }

        return rows;
    }

    private static IndexSnapshot? FindCoveringIndex(MissingIndexSnapshot missing, IReadOnlyCollection<IndexSnapshot> indexes)
    {
        var requiredKeys = ParseColumns(missing.EqualityColumns)
            .Concat(ParseColumns(missing.InequalityColumns))
            .ToArray();
        var included = ParseColumns(missing.IncludedColumns);
        if (requiredKeys.Length == 0) return null;

        foreach (var index in indexes.Where(x => !x.IsDisabled && !x.HasFilter))
        {
            var existingKeys = ParseColumns(index.KeyColumns);
            if (existingKeys.Length < requiredKeys.Length) continue;

            var prefix = existingKeys.Take(requiredKeys.Length).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (!requiredKeys.All(prefix.Contains)) continue;

            var available = existingKeys.Concat(ParseColumns(index.IncludeColumns))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (included.All(available.Contains)) return index;
        }

        return null;
    }

    private static IndexSnapshot? FindNearestIndex(MissingIndexSnapshot missing, IReadOnlyCollection<IndexSnapshot> indexes) =>
        indexes
            .Where(x => !x.IsDisabled)
            .Select(x => new { Index = x, Score = IndexSimilarityScore(missing, x) })
            .Where(x => x.Score >= 35)
            .OrderByDescending(x => x.Score)
            .ThenByDescending(x => x.Index.UserSeeks + x.Index.UserScans + x.Index.UserLookups)
            .Select(x => x.Index)
            .FirstOrDefault();

    private static int IndexSimilarityScore(MissingIndexSnapshot missing, IndexSnapshot index)
    {
        var requestedKeys = ParseColumns(missing.EqualityColumns)
            .Concat(ParseColumns(missing.InequalityColumns))
            .ToArray();
        var requestedIncludes = ParseColumns(missing.IncludedColumns);
        if (requestedKeys.Length == 0) return 0;

        var existingKeys = ParseColumns(index.KeyColumns);
        var existingAll = existingKeys.Concat(ParseColumns(index.IncludeColumns))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var keyMatches = requestedKeys.Count(k => existingKeys.Contains(k, StringComparer.OrdinalIgnoreCase));
        var includeMatches = requestedIncludes.Count(existingAll.Contains);
        var firstKeyBonus = existingKeys.Length > 0 &&
                            requestedKeys.Contains(existingKeys[0], StringComparer.OrdinalIgnoreCase)
            ? 20
            : 0;

        var keyScore = (int)Math.Round(keyMatches * 60.0 / Math.Max(1, requestedKeys.Length));
        var includeScore = requestedIncludes.Length == 0
            ? 10
            : (int)Math.Round(includeMatches * 20.0 / requestedIncludes.Length);
        return Math.Min(100, keyScore + includeScore + firstKeyBonus);
    }

    private static IndexDecision BuildDecision(MissingIndexSnapshot missing, IndexSnapshot? covering, IndexSnapshot? near)
    {
        if (covering is not null)
        {
            return new IndexDecision(
                "UseExisting",
                "Yeni indeks oluşturmayın — mevcut indeks kapsıyor",
                $"{covering.IndexName} istenen key/include kolonlarını zaten kapsıyor. Yeni indeks açmak write ve bakım maliyetini artırabilir. Execution plan'da optimizer'ın bu indeksi neden seçmediğini, statistics ve cardinality tahminlerini inceleyin.",
                null);
        }

        var proposedSql = BuildCreateIndexSql(missing);
        if (near is not null)
        {
            return new IndexDecision(
                "ConsolidateOrCreate",
                "Mevcut indeksle konsolide edin veya kontrollü yeni indeks oluşturun",
                $"{near.IndexName} bu talebe kısmen benziyor. Önce key/include farkını ve {near.UserUpdates:N0} write maliyetini karşılaştırın. Mevcut indeks genişletilemiyorsa aşağıdaki CREATE INDEX taslağını test ortamında doğrulayın.",
                proposedSql);
        }

        return new IndexDecision(
            "CreateIndexCandidate",
            "Yeni indeks oluşturma adayı",
            "Mevcut indeks kataloğunda bu key/include talebini kapsayan veya güçlü biçimde örtüşen bir indeks bulunamadı. DMV etkisi gerçek workload ile doğrulanırsa aşağıdaki taslak test ortamında değerlendirilebilir.",
            proposedSql);
    }

    private static string BuildCreateIndexSql(MissingIndexSnapshot missing)
    {
        var keys = ParseColumns(missing.EqualityColumns)
            .Concat(ParseColumns(missing.InequalityColumns))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var includes = ParseColumns(missing.IncludedColumns)
            .Where(x => !keys.Contains(x, StringComparer.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (keys.Length == 0) return string.Empty;

        var signature = $"{missing.DatabaseName}|{missing.TableName}|{string.Join(',', keys)}|{string.Join(',', includes)}";
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(signature)))[..10];
        var indexName = $"IX_ADVISOR_{hash}";
        var keySql = string.Join(", ", keys.Select(QuoteIdentifier));
        var includeSql = includes.Length == 0 ? string.Empty : $"\nINCLUDE ({string.Join(", ", includes.Select(QuoteIdentifier))})";

        return $"-- TASLAK: SQL Server Advisor bu komutu otomatik çalıştırmaz.\nUSE {QuoteIdentifier(missing.DatabaseName)};\nCREATE NONCLUSTERED INDEX {QuoteIdentifier(indexName)}\nON {QuoteMultipartIdentifier(missing.TableName)} ({keySql}){includeSql};";
    }

    private static bool ReferencesTable(string referencedObjects, string tableName)
    {
        if (string.IsNullOrWhiteSpace(referencedObjects)) return false;
        var target = NormalizeObjectForCompare(tableName);
        var targetBare = target.Split('.').LastOrDefault() ?? target;

        return referencedObjects.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(NormalizeObjectForCompare)
            .Any(candidate => candidate.Equals(target, StringComparison.OrdinalIgnoreCase) ||
                              candidate.EndsWith('.' + target, StringComparison.OrdinalIgnoreCase) ||
                              (candidate.Split('.').LastOrDefault() ?? candidate).Equals(targetBare, StringComparison.OrdinalIgnoreCase));
    }

    private static string NormalizeObjectForCompare(string value) =>
        string.Join('.', value.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(x => x.Trim().Trim('[', ']', '"')));

    private static string QuoteMultipartIdentifier(string value) =>
        string.Join('.', value.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(x => QuoteIdentifier(x.Trim().Trim('[', ']', '"'))));

    private static string QuoteIdentifier(string value) => $"[{value.Replace("]", "]]", StringComparison.Ordinal)}]";

    private static string[] ParseColumns(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(x => x.Trim().Trim('[', ']'))
                .Where(x => x.Length > 0)
                .ToArray();

    private sealed record WorkloadFileRow(Guid? ServerProfileId, string? DatabaseName, string FileName, string ReferencedObjects);
    private sealed record IndexDecision(string Type, string Title, string Reason, string? ProposedSql);
}
