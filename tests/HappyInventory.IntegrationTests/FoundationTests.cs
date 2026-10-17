using System.Globalization;
using System.Net;
using HappyInventory.Api;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Swashbuckle.AspNetCore.Swagger;

namespace HappyInventory.IntegrationTests;

public sealed class FoundationTests(SqliteAppFactory factory)
    : IClassFixture<SqliteAppFactory>
{
    [Theory]
    [InlineData("/")]
    [InlineData("/api/v1/warehouses")]
    public async Task ScaffoldDoesNotExposeAnonymousEndpoints(string path)
    {
        using var client = factory.CreateClient();
        using var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ArbitraryBearerTokenDoesNotGrantAccess()
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", "invalid-token");
        using var response = await client.GetAsync("/api/v1/warehouses");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("Development", 5081, "/swagger/v1/swagger.json", 200)]
    [InlineData("Development", 5081, "/swagger/index.html", 200)]
    [InlineData("Development", 5080, "/swagger/v1/swagger.json", 401)]
    [InlineData("Development", 5080, "/swagger/index.html", 401)]
    [InlineData("Production", 5081, "/swagger/v1/swagger.json", 401)]
    [InlineData("Production", 5081, "/swagger/index.html", 401)]
    public async Task SwaggerIsOnlyAvailableOnTheDevelopmentDocumentationListener(
        string environment, int localPort, string path, int status)
    {
        using var host = factory.WithWebHostBuilder(builder => builder.UseEnvironment(environment));
        var response = await host.Server.SendAsync(context =>
        {
            context.Request.Method = "GET";
            context.Request.Path = path;
            // Spoofing Host must not expose docs on the public connection port.
            context.Request.Host = new HostString("localhost", 5081);
            context.Connection.LocalPort = localPort;
        });
        Assert.Equal(status, response.Response.StatusCode);
    }

    [Fact]
    public void SwaggerDescribesBearerAuthentication()
    {
        var provider = factory.Services.GetRequiredService<ISwaggerProvider>();
        var document = provider.GetSwagger("v1");
        Assert.Equal("bearer", document.Components!.SecuritySchemes!["Bearer"].Scheme);
        Assert.NotEmpty(document.Security!);
    }

    [Theory]
    [InlineData("en", "Happy Inventory")]
    [InlineData("ar", "هابي للمخزون")]
    public void ApiResourcesResolveForSupportedCultures(string culture, string expected)
    {
        var previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
            var localizer = factory.Services.GetRequiredService<IStringLocalizer<SharedResource>>();
            Assert.False(localizer["ApplicationName"].ResourceNotFound);
            Assert.Equal(expected, localizer["ApplicationName"].Value);
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }
}
