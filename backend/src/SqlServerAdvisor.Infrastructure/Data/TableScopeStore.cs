using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using SqlServerAdvisor.Application.Contracts;
using SqlServerAdvisor.Application.DTOs;

namespace SqlServerAdvisor.Infrastructure.Data;

public sealed class TableScopeStore(AdvisorDbContext db) : ITableScopeStore
{
    public Task<IReadOnlyList<MonitoredTableScopeItemDto>> GetAllAsync(CancellationToken cancellationToken) =>
        LoadAsync(null, cancellationToken);

    public Task<IReadOnlyList<MonitoredTableScopeItemDto>> GetForServerAsync(
        Guid serverProfileId,
        CancellationToken cancellationToken) => LoadAsync(serverProfileId, cancellationToken);

    public async Task ReplaceForServerAsync(
        Guid serverProfileId,
        IReadOnlyCollection<MonitoredTableSelectionDto> tables,
        CancellationToken cancellationToken)
    {
        var normalized = tables
            .Where(x => !string.IsNullOrWhiteSpace(x.DatabaseName) &&
                        !string.IsNullOrWhiteSpace(x.SchemaName) &&
                        !string.IsNullOrWhiteSpace(x.TableName))
            .Select(x => new MonitoredTableSelectionDto(
                x.DatabaseName.Trim(),
                x.SchemaName.Trim(),
                x.TableName.Trim()))
            .DistinctBy(x => $"{x.DatabaseName}\u001f{x.SchemaName}\u001f{x.TableName}", StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var connectionString = db.Database.GetConnectionString()
            ?? throw new InvalidOperationException("Advisor database connection string bulunamadı.");

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            await using (var delete = new SqlCommand(
                "DELETE FROM ADM.MonitoredTableScope WHERE ServerProfileId = @ServerProfileId;",
                connection,
                transaction))
            {
                delete.Parameters.AddWithValue("@ServerProfileId", serverProfileId);
                await delete.ExecuteNonQueryAsync(cancellationToken);
            }

            const string insertSql = """
                INSERT INTO ADM.MonitoredTableScope
                    (ServerProfileId, DatabaseName, SchemaName, TableName, CreatedAt)
                VALUES
                    (@ServerProfileId, @DatabaseName, @SchemaName, @TableName, SYSUTCDATETIME());
                """;

            foreach (var table in normalized)
            {
                await using var insert = new SqlCommand(insertSql, connection, transaction);
                insert.Parameters.AddWithValue("@ServerProfileId", serverProfileId);
                insert.Parameters.AddWithValue("@DatabaseName", table.DatabaseName);
                insert.Parameters.AddWithValue("@SchemaName", table.SchemaName);
                insert.Parameters.AddWithValue("@TableName", table.TableName);
                await insert.ExecuteNonQueryAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private async Task<IReadOnlyList<MonitoredTableScopeItemDto>> LoadAsync(
        Guid? serverProfileId,
        CancellationToken cancellationToken)
    {
        var connectionString = db.Database.GetConnectionString()
            ?? throw new InvalidOperationException("Advisor database connection string bulunamadı.");
        var result = new List<MonitoredTableScopeItemDto>();

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT ServerProfileId, DatabaseName, SchemaName, TableName
            FROM ADM.MonitoredTableScope
            WHERE (@ServerProfileId IS NULL OR ServerProfileId = @ServerProfileId)
            ORDER BY ServerProfileId, DatabaseName, SchemaName, TableName;
            """;
        command.Parameters.AddWithValue("@ServerProfileId", (object?)serverProfileId ?? DBNull.Value);

        try
        {
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                result.Add(new MonitoredTableScopeItemDto(
                    reader.GetGuid(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetString(3)));
            }
        }
        catch (SqlException ex) when (ex.Number == 208)
        {
            return [];
        }

        return result;
    }
}
