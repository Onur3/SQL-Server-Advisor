using Microsoft.AspNetCore.Authentication.Cookies;
using SqlServerAdvisor.Analysis;
using SqlServerAdvisor.Api.Hubs;
using SqlServerAdvisor.Api.Security;
using SqlServerAdvisor.Infrastructure;
using SqlServerAdvisor.SqlServer;

var builder = WebApplication.CreateBuilder(args);
var appLoginEnabled = builder.Configuration.GetValue<bool>($"{AppLoginOptions.SectionName}:Enabled");

builder.Services.Configure<AppLoginOptions>(builder.Configuration.GetSection(AppLoginOptions.SectionName));
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = ".SqlServerAdvisor.Auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Strict;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.SlidingExpiration = true;
        options.Events.OnRedirectToLogin = context =>
        {
            if (context.Request.Path.StartsWithSegments("/api") || context.Request.Path.StartsWithSegments("/hubs"))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return Task.CompletedTask;
            }

            context.Response.Redirect(context.RedirectUri);
            return Task.CompletedTask;
        };
        options.Events.OnRedirectToAccessDenied = context =>
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        };
    });
builder.Services.AddAuthorization();

builder.Services.AddAdvisorInfrastructure(builder.Configuration);
builder.Services.AddMonitoredSqlServer();
builder.Services.AddAdvisorAnalysis();
builder.Services.AddControllers();
builder.Services.AddSignalR();
builder.Services.AddOpenApi();
builder.Services.AddCors(options =>
{
    options.AddPolicy("Angular", policy => policy
        .WithOrigins(builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? ["http://localhost:4200"])
        .AllowAnyHeader()
        .AllowAnyMethod()
        .AllowCredentials());
});

var app = builder.Build();

if (builder.Configuration.GetValue("HttpsRedirection:Enabled", true))
{
    app.UseHttpsRedirection();
}

app.UseDefaultFiles();
app.UseStaticFiles();
app.UseCors("Angular");
app.UseAuthentication();
app.UseAuthorization();

var controllers = app.MapControllers();
var advisorHub = app.MapHub<AdvisorHub>("/hubs/advisor");
if (appLoginEnabled)
{
    controllers.RequireAuthorization();
    advisorHub.RequireAuthorization();
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapGet("/health", () => Results.Ok(new { status = "ok", utc = DateTimeOffset.UtcNow }));

// Production deployment copies the Angular browser bundle into wwwroot.
// API and SignalR endpoints are mapped first; every remaining browser route
// falls back to Angular's index.html so IIS needs only one application/site.
app.MapFallbackToFile("index.html");

app.Run();
