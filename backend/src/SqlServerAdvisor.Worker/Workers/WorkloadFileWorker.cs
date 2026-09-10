using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using SqlServerAdvisor.Infrastructure.Data;
using SqlServerAdvisor.Worker.Services;

namespace SqlServerAdvisor.Worker.Workers;

public sealed class WorkloadFileWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<WorkloadFileWorker> logger) : BackgroundService
{
    private DateTimeOffset _nextRun = DateTimeOffset.MinValue;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Workload TXT collector başladı.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (DateTimeOffset.UtcNow >= _nextRun)
                    await ScanAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Workload TXT collector cycle hatası.");
            }

            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
        }
    }

    private async Task ScanAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AdvisorDbContext>();
        var connectionString = db.Database.GetConnectionString()
            ?? throw new InvalidOperationException("Advisor database connection string bulunamadı.");

        var settings = await LoadSettingsAsync(connectionString, cancellationToken);
        var enabled = GetBool(settings, "Workload.Enabled", false);
        var intervalSeconds = Math.Clamp(GetInt(settings, "Workload.ScanIntervalSeconds", 300), 60, 86400);
        _nextRun = DateTimeOffset.UtcNow.AddSeconds(intervalSeconds);

        if (!enabled)
        {
            await UpdateStatusAsync(connectionString, "Disabled", "Workload klasör taraması kapalı.", null, 0, cancellationToken);
            return;
        }

        var folder = settings.GetValueOrDefault("Workload.FolderPath")?.Trim();
        if (string.IsNullOrWhiteSpace(folder))
        {
            await UpdateStatusAsync(connectionString, "Warning", "Workload klasör yolu tanımlı değil.", DateTimeOffset.UtcNow, 0, cancellationToken);
            return;
        }

        string fullFolder;
        try
        {
            fullFolder = Path.GetFullPath(folder);
        }
        catch (Exception ex)
        {
            await UpdateStatusAsync(connectionString, "Warning", $"Workload klasör yolu geçersiz: {ex.Message}", DateTimeOffset.UtcNow, 0, cancellationToken);
            return;
        }

        if (!Directory.Exists(fullFolder))
        {
            await UpdateStatusAsync(connectionString, "Warning", $"Workload klasörü bulunamadı: {fullFolder}", DateTimeOffset.UtcNow, 0, cancellationToken);
            return;
        }

        var recursive = GetBool(settings, "Workload.Recursive", false);
        var maxFileSizeKb = Math.Clamp(GetInt(settings, "Workload.MaxFileSizeKb", 2048), 16, 10240);
        var maxBytes = maxFileSizeKb * 1024L;

        Guid? serverProfileId = null;
        var serverText = settings.GetValueOrDefault("Workload.DefaultServerProfileId");
        if (Guid.TryParse(serverText, out var configuredServerId))
            serverProfileId = configuredServerId;

        if (!serverProfileId.HasValue)
        {
            var enabledServers = await db.Servers.AsNoTracking()
                .Where(x => x.IsEnabled)
                .Select(x => x.Id)
                .Take(2)
                .ToListAsync(cancellationToken);
            if (enabledServers.Count == 1)
                serverProfileId = enabledServers[0];
        }

        var defaultDatabase = settings.GetValueOrDefault("Workload.DefaultDatabaseName")?.Trim();
        var option = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        List<string> files;
        try
        {
            files = Directory.EnumerateFiles(fullFolder, "*.txt", option)
                .Take(5001)
                .ToList();
        }
        catch (Exception ex)
        {
            await UpdateStatusAsync(connectionString, "Warning", $"Klasör okunamadı: {ex.Message}", DateTimeOffset.UtcNow, 0, cancellationToken);
            return;
        }

        var warnings = new List<string>();
        if (files.Count > 5000)
        {
            files = files.Take(5000).ToList();
            warnings.Add("5000 dosya güvenlik sınırına ulaşıldı; kalan dosyalar bu turda taranmadı.");
        }

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await ExecuteNonQueryAsync(connection,
            "UPDATE QRY.WorkloadFile SET IsActive = 0 WHERE IsActive = 1;",
            cancellationToken);

        var scanned = 0;
        foreach (var file in files)
        {
            if (cancellationToken.IsCancellationRequested) break;

            try
            {
                var info = new FileInfo(file);
                if (info.Length > maxBytes)
                {
                    warnings.Add($"{info.Name}: {info.Length / 1024:N0} KB; {maxFileSizeKb:N0} KB limitini aştığı için atlandı.");
                    continue;
                }

                var text = await File.ReadAllTextAsync(file, cancellationToken);
                var analysis = WorkloadSqlAnalyzer.Analyze(text, defaultDatabase);
                var pathHash = Sha256(info.FullName.ToUpperInvariant());
                var contentHash = WorkloadSqlAnalyzer.ContentHash(text);

                await UpsertFileAsync(
                    connection,
                    serverProfileId,
                    info.FullName,
                    pathHash,
                    info.Name,
                    analysis.DatabaseName,
                    contentHash,
                    analysis.NormalizedHash,
                    text,
                    analysis.ReferencedObjects,
                    analysis.ReferencedColumns,
                    new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero),
                    analysis.ParseMessage,
                    cancellationToken);
                scanned++;
            }
            catch (Exception ex)
            {
                warnings.Add($"{Path.GetFileName(file)}: okunamadı ({ex.Message}).");
            }
        }

        var status = warnings.Count == 0 ? "Success" : "Warning";
        var message = warnings.Count == 0
            ? $"{scanned:N0} TXT sorgu dosyası tarandı. Dosyalar yalnız analiz edildi; hiçbir SQL çalıştırılmadı."
            : $"{scanned:N0} dosya tarandı. {warnings.Count:N0} uyarı: {string.Join(" | ", warnings.Take(8))}";

        await UpdateStatusAsync(connectionString, status, message, DateTimeOffset.UtcNow, scanned, cancellationToken);
        logger.LogInformation("Workload TXT scan: {Status}, {Scanned} files.", status, scanned);
    }

    private static async Task<Dictionary<string, string?>> LoadSettingsAsync(string connectionString, CancellationToken cancellationToken)
    {
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

    private static async Task UpsertFileAsync(
        SqlConnection connection,
        Guid? serverProfileId,
        string filePath,
        string pathHash,
        string fileName,
        string? databaseName,
        string contentHash,
        string normalizedHash,
        string queryText,
        string referencedObjects,
        string referencedColumns,
        DateTimeOffset lastWriteTimeUtc,
        string parseMessage,
        CancellationToken cancellationToken)
    {
        const string sql = """
            MERGE QRY.WorkloadFile AS target
            USING (SELECT @PathHash AS PathHash) AS source
               ON target.PathHash = source.PathHash
            WHEN MATCHED THEN
                UPDATE SET
                    ServerProfileId = @ServerProfileId,
                    FilePath = @FilePath,
                    FileName = @FileName,
                    DatabaseName = @DatabaseName,
                    ContentHash = @ContentHash,
                    NormalizedHash = @NormalizedHash,
                    QueryText = @QueryText,
                    ReferencedObjects = @ReferencedObjects,
                    PredicateColumns = @PredicateColumns,
                    LastWriteTimeUtc = @LastWriteTimeUtc,
                    LastScannedAt = SYSUTCDATETIME(),
                    IsActive = 1,
                    ParseMessage = @ParseMessage
            WHEN NOT MATCHED THEN
                INSERT (ServerProfileId, FilePath, PathHash, FileName, DatabaseName, ContentHash, NormalizedHash,
                        QueryText, ReferencedObjects, PredicateColumns, LastWriteTimeUtc, LastScannedAt, IsActive, ParseMessage)
                VALUES (@ServerProfileId, @FilePath, @PathHash, @FileName, @DatabaseName, @ContentHash, @NormalizedHash,
                        @QueryText, @ReferencedObjects, @PredicateColumns, @LastWriteTimeUtc, SYSUTCDATETIME(), 1, @ParseMessage);
            """;

        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@ServerProfileId", (object?)serverProfileId ?? DBNull.Value);
        command.Parameters.AddWithValue("@FilePath", filePath);
        command.Parameters.AddWithValue("@PathHash", pathHash);
        command.Parameters.AddWithValue("@FileName", fileName);
        command.Parameters.AddWithValue("@DatabaseName", (object?)databaseName ?? DBNull.Value);
        command.Parameters.AddWithValue("@ContentHash", contentHash);
        command.Parameters.AddWithValue("@NormalizedHash", normalizedHash);
        command.Parameters.AddWithValue("@QueryText", queryText);
        command.Parameters.AddWithValue("@ReferencedObjects", referencedObjects);
        command.Parameters.AddWithValue("@PredicateColumns", referencedColumns);
        command.Parameters.AddWithValue("@LastWriteTimeUtc", lastWriteTimeUtc);
        command.Parameters.AddWithValue("@ParseMessage", parseMessage);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task UpdateStatusAsync(
        string connectionString,
        string status,
        string message,
        DateTimeOffset? scanAt,
        int activeFiles,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await UpsertSettingAsync(connection, "Workload.LastStatus", status, cancellationToken);
        await UpsertSettingAsync(connection, "Workload.LastMessage", Truncate(message, 3900), cancellationToken);
        if (scanAt.HasValue)
            await UpsertSettingAsync(connection, "Workload.LastScanAt", scanAt.Value.ToString("O"), cancellationToken);
        await UpsertSettingAsync(connection, "Workload.ActiveFiles", activeFiles.ToString(), cancellationToken);
    }

    private static async Task UpsertSettingAsync(SqlConnection connection, string key, string? value, CancellationToken cancellationToken)
    {
        const string sql = """
            MERGE ADM.ApplicationSetting AS target
            USING (SELECT @Key AS [Key]) AS source
               ON target.[Key] = source.[Key]
            WHEN MATCHED THEN UPDATE SET [Value] = @Value, UpdatedAt = SYSUTCDATETIME()
            WHEN NOT MATCHED THEN INSERT ([Key], [Value], UpdatedAt) VALUES (@Key, @Value, SYSUTCDATETIME());
            """;
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@Key", key);
        command.Parameters.AddWithValue("@Value", (object?)value ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task ExecuteNonQueryAsync(SqlConnection connection, string sql, CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static bool GetBool(IReadOnlyDictionary<string, string?> settings, string key, bool fallback) =>
        settings.TryGetValue(key, out var value) && bool.TryParse(value, out var parsed) ? parsed : fallback;

    private static int GetInt(IReadOnlyDictionary<string, string?> settings, string key, int fallback) =>
        settings.TryGetValue(key, out var value) && int.TryParse(value, out var parsed) ? parsed : fallback;

    private static string Sha256(string value) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value)));

    private static string Truncate(string value, int maxLength) => value.Length <= maxLength ? value : value[..maxLength];
}
