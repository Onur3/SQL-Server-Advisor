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
        var query =
            from finding in db.Findings.AsNoTracking()
            join server in db.Servers.AsNoTracking()
                on finding.ServerProfileId equals server.Id
            select new { finding, ServerName = server.Name };

        if (serverId.HasValue)
            query = query.Where(x => x.finding.ServerProfileId == serverId.Value);

        if (!string.IsNullOrWhiteSpace(status) && !status.Equals("All", StringComparison.OrdinalIgnoreCase))
            query = query.Where(x => x.finding.Status == status);

        var rows = await query
            .OrderByDescending(x => x.finding.Status == "Open")
            .ThenByDescending(x => x.finding.Severity)
            .ThenByDescending(x => x.finding.FindingScore)
            .ThenByDescending(x => x.finding.LastDetectedAt)
            .Take(500)
            .ToListAsync(cancellationToken);

        var items = rows.Select(x =>
        {
            var narrative = AdvisorNarrativeCatalog.For(x.finding.RuleId);
            return new FindingListItemDto(
                x.finding.Id,
                x.finding.ServerProfileId,
                x.ServerName,
                x.finding.QueryId,
                x.finding.DatabaseName,
                x.finding.ObjectName,
                AdvisorNarrativeCatalog.BuildScope(x.ServerName, x.finding.DatabaseName, x.finding.ObjectName),
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
}
