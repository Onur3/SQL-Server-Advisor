using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SqlServerAdvisor.Application.Contracts;
using SqlServerAdvisor.Application.DTOs;
using SqlServerAdvisor.Domain.Entities;
using SqlServerAdvisor.Infrastructure.Data;

namespace SqlServerAdvisor.Api.Controllers;

[ApiController]
[Route("api/servers")]
public sealed class ServersController(
    AdvisorDbContext db,
    ICredentialProtector credentialProtector,
    IServerCapabilityScanner capabilityScanner) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyCollection<ServerListItemDto>>> GetAll(CancellationToken cancellationToken)
    {
        var items = await db.Servers.AsNoTracking()
            .OrderBy(x => x.Name)
            .Select(x => new ServerListItemDto(
                x.Id, x.Name, x.Host, x.Port, x.DefaultDatabase, x.AuthenticationType,
                x.Username, x.Encrypt, x.TrustServerCertificate, x.IsEnabled,
                x.LastConnectedAt, x.LastError))
            .ToListAsync(cancellationToken);

        return Ok(items);
    }

    [HttpPost("test")]
    public async Task<ActionResult<ConnectionTestResult>> Test(CreateServerRequest request, CancellationToken cancellationToken)
    {
        var profile = BuildProfile(request);
        var result = await capabilityScanner.TestAsync(profile, cancellationToken);
        return result.Success ? Ok(result) : BadRequest(result);
    }

    [HttpPost]
    public async Task<ActionResult<ServerListItemDto>> Create(CreateServerRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.Host))
            return BadRequest("Sunucu adı ve host zorunludur.");

        if (await db.Servers.AnyAsync(x => x.Name == request.Name, cancellationToken))
            return Conflict($"'{request.Name}' isimli sunucu profili zaten var.");

        var profile = BuildProfile(request);
        var test = await capabilityScanner.TestAsync(profile, cancellationToken);
        if (!test.Success)
            return BadRequest(test);

        profile.LastConnectedAt = DateTimeOffset.UtcNow;
        profile.LastError = null;
        db.Servers.Add(profile);
        await db.SaveChangesAsync(cancellationToken);

        return CreatedAtAction(nameof(GetAll), new { id = profile.Id }, new ServerListItemDto(
            profile.Id, profile.Name, profile.Host, profile.Port, profile.DefaultDatabase,
            profile.AuthenticationType, profile.Username, profile.Encrypt,
            profile.TrustServerCertificate, profile.IsEnabled, profile.LastConnectedAt, profile.LastError));
    }

    [HttpPatch("{id:guid}/enabled")]
    public async Task<IActionResult> SetEnabled(Guid id, [FromBody] bool enabled, CancellationToken cancellationToken)
    {
        var profile = await db.Servers.FindAsync([id], cancellationToken);
        if (profile is null) return NotFound();
        profile.IsEnabled = enabled;
        await db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    private ServerProfile BuildProfile(CreateServerRequest request)
    {
        return new ServerProfile
        {
            Name = request.Name.Trim(),
            Host = request.Host.Trim(),
            Port = request.Port <= 0 ? 1433 : request.Port,
            DefaultDatabase = string.IsNullOrWhiteSpace(request.DefaultDatabase) ? "master" : request.DefaultDatabase.Trim(),
            AuthenticationType = request.AuthenticationType,
            Username = string.IsNullOrWhiteSpace(request.Username) ? null : request.Username.Trim(),
            ProtectedPassword = string.IsNullOrWhiteSpace(request.Password) ? null : credentialProtector.Protect(request.Password),
            Encrypt = request.Encrypt,
            TrustServerCertificate = request.TrustServerCertificate,
            IsEnabled = true
        };
    }
}
