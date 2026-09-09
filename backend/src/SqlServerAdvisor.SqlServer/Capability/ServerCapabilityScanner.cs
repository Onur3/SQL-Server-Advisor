using Dapper;
using Microsoft.Data.SqlClient;
using SqlServerAdvisor.Application.Contracts;
using SqlServerAdvisor.Application.DTOs;
using SqlServerAdvisor.Domain.Entities;

namespace SqlServerAdvisor.SqlServer.Capability;

public sealed class ServerCapabilityScanner(IMonitoredConnectionStringFactory connectionStringFactory) : IServerCapabilityScanner
{
    private const string Sql = """
        SELECT
            CAST(SERVERPROPERTY('ServerName') AS nvarchar(255)) AS ServerName,
            CAST(SERVERPROPERTY('ProductVersion') AS nvarchar(100)) AS ProductVersion,
            CAST(SERVERPROPERTY('Edition') AS nvarchar(255)) AS Edition;
        """;

    public async Task<ConnectionTestResult> TestAsync(ServerProfile server, CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = new SqlConnection(connectionStringFactory.Create(server));
            var info = await connection.QuerySingleAsync<ServerInfo>(new CommandDefinition(Sql, commandTimeout: 5, cancellationToken: cancellationToken));
            return new ConnectionTestResult(true, info.ServerName, info.ProductVersion, info.Edition, null);
        }
        catch (Exception ex)
        {
            return new ConnectionTestResult(false, null, null, null, ex.Message);
        }
    }

    private sealed record ServerInfo(string? ServerName, string? ProductVersion, string? Edition);
}
