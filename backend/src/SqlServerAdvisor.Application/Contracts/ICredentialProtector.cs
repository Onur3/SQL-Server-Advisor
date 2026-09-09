namespace SqlServerAdvisor.Application.Contracts;

public interface ICredentialProtector
{
    string Protect(string plaintext);
    string Unprotect(string protectedText);
}
