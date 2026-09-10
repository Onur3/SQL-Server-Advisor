using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using SqlServerAdvisor.Api.Security;

namespace SqlServerAdvisor.Api.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController(IOptions<AppLoginOptions> options) : ControllerBase
{
    private readonly AppLoginOptions _options = options.Value;

    [AllowAnonymous]
    [HttpGet("status")]
    public ActionResult GetStatus()
    {
        var authenticated = !_options.Enabled || User.Identity?.IsAuthenticated == true;
        return Ok(new
        {
            enabled = _options.Enabled,
            authenticated,
            username = User.Identity?.IsAuthenticated == true ? User.Identity.Name : null
        });
    }

    [AllowAnonymous]
    [HttpPost("login")]
    public async Task<ActionResult> Login([FromBody] LoginRequest request)
    {
        if (!_options.Enabled)
            return Ok(new { enabled = false, authenticated = true, username = (string?)null });

        if (!IsConfigurationValid())
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { message = "Uygulama giriş yapılandırması geçersiz. Kurulumu yeniden çalıştırın." });

        if (!string.Equals(request.Username?.Trim(), _options.Username, StringComparison.Ordinal) ||
            !VerifyPassword(request.Password ?? string.Empty))
        {
            await Task.Delay(Random.Shared.Next(120, 260), HttpContext.RequestAborted);
            return Unauthorized(new { message = "Kullanıcı adı veya şifre hatalı." });
        }

        var claims = new[]
        {
            new Claim(ClaimTypes.Name, _options.Username),
            new Claim(ClaimTypes.NameIdentifier, _options.Username)
        };
        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var principal = new ClaimsPrincipal(identity);

        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            principal,
            new AuthenticationProperties
            {
                IsPersistent = false,
                AllowRefresh = true,
                ExpiresUtc = DateTimeOffset.UtcNow.AddHours(Math.Clamp(_options.SessionHours, 1, 168))
            });

        return Ok(new { enabled = true, authenticated = true, username = _options.Username });
    }

    [AllowAnonymous]
    [HttpPost("logout")]
    public async Task<ActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return NoContent();
    }

    private bool IsConfigurationValid() =>
        !string.IsNullOrWhiteSpace(_options.Username) &&
        !string.IsNullOrWhiteSpace(_options.PasswordHash) &&
        !string.IsNullOrWhiteSpace(_options.PasswordSalt) &&
        _options.Iterations >= 100_000;

    private bool VerifyPassword(string password)
    {
        try
        {
            var salt = Convert.FromBase64String(_options.PasswordSalt);
            var expected = Convert.FromBase64String(_options.PasswordHash);
            var actual = Rfc2898DeriveBytes.Pbkdf2(
                password,
                salt,
                _options.Iterations,
                HashAlgorithmName.SHA256,
                expected.Length);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    public sealed record LoginRequest(string Username, string Password);
}
