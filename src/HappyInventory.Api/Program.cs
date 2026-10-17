using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using HappyInventory.Api;
using Microsoft.Extensions.Localization;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddLocalization(options => options.ResourcesPath = "Resources");
builder.Services.AddProblemDetails();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
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
// Fail closed until Phase 3 supplies validated issuer/audience/signing configuration.
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
builder.Services.AddAuthorization(options =>
    options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

var app = builder.Build();
app.UseExceptionHandler();
app.UseRequestLocalization(options => options
    .SetDefaultCulture("en")
    .AddSupportedCultures("en", "ar")
    .AddSupportedUICultures("en", "ar"));
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
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.Run();

public partial class Program;
