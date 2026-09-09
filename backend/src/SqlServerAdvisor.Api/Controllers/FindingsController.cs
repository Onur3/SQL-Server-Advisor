using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SqlServerAdvisor.Application.DTOs;
using SqlServerAdvisor.Application.Presentation;
using SqlServerAdvisor.Infrastructure.Data;

namespace SqlServerAdvisor.Api.Controllers;

[ApiController]
[Route("api/findings")]
public sealed class FindingsController(AdvisorDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyCollection<FindingListItemDto>>> Get(
        [FromQuery] Guid? serverId,
        [FromQuery] string? status,
        CancellationToken cancellationToken)
    {
        var source =
            from finding in db.Findings.AsNoTracking()
            join server in db.Servers.AsNoTracking()
                on finding.ServerProfileId equals server.Id
            select new { finding, ServerName = server.Name };

        if (serverId.HasValue)
            source = source.Where(x => x.finding.ServerProfileId == serverId.Value);

        if (!string.IsNullOrWhiteSpace(status) && !status.Equals("All", StringComparison.OrdinalIgnoreCase))
            source = source.Where(x => x.finding.Status == status);

        var rows = await source
            .OrderByDescending(x => x.finding.Status == "Open")
            .ThenByDescending(x => x.finding.Severity)
            .ThenByDescending(x => x.finding.FindingScore)
            .ThenByDescending(x => x.finding.LastDetectedAt)
            .Take(500)
            .ToListAsync(cancellationToken);

        var queryIds = rows
            .Where(x => x.finding.QueryId.HasValue)
            .Select(x => x.finding.QueryId!.Value)
            .Distinct()
            .ToArray();

        var queryDefinitions = queryIds.Length == 0
            ? new Dictionary<long, SqlServerAdvisor.Domain.Entities.QueryDefinition>()
            : await db.Queries.AsNoTracking()
                .Where(x => queryIds.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id, cancellationToken);

        var planQueryIds = queryIds.Length == 0
            ? new HashSet<long>()
            : (await db.QueryPlans.AsNoTracking()
                .Where(x => queryIds.Contains(x.QueryId))
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

            return new FindingListItemDto(
                x.finding.Id,
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
                narrative.WhatWasFound,
                narrative.WhyItMatters,
                narrative.NextCheck,
                x.finding.TechnicalDescription,
                x.finding.ConfidenceScore,
                x.finding.ImpactScore,
                x.finding.FindingScore,
                x.finding.FirstDetectedAt,
                x.finding.LastDetectedAt,
                x.finding.ResolvedAt,
                x.finding.OccurrenceCount,
                x.finding.Status);
        }).ToList();

        return Ok(items);
    }

    private static string? NormalizeDatabase(string? value) =>
        string.IsNullOrWhiteSpace(value) ||
        value.Contains("Ad-hoc", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("bağlamı yok", StringComparison.OrdinalIgnoreCase)
            ? null
            : value;

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength] + "\n-- ... kısaltıldı";
}
