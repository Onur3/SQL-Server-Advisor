namespace SqlServerAdvisor.Api.Security;

public sealed class AppLoginOptions
{
    public const string SectionName = "AppLogin";

    public bool Enabled { get; set; }
    public string Username { get; set; } = "admin";
    public string PasswordHash { get; set; } = string.Empty;
    public string PasswordSalt { get; set; } = string.Empty;
    public int Iterations { get; set; } = 210_000;
    public int SessionHours { get; set; } = 12;
}
