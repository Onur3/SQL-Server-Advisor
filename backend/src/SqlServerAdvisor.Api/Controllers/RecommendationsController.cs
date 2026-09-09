using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SqlServerAdvisor.Application.DTOs;
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
            select new { recommendation, finding, server };

        if (serverId.HasValue)
            query = query.Where(x => x.finding.ServerProfileId == serverId.Value);

        if (!string.IsNullOrWhiteSpace(status) && !status.Equals("All", StringComparison.OrdinalIgnoreCase))
            query = query.Where(x => x.recommendation.Status == status);

        var items = await query
            .OrderByDescending(x => x.recommendation.Status == "New")
            .ThenByDescending(x => x.recommendation.PriorityScore)
            .ThenByDescending(x => x.recommendation.CreatedAt)
            .Select(x => new RecommendationListItemDto(
                x.recommendation.Id,
                x.recommendation.FindingId,
                x.finding.ServerProfileId,
                x.server.Name,
                x.finding.RuleId,
                (int)x.finding.Severity,
                x.finding.Title,
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
                x.recommendation.CreatedAt))
            .Take(500)
            .ToListAsync(cancellationToken);

        return Ok(items);
    }
}
