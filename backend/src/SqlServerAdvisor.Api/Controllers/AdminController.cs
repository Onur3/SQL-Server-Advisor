using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using SqlServerAdvisor.Application.DTOs;
using SqlServerAdvisor.Infrastructure.Data;

namespace SqlServerAdvisor.Api.Controllers;

[ApiController]
[Route("api/admin")]
public sealed class AdminController(AdvisorDbContext db) : ControllerBase
{
    [HttpGet("workload-settings")]
    public async Task<ActionResult<WorkloadSettingsDto>> GetWorkloadSettings(CancellationToken cancellationToken)
    {
        var settings = await LoadSettingsAsync(cancellationToken);
        return Ok(ToDto(settings));
    }

    [HttpPut("workload-settings")]
    public async Task<ActionResult<WorkloadSettingsDto>> UpdateWorkloadSettings(
        [FromBody] UpdateWorkloadSettingsRequest request,
        CancellationToken cancellationToken)
    {
        var folderPath = request.FolderPath?.Trim() ?? string.Empty;
        if (folderPath.Length > 1500)
            return BadRequest("Klasör yolu en fazla 1500 karakter olabilir.");

        if (request.Enabled && string.IsNullOrWhiteSpace(folderPath))
            return BadRequest("Workload taraması aktifken klasör yolu zorunludur.");

        if (!string.IsNullOrWhiteSpace(folderPath))
        {
            try
            {
                folderPath = Path.GetFullPath(folderPath);
            }
            catch (Exception ex)
            {
                return BadRequest($"Geçersiz klasör yolu: {ex.Message}");
            }
        }

        if (request.DefaultDatabaseName is { Length: > 128 })
            return BadRequest("Varsayılan veritabanı adı en fazla 128 karakter olabilir.");

        if (request.DefaultServerProfileId.HasValue)
        {
            var exists = await db.Servers.AsNoTracking()
                .AnyAsync(x => x.Id == request.DefaultServerProfileId.Value, cancellationToken);
            if (!exists)
                return BadRequest("Seçilen SQL Server profili bulunamadı.");
        }

        var scanInterval = Math.Clamp(request.ScanIntervalSeconds, 60, 86400);
        var maxFileSizeKb = Math.Clamp(request.MaxFileSizeKb, 16, 10240);

        var values = new Dictionary<string, string?>
        {
            ["Workload.Enabled"] = request.Enabled.ToString().ToLowerInvariant(),
            ["Workload.FolderPath"] = folderPath,
            ["Workload.Recursive"] = request.Recursive.ToString().ToLowerInvariant(),
            ["Workload.DefaultServerProfileId"] = request.DefaultServerProfileId?.ToString() ?? string.Empty,
            ["Workload.DefaultDatabaseName"] = request.DefaultDatabaseName?.Trim() ?? string.Empty,
            ["Workload.ScanIntervalSeconds"] = scanInterval.ToString(),
            ["Workload.MaxFileSizeKb"] = maxFileSizeKb.ToString()
        };

        await SaveSettingsAsync(values, cancellationToken);
        await SaveSettingsAsync(new Dictionary<string, string?>
        {
            ["Workload.ScanRequestToken"] = Guid.NewGuid().ToString("N")
        }, cancellationToken);

        var updated = await LoadSettingsAsync(cancellationToken);
        return Ok(ToDto(updated));
    }

    [HttpPost("workload-scan")]
    public async Task<ActionResult> RequestWorkloadScan(CancellationToken cancellationToken)
    {
        await SaveSettingsAsync(new Dictionary<string, string?>
        {
            ["Workload.ScanRequestToken"] = Guid.NewGuid().ToString("N")
        }, cancellationToken);
        return Accepted(new { message = "Workload tarama isteği Worker'a iletildi." });
    }

    [HttpGet("workload-files")]
    public async Task<ActionResult<IReadOnlyCollection<WorkloadFileDto>>> GetWorkloadFiles(
        [FromQuery] bool activeOnly = true,
        [FromQuery] int take = 250,
        CancellationToken cancellationToken = default)
    {
        take = Math.Clamp(take, 1, 1000);
        var connectionString = db.Database.GetConnectionString()
            ?? throw new InvalidOperationException("Advisor database connection string bulunamadı.");

        const string sql = """
            SELECT TOP (@Take)
                Id, ServerProfileId, FilePath, FileName, DatabaseName,
                ReferencedObjects, PredicateColumns, LastWriteTimeUtc,
                LastScannedAt, IsActive, ParseMessage
            FROM QRY.WorkloadFile
            WHERE (@ActiveOnly = 0 OR IsActive = 1)
            ORDER BY IsActive DESC, LastScannedAt DESC, FileName;
            """;

        var rows = new List<WorkloadFileDto>();
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@Take", take);
        command.Parameters.AddWithValue("@ActiveOnly", activeOnly);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new WorkloadFileDto(
                reader.GetInt64(0),
                reader.IsDBNull(1) ? null : reader.GetGuid(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.IsDBNull(5) ? string.Empty : reader.GetString(5),
                reader.IsDBNull(6) ? string.Empty : reader.GetString(6),
                reader.GetDateTimeOffset(7),
                reader.GetDateTimeOffset(8),
                reader.GetBoolean(9),
                reader.IsDBNull(10) ? null : reader.GetString(10)));
        }

        return Ok(rows);
    }

    private async Task<Dictionary<string, string?>> LoadSettingsAsync(CancellationToken cancellationToken)
    {
        var connectionString = db.Database.GetConnectionString()
            ?? throw new InvalidOperationException("Advisor database connection string bulunamadı.");
        var result = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT [Key], [Value] FROM ADM.ApplicationSetting WHERE [Key] LIKE N'Workload.%';";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            result[reader.GetString(0)] = reader.IsDBNull(1) ? null : reader.GetString(1);
        return result;
    }

    private async Task SaveSettingsAsync(IReadOnlyDictionary<string, string?> values, CancellationToken cancellationToken)
    {
        var connectionString = db.Database.GetConnectionString()
            ?? throw new InvalidOperationException("Advisor database connection string bulunamadı.");
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        const string sql = """
            MERGE ADM.ApplicationSetting AS target
            USING (SELECT @Key AS [Key]) AS source
               ON target.[Key] = source.[Key]
            WHEN MATCHED THEN UPDATE SET [Value] = @Value, UpdatedAt = SYSUTCDATETIME()
            WHEN NOT MATCHED THEN INSERT ([Key], [Value], UpdatedAt) VALUES (@Key, @Value, SYSUTCDATETIME());
            """;

        foreach (var item in values)
        {
            await using var command = new SqlCommand(sql, connection);
            command.Parameters.AddWithValue("@Key", item.Key);
            command.Parameters.AddWithValue("@Value", (object?)item.Value ?? DBNull.Value);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static WorkloadSettingsDto ToDto(IReadOnlyDictionary<string, string?> values)
    {
        var serverId = Guid.TryParse(values.GetValueOrDefault("Workload.DefaultServerProfileId"), out var parsedServerId)
            ? parsedServerId
            : (Guid?)null;
        var lastScan = DateTimeOffset.TryParse(values.GetValueOrDefault("Workload.LastScanAt"), out var parsedScan)
            ? parsedScan
            : (DateTimeOffset?)null;

        return new WorkloadSettingsDto(
            GetBool(values, "Workload.Enabled", false),
            values.GetValueOrDefault("Workload.FolderPath") ?? string.Empty,
            GetBool(values, "Workload.Recursive", false),
            serverId,
            NullIfEmpty(values.GetValueOrDefault("Workload.DefaultDatabaseName")),
            GetInt(values, "Workload.ScanIntervalSeconds", 300),
            GetInt(values, "Workload.MaxFileSizeKb", 2048),
            values.GetValueOrDefault("Workload.LastStatus") ?? "NotScanned",
            NullIfEmpty(values.GetValueOrDefault("Workload.LastMessage")),
            lastScan,
            GetInt(values, "Workload.ActiveFiles", 0));
    }

    private static bool GetBool(IReadOnlyDictionary<string, string?> values, string key, bool fallback) =>
        values.TryGetValue(key, out var value) && bool.TryParse(value, out var parsed) ? parsed : fallback;

    private static int GetInt(IReadOnlyDictionary<string, string?> values, string key, int fallback) =>
        values.TryGetValue(key, out var value) && int.TryParse(value, out var parsed) ? parsed : fallback;

    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
