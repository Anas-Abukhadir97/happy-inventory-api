using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using System.Net;
using System.Text.Json;
using System.Threading.RateLimiting;
using HappyInventory.Application.Auth;
using HappyInventory.Application.Health;
using HappyInventory.Application.Inventory;
using HappyInventory.Application.Integrations;
using HappyInventory.Application.Logs;
using HappyInventory.Application.Notifications;
using HappyInventory.Application.Users;
using HappyInventory.Api;
using HappyInventory.Infrastructure.Auth;
using HappyInventory.Infrastructure.Health;
using HappyInventory.Infrastructure.Data;
using HappyInventory.Infrastructure.Inventory;
using HappyInventory.Infrastructure.Integrations;
using HappyInventory.Infrastructure.Logs;
using HappyInventory.Infrastructure.Notifications;
using HappyInventory.Api.Notifications;
using HappyInventory.Infrastructure.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Localization;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;
using Serilog;
using Serilog.Formatting.Json;
using Serilog.Events;

var builder = WebApplication.CreateBuilder(args);
var appServiceHome = builder.Configuration.GetValue<bool>("Storage:UseAppServiceHome")
    ? Environment.GetEnvironmentVariable("HOME") ?? throw new InvalidOperationException("App Service HOME is required.")
    : null;
if (appServiceHome is not null && !Path.IsPathFullyQualified(appServiceHome))
    throw new InvalidOperationException("App Service HOME must be an absolute path.");
var logDirectory = builder.Configuration["EventLogs:Path"] ??
    Path.Combine(appServiceHome ?? builder.Environment.ContentRootPath, "data", "logs");
Directory.CreateDirectory(logDirectory);
builder.Host.UseSerilog((_, _, logger) => logger
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .WriteTo.Console()
    .WriteTo.File(new JsonFormatter(renderMessage: true), Path.Combine(logDirectory, "inventory-.json"),
        rollingInterval: RollingInterval.Day, retainedFileCountLimit: 14, fileSizeLimitBytes: 1_048_576,
        rollOnFileSizeLimit: true, shared: true));

builder.Services.AddControllers();
builder.Services.Configure<ApiBehaviorOptions>(options =>
{
    options.InvalidModelStateResponseFactory = context =>
    {
        var text = context.HttpContext.RequestServices.GetRequiredService<IStringLocalizer<SharedResource>>();
        var errors = context.ModelState.Where(entry => entry.Value?.Errors.Count > 0)
            .ToDictionary(entry => entry.Key, _ => new[] { text["InvalidRequest"].Value });
        var problem = new ValidationProblemDetails(errors)
        {
            Title = text["InvalidRequest"],
            Status = 400,
            Type = "about:blank",
            Detail = text["InvalidRequest"],
            Instance = context.HttpContext.Request.Path
        };
        problem.Extensions["code"] = "invalid_request";
        problem.Extensions["traceId"] = context.HttpContext.TraceIdentifier;
        return new ObjectResult(problem) { StatusCode = 400, ContentTypes = { "application/problem+json" } };
    };
});
builder.Services.AddDbContext<InventoryDbContext>((provider, options) =>
{
    var configuration = provider.GetRequiredService<IConfiguration>();
    if (appServiceHome is not null && configuration.GetConnectionString("Inventory") is not null)
        throw new InvalidOperationException("Do not configure an inventory connection string with Storage:UseAppServiceHome.");
    var inventoryConnection = appServiceHome is null
        ? configuration.GetConnectionString("Inventory") ?? "Data Source=data/inventory.db"
        : new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(appServiceHome, "data", "inventory.db"),
            Pooling = false
        }.ToString();
    options.UseSqlite(inventoryConnection);
});
builder.Services.AddSingleton(_ => AuthSettings.Load(builder.Configuration, builder.Environment.IsDevelopment()));
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IInventoryService, InventoryService>();
builder.Services.AddScoped<IIntegrationService, IntegrationService>();
builder.Services.AddScoped<ISupplierOutboxDispatcher, SupplierOutboxDispatcher>();
builder.Services.AddSingleton<ISupplierNotificationAdapter, LocalSupplierNotificationAdapter>();
if (builder.Configuration.GetValue("SupplierOutbox:WorkerEnabled", true))
    builder.Services.AddHostedService<SupplierOutboxWorker>();
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<INotificationService, NotificationService>();
builder.Services.AddSingleton<NotificationEventBroker>();
builder.Services.AddSingleton<INotificationPush>(provider => provider.GetRequiredService<NotificationEventBroker>());
builder.Services.AddScoped<IHealthService, HealthService>();
builder.Services.AddSingleton<IEventLogReader>(new EventLogReader(logDirectory));
builder.Services.AddLocalization(options => options.ResourcesPath = "Resources");
builder.Services.AddProblemDetails();
builder.Services.AddRateLimiter(options =>
{
    options.AddPolicy("login", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 10,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true
        }));
    options.OnRejected = (context, _) => new ValueTask(WriteAuthProblemAsync(
        context.HttpContext, 429, "rate_limited", "TooManyAttempts"));
});
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.Configure<SwaggerGenOptions>(options => options.OperationFilter<AnonymousSecurityOperationFilter>());
builder.Services.AddOptions<SwaggerGenOptions>()
    .Configure<IStringLocalizer<SharedResource>>((options, text) =>
    {
        options.SwaggerDoc("v1", new OpenApiInfo
        {
            Title = text["ApiDocumentationTitle"],
            Version = "v1-draft",
            Description = text["ApiDocumentationDescription"]
        });
        options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT"
        });
        options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
        {
            [new OpenApiSecuritySchemeReference("Bearer", document)] = []
        });
    });
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
{
    var authSettings = AuthSettings.Load(builder.Configuration, builder.Environment.IsDevelopment());
    options.MapInboundClaims = false;
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidIssuer = authSettings.Issuer,
        ValidateAudience = true,
        ValidAudience = authSettings.Audience,
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(authSettings.SigningKey),
        ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
        RequireSignedTokens = true,
        RequireExpirationTime = true,
        ValidateLifetime = true,
        ClockSkew = TimeSpan.Zero,
        NameClaimType = "sub",
        RoleClaimType = "role"
    };
    options.Events = new JwtBearerEvents
    {
        OnChallenge = context =>
        {
            context.HandleResponse();
            context.Response.Headers["WWW-Authenticate"] = "Bearer";
            return WriteAuthProblemAsync(context.HttpContext, 401, "authentication_required", "AuthenticationRequired");
        },
        OnForbidden = context => WriteAuthProblemAsync(context.HttpContext, 403, "access_forbidden", "AccessForbidden"),
        OnTokenValidated = async context =>
        {
            var claims = context.Principal;
            if (!int.TryParse(claims?.FindFirst("sub")?.Value, out var id) ||
                !int.TryParse(claims?.FindFirst("sv")?.Value, out var securityVersion))
            {
                context.Fail("Invalid token claims");
                return;
            }
            var familyText = claims.FindFirst("fid")?.Value;
            Guid? family = null;
            if (familyText is not null)
            {
                if (!Guid.TryParse(familyText, out var parsed))
                {
                    context.Fail("Invalid session claim");
                    return;
                }
                family = parsed;
            }
            var service = context.HttpContext.RequestServices.GetRequiredService<IAuthService>();
            var user = await service.ValidateAccessAsync(id, securityVersion, family, context.HttpContext.RequestAborted);
            if (user is null || user.Role != claims.FindFirst("role")?.Value)
                context.Fail("Account or session is no longer valid");
            else
                context.HttpContext.Items["CurrentUser"] = user;
        }
    };
});
builder.Services.AddAuthorization(options =>
    options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());
var knownProxy = builder.Configuration["ReverseProxy:KnownProxy"];
if (!string.IsNullOrWhiteSpace(knownProxy))
{
    if (!IPAddress.TryParse(knownProxy, out var proxyAddress))
        throw new InvalidOperationException("ReverseProxy:KnownProxy must be an IP address.");
    builder.Services.Configure<ForwardedHeadersOptions>(options =>
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        options.ForwardLimit = 1;
        options.KnownProxies.Add(proxyAddress);
    });
}

var app = builder.Build();
_ = app.Services.GetRequiredService<AuthSettings>();
using (var scope = app.Services.CreateScope())
{
    await DatabaseInitializer.InitializeAsync(
        scope.ServiceProvider.GetRequiredService<InventoryDbContext>(),
        app.Configuration,
        app.Environment.IsDevelopment());
}
app.UseExceptionHandler();
if (!string.IsNullOrWhiteSpace(knownProxy)) app.UseForwardedHeaders();
app.UseRequestLocalization(options => options
    .SetDefaultCulture("en")
    .AddSupportedCultures("en", "ar")
    .AddSupportedUICultures("en", "ar"));
if (!app.Environment.IsDevelopment())
{
    app.Use(async (context, next) =>
    {
        if (!context.Request.IsHttps &&
            !(IsLocalProbe(context) && context.Request.Path.StartsWithSegments("/health")))
        {
            await WriteAuthProblemAsync(context, 400, "https_required", "HttpsRequired");
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

static Task WriteAuthProblemAsync(HttpContext context, int status, string code, string resourceKey)
{
    var text = context.RequestServices.GetRequiredService<IStringLocalizer<SharedResource>>();
    context.Response.StatusCode = status;
    context.Response.ContentType = "application/problem+json";
    context.Response.Headers.CacheControl = "no-store";
    return context.Response.WriteAsync(JsonSerializer.Serialize(new
    {
        type = "about:blank",
        title = text[resourceKey].Value,
        status,
        detail = text[resourceKey].Value,
        code,
        traceId = context.TraceIdentifier
    }));
}

public partial class Program;
