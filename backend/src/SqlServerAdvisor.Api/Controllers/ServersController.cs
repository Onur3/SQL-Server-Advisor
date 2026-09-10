using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
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
    IServerCapabilityScanner capabilityScanner,
    IMonitoredConnectionStringFactory connectionStringFactory,
    ITableScopeStore tableScopeStore) : ControllerBase
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

    [HttpGet("table-scopes")]
    public async Task<ActionResult<IReadOnlyCollection<MonitoredTableScopeItemDto>>> GetAllTableScopes(
        CancellationToken cancellationToken)
    {
        return Ok(await tableScopeStore.GetAllAsync(cancellationToken));
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

    [HttpGet("{id:guid}/databases")]
    public async Task<ActionResult<IReadOnlyCollection<DatabaseOptionDto>>> GetDatabases(
        Guid id,
        CancellationToken cancellationToken)
    {
        var profile = await db.Servers.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (profile is null) return NotFound();

        try
        {
            await using var connection = new SqlConnection(connectionStringFactory.Create(profile));
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT name
                FROM sys.databases
                WHERE database_id > 4
                  AND state = 0
                  AND source_database_id IS NULL
                  AND name <> N'SQLAdvisor'
                ORDER BY name;
                """;

            var result = new List<DatabaseOptionDto>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                result.Add(new DatabaseOptionDto(reader.GetString(0)));

            return Ok(result);
        }
        catch (SqlException ex)
        {
            return BadRequest(new { error = $"Veritabanı listesi alınamadı: {ex.Message}" });
        }
    }

    [HttpGet("{id:guid}/tables")]
    public async Task<ActionResult<IReadOnlyCollection<TableOptionDto>>> GetTables(
        Guid id,
        [FromQuery] string databaseName,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(databaseName) || databaseName.Length > 128)
            return BadRequest(new { error = "Geçerli bir veritabanı adı seçin." });

        var profile = await db.Servers.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (profile is null) return NotFound();

        try
        {
            var result = await LoadTablesAsync(profile, databaseName.Trim(), cancellationToken);
            return Ok(result);
        }
        catch (SqlException ex)
        {
            return BadRequest(new { error = $"Tablo listesi alınamadı: {ex.Message}" });
        }
    }

    [HttpGet("{id:guid}/table-scope")]
    public async Task<ActionResult<TableScopeDto>> GetTableScope(Guid id, CancellationToken cancellationToken)
    {
        if (!await db.Servers.AsNoTracking().AnyAsync(x => x.Id == id, cancellationToken))
            return NotFound();

        var rows = await tableScopeStore.GetForServerAsync(id, cancellationToken);
        var tables = rows
            .Select(x => new MonitoredTableSelectionDto(x.DatabaseName, x.SchemaName, x.TableName))
            .ToArray();

        return Ok(new TableScopeDto(id, tables.Length > 0, tables));
    }

    [HttpPut("{id:guid}/table-scope")]
    public async Task<ActionResult<TableScopeDto>> UpdateTableScope(
        Guid id,
        UpdateTableScopeRequest request,
        CancellationToken cancellationToken)
    {
        var profile = await db.Servers.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (profile is null) return NotFound();

        var requested = (request.Tables ?? [])
            .Select(x => new MonitoredTableSelectionDto(
                x.DatabaseName.Trim(),
                x.SchemaName.Trim(),
                x.TableName.Trim()))
            .Where(x => x.DatabaseName.Length > 0 && x.SchemaName.Length > 0 && x.TableName.Length > 0)
            .DistinctBy(x => $"{x.DatabaseName}\u001f{x.SchemaName}\u001f{x.TableName}", StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (requested.Length > 2000)
            return BadRequest(new { error = "Bir sunucu için en fazla 2000 tablo seçilebilir." });

        if (requested.Any(x => x.DatabaseName.Length > 128 || x.SchemaName.Length > 128 || x.TableName.Length > 128))
            return BadRequest(new { error = "Veritabanı, şema ve tablo adları en fazla 128 karakter olabilir." });

        if (requested.Length > 0)
        {
            try
            {
                foreach (var group in requested.GroupBy(x => x.DatabaseName, StringComparer.OrdinalIgnoreCase))
                {
                    var available = await LoadTablesAsync(profile, group.Key, cancellationToken);
                    var availableKeys = available
                        .Select(x => $"{x.SchemaName}\u001f{x.TableName}")
                        .ToHashSet(StringComparer.OrdinalIgnoreCase);

                    var missing = group.FirstOrDefault(x => !availableKeys.Contains($"{x.SchemaName}\u001f{x.TableName}"));
                    if (missing is not null)
                        return BadRequest(new { error = $"Tablo bulunamadı veya metadata yetkisi yok: {missing.DatabaseName}.{missing.SchemaName}.{missing.TableName}" });
                }
            }
            catch (SqlException ex)
            {
                return BadRequest(new { error = $"Tablo kapsamı doğrulanamadı: {ex.Message}" });
            }
        }

        await tableScopeStore.ReplaceForServerAsync(id, requested, cancellationToken);
        return Ok(new TableScopeDto(id, requested.Length > 0, requested));
    }

    private async Task<IReadOnlyCollection<TableOptionDto>> LoadTablesAsync(
        ServerProfile profile,
        string databaseName,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(connectionStringFactory.Create(profile));
        await connection.OpenAsync(cancellationToken);
        connection.ChangeDatabase(databaseName);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT s.name AS SchemaName, t.name AS TableName
            FROM sys.tables t
            JOIN sys.schemas s ON s.schema_id = t.schema_id
            WHERE t.is_ms_shipped = 0
            ORDER BY s.name, t.name;
            """;

        var result = new List<TableOptionDto>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var schemaName = reader.GetString(0);
            var tableName = reader.GetString(1);
            result.Add(new TableOptionDto(
                databaseName,
                schemaName,
                tableName,
                $"{schemaName}.{tableName}"));
        }

        return result;
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
