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

    private static string? NormalizeDatabase(string? value) =>
        string.IsNullOrWhiteSpace(value) ||
        value.Contains("Ad-hoc", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("bağlamı yok", StringComparison.OrdinalIgnoreCase)
            ? null
            : value;

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength] + "\n-- ... kısaltıldı";
}
