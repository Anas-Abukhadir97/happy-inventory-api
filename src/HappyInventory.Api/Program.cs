using System.Net;
using HappyInventory.Api;
using HappyInventory.Application.Health;
using HappyInventory.Infrastructure.Auth;
using HappyInventory.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using Serilog;

var builder = WebApplication.CreateBuilder(args);
builder.AddHappyInventoryServices();

var knownProxy = builder.Configuration["ReverseProxy:KnownProxy"];
var app = builder.Build();
_ = app.Services.GetRequiredService<AuthSettings>();
using (var scope = app.Services.CreateScope())
{
    await DatabaseInitializer.InitializeAsync(
        scope.ServiceProvider.GetRequiredService<InventoryDbContext>(),
        app.Configuration,
        app.Environment.IsDevelopment());
}
if (!string.IsNullOrWhiteSpace(knownProxy)) app.UseForwardedHeaders();
app.UseRequestLocalization(options => options
    .SetDefaultCulture("en")
    .AddSupportedCultures("en", "ar")
    .AddSupportedUICultures("en", "ar"));
app.UseExceptionHandler();
if (!app.Environment.IsDevelopment())
{
    app.Use(async (context, next) =>
    {
        if (!context.Request.IsHttps &&
            !(IsLocalProbe(context) && context.Request.Path.StartsWithSegments("/health")))
        {
            await ApiProblemResponses.WriteAsync(context, 400, "https_required", "HttpsRequired");
            return;
        }
        await next();
    });
}
if (app.Environment.IsDevelopment())
{
    // Inspect the actual connection port, never a caller-controlled Host header.
    app.UseWhen(context => context.Connection.LocalPort == 5081, docs =>
    {
        docs.UseSwagger();
        docs.UseSwaggerUI(options =>
        {
            var text = app.Services.GetRequiredService<IStringLocalizer<SharedResource>>();
            options.DocumentTitle = text["ApiDocumentationTitle"];
            options.SwaggerEndpoint("/swagger/v1/swagger.json", "v1-draft");
            options.ConfigObject.PersistAuthorization = false;
        });
    });
}
app.UseRouting();
// Azure's free App Service hosts the compiled SPA and API on one HTTPS origin.
// This is opt-in so API-only deployments and development keep their current routing.
if (app.Configuration.GetValue<bool>("Web:ServeSpa"))
{
    app.UseDefaultFiles();
    app.UseStaticFiles();
}
app.UseSerilogRequestLogging(options => options.EnrichDiagnosticContext = (diagnostic, context) =>
    diagnostic.Set("CorrelationId", context.TraceIdentifier));
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.MapGet("/health/live", (HttpContext context) =>
    IsLocalProbe(context) ? Results.NoContent() : Results.NotFound()).AllowAnonymous();
app.MapGet("/health/ready", async (HttpContext context, IHealthService health, CancellationToken ct) =>
{
    if (!IsLocalProbe(context)) return Results.NotFound();
    return await health.IsReadyAsync(ct) ? Results.NoContent() : Results.StatusCode(503);
}).AllowAnonymous();
app.MapControllers();
if (app.Configuration.GetValue<bool>("Web:ServeSpa"))
{
    // Unknown API paths must never receive the SPA shell.
    app.Map("/api/{**path}", () => Results.NotFound()).AllowAnonymous();
    app.MapFallbackToFile("index.html").AllowAnonymous();
}
app.Run();

static bool IsLocalProbe(HttpContext context) =>
    IPAddress.IsLoopback(context.Connection.RemoteIpAddress ?? IPAddress.None);

public partial class Program;
