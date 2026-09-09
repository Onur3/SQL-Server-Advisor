using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SqlServerAdvisor.Application.Contracts;
using SqlServerAdvisor.Infrastructure.Data;
using SqlServerAdvisor.Infrastructure.Security;

namespace SqlServerAdvisor.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddAdvisorInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("AdvisorDatabase")
            ?? throw new InvalidOperationException("ConnectionStrings:AdvisorDatabase tanımlı değil.");

        services.AddDbContext<AdvisorDbContext>(options =>
            options.UseSqlServer(connectionString, sql => sql.EnableRetryOnFailure()));

        var keyPath = configuration["Security:DataProtectionKeyPath"]
            ?? Path.Combine(AppContext.BaseDirectory, "DataProtectionKeys");
        Directory.CreateDirectory(keyPath);

        services.AddDataProtection()
            .PersistKeysToFileSystem(new DirectoryInfo(keyPath))
            .SetApplicationName("SqlServerAdvisor");

        services.AddSingleton<ICredentialProtector, DataProtectionCredentialProtector>();
        return services;
    }
}
