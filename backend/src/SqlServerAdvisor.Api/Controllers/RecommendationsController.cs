using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SqlServerAdvisor.Application.DTOs;
using SqlServerAdvisor.Application.Presentation;
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
        var query =
            from recommendation in db.Recommendations.AsNoTracking()
            join finding in db.Findings.AsNoTracking()
                on recommendation.FindingId equals finding.Id
            join server in db.Servers.AsNoTracking()
                on finding.ServerProfileId equals server.Id
            select new { recommendation, finding, ServerName = server.Name };

        if (serverId.HasValue)
            query = query.Where(x => x.finding.ServerProfileId == serverId.Value);

        if (!string.IsNullOrWhiteSpace(status) && !status.Equals("All", StringComparison.OrdinalIgnoreCase))
            query = query.Where(x => x.recommendation.Status == status);

        var rows = await query
            .OrderByDescending(x => x.recommendation.Status == "New")
            .ThenByDescending(x => x.recommendation.PriorityScore)
            .ThenByDescending(x => x.recommendation.CreatedAt)
            .Take(500)
            .ToListAsync(cancellationToken);

        var items = rows.Select(x =>
        {
            var narrative = AdvisorNarrativeCatalog.For(x.finding.RuleId);
            return new RecommendationListItemDto(
                x.recommendation.Id,
                x.recommendation.FindingId,
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
}
