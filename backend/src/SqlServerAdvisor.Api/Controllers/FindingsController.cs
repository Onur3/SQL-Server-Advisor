using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SqlServerAdvisor.Application.DTOs;
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
            select new { finding, server };

        if (serverId.HasValue)
            query = query.Where(x => x.finding.ServerProfileId == serverId.Value);

        if (!string.IsNullOrWhiteSpace(status) && !status.Equals("All", StringComparison.OrdinalIgnoreCase))
            query = query.Where(x => x.finding.Status == status);

        var items = await query
            .OrderByDescending(x => x.finding.Status == "Open")
            .ThenByDescending(x => x.finding.Severity)
            .ThenByDescending(x => x.finding.LastDetectedAt)
            .Select(x => new FindingListItemDto(
                x.finding.Id,
                x.finding.ServerProfileId,
                x.server.Name,
                x.finding.RuleId,
                x.finding.Category,
                (int)x.finding.Severity,
                x.finding.Title,
                x.finding.TechnicalDescription,
                x.finding.ConfidenceScore,
                x.finding.ImpactScore,
                x.finding.FindingScore,
                x.finding.FirstDetectedAt,
                x.finding.LastDetectedAt,
                x.finding.ResolvedAt,
                x.finding.OccurrenceCount,
                x.finding.Status))
            .Take(500)
            .ToListAsync(cancellationToken);

        return Ok(items);
    }
}
