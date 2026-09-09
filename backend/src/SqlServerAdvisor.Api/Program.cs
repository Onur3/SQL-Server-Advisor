using SqlServerAdvisor.Analysis;
using SqlServerAdvisor.Api.Hubs;
using SqlServerAdvisor.Infrastructure;
using SqlServerAdvisor.SqlServer;

var builder = WebApplication.CreateBuilder(args);

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
app.MapControllers();
app.MapHub<AdvisorHub>("/hubs/advisor");

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
