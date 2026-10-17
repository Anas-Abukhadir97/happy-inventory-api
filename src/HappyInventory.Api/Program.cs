using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using System.Threading.RateLimiting;
using HappyInventory.Application.Auth;
using HappyInventory.Application.Inventory;
using HappyInventory.Application.Logs;
using HappyInventory.Application.Users;
using HappyInventory.Api;
using HappyInventory.Infrastructure.Auth;
using HappyInventory.Infrastructure.Data;
using HappyInventory.Infrastructure.Inventory;
using HappyInventory.Infrastructure.Logs;
using HappyInventory.Infrastructure.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;
using Serilog;
using Serilog.Formatting.Json;
using Serilog.Events;

var builder = WebApplication.CreateBuilder(args);
var logDirectory = builder.Configuration["EventLogs:Path"] ??
    Path.Combine(builder.Environment.ContentRootPath, "data", "logs");
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
builder.Services.AddDbContext<InventoryDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("Inventory") ?? "Data Source=data/inventory.db"));
builder.Services.AddSingleton(_ => AuthSettings.Load(builder.Configuration, builder.Environment.IsDevelopment()));
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IInventoryService, InventoryService>();
builder.Services.AddScoped<IUserService, UserService>();
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
app.UseRequestLocalization(options => options
    .SetDefaultCulture("en")
    .AddSupportedCultures("en", "ar")
    .AddSupportedUICultures("en", "ar"));
if (!app.Environment.IsDevelopment())
{
    app.Use(async (context, next) =>
    {
        if (!context.Request.IsHttps)
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
app.UseSerilogRequestLogging(options => options.EnrichDiagnosticContext = (diagnostic, context) =>
    diagnostic.Set("CorrelationId", context.TraceIdentifier));
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.Run();

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
