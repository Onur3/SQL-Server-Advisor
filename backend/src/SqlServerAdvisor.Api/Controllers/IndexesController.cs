using Microsoft.AspNetCore.Mvc;
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
    [HttpGet("fragmented")]
    public async Task<ActionResult<IReadOnlyCollection<FragmentedIndexDto>>> GetFragmented(
        [FromQuery] Guid? serverId,
        [FromQuery] int take = 100,
        CancellationToken cancellationToken = default)
    {
        take = Math.Clamp(take, 1, 200);

        var query = db.IndexSnapshots.AsNoTracking()
            .Where(x => x.AvgFragmentationPercent != null && x.PageCount != null && x.PageCount >= 1000);

        if (serverId.HasValue)
            query = query.Where(x => x.ServerProfileId == serverId.Value);

        var recent = await query
            .OrderByDescending(x => x.CapturedAt)
            .ThenByDescending(x => x.AvgFragmentationPercent)
            .Take(take * 10)
            .ToListAsync(cancellationToken);

        var latest = recent
            .GroupBy(x => new { x.ServerProfileId, x.DatabaseName, x.ObjectId, x.IndexId })
            .Select(x => x.OrderByDescending(y => y.CapturedAt).First())
            .OrderByDescending(x => x.AvgFragmentationPercent)
            .ThenByDescending(x => x.PageCount)
            .Take(take)
            .ToList();

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

        var coverageIndexes = new List<IndexSnapshot>();
        foreach (var currentServerId in serverIds)
        {
            var latestCapturedAt = await db.IndexSnapshots.AsNoTracking()
                .Where(x => x.ServerProfileId == currentServerId)
                .MaxAsync(x => (DateTimeOffset?)x.CapturedAt, cancellationToken);

            if (!latestCapturedAt.HasValue) continue;

            coverageIndexes.AddRange(await db.IndexSnapshots.AsNoTracking()
                .Where(x => x.ServerProfileId == currentServerId && x.CapturedAt == latestCapturedAt.Value)
                .ToListAsync(cancellationToken));
        }

        return Ok(latest.Select(x =>
        {
            var covered = IsCoveredByExistingIndex(x, coverageIndexes);
            var interpretation = IndexInterpretation.BuildMissing(
                x.UserSeeks,
                x.UserScans,
                x.AvgUserImpact,
                x.ImprovementMeasure,
                covered);

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

    private static bool IsCoveredByExistingIndex(MissingIndexSnapshot missing, IReadOnlyCollection<IndexSnapshot> indexes)
    {
        var requiredKeys = ParseColumns(missing.EqualityColumns)
            .Concat(ParseColumns(missing.InequalityColumns))
            .ToArray();
        var included = ParseColumns(missing.IncludedColumns);
        if (requiredKeys.Length == 0) return false;

        foreach (var index in indexes.Where(x =>
                     x.ServerProfileId == missing.ServerProfileId &&
                     x.DatabaseName.Equals(missing.DatabaseName, StringComparison.OrdinalIgnoreCase) &&
                     x.TableName.Equals(missing.TableName, StringComparison.OrdinalIgnoreCase) &&
                     !x.IsDisabled && !x.HasFilter))
        {
            var existingKeys = ParseColumns(index.KeyColumns);
            if (existingKeys.Length < requiredKeys.Length) continue;

            var prefix = existingKeys.Take(requiredKeys.Length).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (!requiredKeys.All(prefix.Contains)) continue;

            var available = existingKeys.Concat(ParseColumns(index.IncludeColumns))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (included.All(available.Contains)) return true;
        }

        return false;
    }

    private static string[] ParseColumns(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(x => x.Trim().Trim('[', ']'))
                .Where(x => x.Length > 0)
                .ToArray();
}
