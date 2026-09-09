using Microsoft.Data.SqlClient;
using SqlServerAdvisor.Application.Contracts;
using SqlServerAdvisor.Domain.Entities;
using SqlServerAdvisor.Domain.Enums;

namespace SqlServerAdvisor.SqlServer.Connection;

public sealed class MonitoredConnectionStringFactory(ICredentialProtector protector) : IMonitoredConnectionStringFactory
{
    public string Create(ServerProfile server)
    {
        var builder = new SqlConnectionStringBuilder
        {
            DataSource = server.Port == 1433 ? server.Host : $"{server.Host},{server.Port}",
            InitialCatalog = string.IsNullOrWhiteSpace(server.DefaultDatabase) ? "master" : server.DefaultDatabase,
            Encrypt = server.Encrypt,
            TrustServerCertificate = server.TrustServerCertificate,
            ApplicationName = "SQLServerAdvisor.Worker",
            ConnectTimeout = 5,
            Pooling = true,
            MaxPoolSize = 20,
            MultipleActiveResultSets = false
        };

        if (server.AuthenticationType == ServerAuthenticationType.Windows)
        {
            builder.IntegratedSecurity = true;
        }
        else
        {
            builder.UserID = server.Username ?? throw new InvalidOperationException("SQL Login için kullanıcı adı zorunludur.");
            builder.Password = string.IsNullOrWhiteSpace(server.ProtectedPassword)
                ? throw new InvalidOperationException("SQL Login için parola bulunamadı.")
                : protector.Unprotect(server.ProtectedPassword);
        }

        return builder.ConnectionString;
    }
}
