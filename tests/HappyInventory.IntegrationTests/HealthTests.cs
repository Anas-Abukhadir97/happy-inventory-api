using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace HappyInventory.IntegrationTests;

public sealed class HealthTests
{
    [Fact]
    public async Task ContractHealthRequiresJwtAndReportsReadyDatabase()
    {
        using var factory = new SqliteAppFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/health")).StatusCode);

        using var login = await client.PostAsJsonAsync("/api/v1/auth/login", new
        {
            email = "admin@happywarehouse.com",
            password = "test-only-bootstrap-password",
            sessionMode = "external"
        });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        using var body = JsonDocument.Parse(await login.Content.ReadAsStringAsync());
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", body.RootElement.GetProperty("accessToken").GetString());

        using var health = await client.GetAsync("/api/v1/health");
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
        using var status = JsonDocument.Parse(await health.Content.ReadAsStringAsync());
        Assert.Equal("healthy", status.RootElement.GetProperty("status").GetString());
    }
}
