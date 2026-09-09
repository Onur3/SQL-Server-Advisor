using Microsoft.AspNetCore.DataProtection;
using SqlServerAdvisor.Application.Contracts;

namespace SqlServerAdvisor.Infrastructure.Security;

public sealed class DataProtectionCredentialProtector(IDataProtectionProvider provider) : ICredentialProtector
{
    private readonly IDataProtector _protector = provider.CreateProtector("SqlServerAdvisor.MonitoredServerCredentials.v1");

    public string Protect(string plaintext) => _protector.Protect(plaintext);
    public string Unprotect(string protectedText) => _protector.Unprotect(protectedText);
}
