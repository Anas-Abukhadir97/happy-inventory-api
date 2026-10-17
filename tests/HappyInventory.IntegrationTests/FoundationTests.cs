using System.Globalization;
using System.Net;
using HappyInventory.Api;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;

namespace HappyInventory.IntegrationTests;

public sealed class FoundationTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
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
