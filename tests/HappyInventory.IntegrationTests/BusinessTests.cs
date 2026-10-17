using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace HappyInventory.IntegrationTests;

public sealed class BusinessTests
{
    private static HttpClient Client(SqliteAppFactory factory) => factory.CreateClient(
        new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });

    private static async Task<string> Login(HttpClient client, string email = "admin@happywarehouse.com",
        string password = "test-only-bootstrap-password")
    {
        using var response = await client.PostAsJsonAsync("/api/v1/auth/login",
            new { email, password, sessionMode = "external" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("accessToken").GetString()!;
    }

    private static HttpRequestMessage Change(HttpMethod method, string path, string? etag, object? body = null)
    {
        var request = new HttpRequestMessage(method, path);
        if (etag is not null) request.Headers.TryAddWithoutValidation("If-Match", etag);
        if (body is not null) request.Content = JsonContent.Create(body);
        return request;
    }

    [Fact]
    public async Task ListsAreCappedAndNamesStayGloballyUnique()
    {
        using var factory = new SqliteAppFactory();
        using var client = Client(factory);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await Login(client));
        var firstId = 0;
        for (var i = 0; i < 12; i++)
        {
            using var created = await client.PostAsJsonAsync("/api/v1/warehouses",
                new { name = $"Warehouse {i}", address = "A", city = "Amman", countryCode = "JO" });
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            if (i == 0) firstId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
        }
        using var first = await client.GetAsync("/api/v1/warehouses?page=1&pageSize=10");
        var firstPage = await first.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(10, firstPage.GetProperty("items").GetArrayLength());
        Assert.Equal("12", firstPage.GetProperty("totalCount").GetString());
        using var second = await client.GetAsync("/api/v1/warehouses?page=2&pageSize=10");
        Assert.Equal(2, (await second.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("items").GetArrayLength());
        using var oversized = await client.GetAsync("/api/v1/warehouses?pageSize=11");
        Assert.Equal(HttpStatusCode.BadRequest, oversized.StatusCode);
        using var arabicRequest = new HttpRequestMessage(HttpMethod.Get, "/api/v1/warehouses?pageSize=11");
        arabicRequest.Headers.AcceptLanguage.ParseAdd("ar");
        using var arabicError = await client.SendAsync(arabicRequest);
        Assert.Equal(HttpStatusCode.BadRequest, arabicError.StatusCode);
        Assert.Contains("تحقق", await arabicError.Content.ReadAsStringAsync());
        using var emptyStatus = await client.GetAsync("/api/v1/dashboard/warehouses");
        var empty = (await emptyStatus.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("items")[0];
        Assert.Equal("0", empty.GetProperty("distinctItemCount").GetString());
        Assert.Equal("0", empty.GetProperty("totalQuantity").GetString());

        using var item = await client.PostAsJsonAsync($"/api/v1/warehouses/{firstId}/items",
            new { name = "Single item", qty = 1, costPrice = "0.00" });
        Assert.Equal(HttpStatusCode.Created, item.StatusCode);
        using var elsewhere = await client.PostAsJsonAsync($"/api/v1/warehouses/{firstId + 1}/items",
            new { name = "single ITEM", qty = 1, costPrice = "0.00" });
        Assert.Equal(HttpStatusCode.Conflict, elsewhere.StatusCode);
        using var fraction = await client.PostAsJsonAsync($"/api/v1/warehouses/{firstId}/items",
            new { name = "Fraction", qty = 1, costPrice = "1.234" });
        Assert.Equal(HttpStatusCode.BadRequest, fraction.StatusCode);
    }

    [Fact]
    public async Task InventoryCrudDashboardAndConcurrency()
    {
        using var factory = new SqliteAppFactory();
        using var client = Client(factory);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await Login(client));

        using var countries = await client.GetAsync("/api/v1/countries");
        Assert.Equal(HttpStatusCode.OK, countries.StatusCode);
        using var badCountry = await client.PostAsJsonAsync("/api/v1/warehouses",
            new { name = "Wrong", address = "A", city = "Amman", countryCode = "ZZ" });
        Assert.Equal(HttpStatusCode.BadRequest, badCountry.StatusCode);

        using var create = await client.PostAsJsonAsync("/api/v1/warehouses",
            new { name = " Main ", address = "Street", city = "Amman", countryCode = "JO" });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var warehouseId = (await create.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
        Assert.Equal($"/api/v1/warehouses/{warehouseId}", create.Headers.Location?.OriginalString);
        var warehouseTag = create.Headers.ETag!.ToString();
        Assert.Equal(38, warehouseTag.Length);

        using var duplicate = await client.PostAsJsonAsync("/api/v1/warehouses",
            new { name = "main", address = "Street", city = "Amman", countryCode = "JO" });
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        using var noTag = await client.SendAsync(Change(HttpMethod.Put, $"/api/v1/warehouses/{warehouseId}", null,
            new { name = "Main 2", address = "Street", city = "Amman", countryCode = "JO" }));
        Assert.Equal((HttpStatusCode)428, noTag.StatusCode);
        using var update = await client.SendAsync(Change(HttpMethod.Put, $"/api/v1/warehouses/{warehouseId}", warehouseTag,
            new { name = "Main 2", address = "Street", city = "Amman", countryCode = "JO" }));
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        using var stale = await client.SendAsync(Change(HttpMethod.Delete, $"/api/v1/warehouses/{warehouseId}", warehouseTag));
        Assert.Equal(HttpStatusCode.PreconditionFailed, stale.StatusCode);
        warehouseTag = update.Headers.ETag!.ToString();

        using var invalid = await client.PostAsJsonAsync($"/api/v1/warehouses/{warehouseId}/items",
            new { name = "Widget", qty = 0, costPrice = "1.00" });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        using var item = await client.PostAsJsonAsync($"/api/v1/warehouses/{warehouseId}/items",
            new { name = "Widget", qty = 5, costPrice = "12.50", msrpPrice = "14.00" });
        Assert.Equal(HttpStatusCode.Created, item.StatusCode);
        var itemBody = await item.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("12.50", itemBody.GetProperty("costPrice").GetString());
        var itemId = itemBody.GetProperty("id").GetInt32();
        Assert.Equal($"/api/v1/warehouses/{warehouseId}/items/{itemId}", item.Headers.Location?.OriginalString);

        using var full = await client.GetAsync("/api/v1/dashboard/warehouses");
        Assert.Equal(HttpStatusCode.OK, full.StatusCode);
        var status = (await full.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("items")[0];
        Assert.Equal("1", status.GetProperty("distinctItemCount").GetString());
        Assert.Equal("5", status.GetProperty("totalQuantity").GetString());
        using var highest = await client.GetAsync("/api/v1/dashboard/items/highest");
        Assert.Equal(itemId, (await highest.Content.ReadFromJsonAsync<JsonElement>())[0].GetProperty("id").GetInt32());

        using var nonempty = await client.SendAsync(Change(HttpMethod.Delete, $"/api/v1/warehouses/{warehouseId}", warehouseTag));
        Assert.Equal(HttpStatusCode.Conflict, nonempty.StatusCode);
        using var deleteItem = await client.SendAsync(Change(HttpMethod.Delete,
            $"/api/v1/warehouses/{warehouseId}/items/{itemId}", item.Headers.ETag!.ToString()));
        Assert.Equal(HttpStatusCode.NoContent, deleteItem.StatusCode);
        using var deleteWarehouse = await client.SendAsync(Change(HttpMethod.Delete, $"/api/v1/warehouses/{warehouseId}", warehouseTag));
        Assert.Equal(HttpStatusCode.NoContent, deleteWarehouse.StatusCode);
        using var absent = await client.GetAsync($"/api/v1/warehouses/{warehouseId}");
        Assert.Equal(HttpStatusCode.NotFound, absent.StatusCode);
    }

    [Fact]
    public async Task UserManagementEnforcesRolesAndRevokesChangedAccounts()
    {
        using var factory = new SqliteAppFactory();
        using var client = Client(factory);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await Login(client));
        using var roles = await client.GetAsync("/api/v1/roles");
        Assert.Equal(HttpStatusCode.OK, roles.StatusCode);
        using var created = await client.PostAsJsonAsync("/api/v1/users", new
        {
            email = "auditor@example.com",
            fullName = "Test Auditor",
            roleId = 3,
            active = true,
            initialPassword = "test-password-123"
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
        Assert.Equal($"/api/v1/users/{id}", created.Headers.Location?.OriginalString);
        var auditorToken = await Login(client, "auditor@example.com", "test-password-123");
        using var auditor = Client(factory);
        auditor.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auditorToken);
        using var read = await auditor.GetAsync("/api/v1/warehouses");
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        using var denied = await auditor.PostAsJsonAsync("/api/v1/warehouses",
            new { name = "Denied", address = "A", city = "Amman", countryCode = "JO" });
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using var deniedUsers = await auditor.GetAsync("/api/v1/users");
        Assert.Equal(HttpStatusCode.Forbidden, deniedUsers.StatusCode);
        using var deniedLogs = await auditor.GetAsync("/api/v1/logs");
        Assert.Equal(HttpStatusCode.Forbidden, deniedLogs.StatusCode);
        using var logs = await client.GetAsync("/api/v1/logs?pageSize=10");
        Assert.Equal(HttpStatusCode.OK, logs.StatusCode);
        Assert.DoesNotContain("test-only-bootstrap-password", await logs.Content.ReadAsStringAsync());
        var logBody = await logs.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(logBody.GetProperty("items").GetArrayLength() > 0);
        var logId = logBody.GetProperty("items")[0].GetProperty("id").GetString();
        using var detail = await client.GetAsync($"/api/v1/logs/{logId}");
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);

        using var changed = await client.SendAsync(Change(HttpMethod.Put, $"/api/v1/users/{id}",
            created.Headers.ETag!.ToString(), new
            {
                email = "auditor@example.com",
                fullName = "Test Auditor",
                roleId = 2,
                active = true
            }));
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        using var revoked = await auditor.GetAsync("/api/v1/warehouses");
        Assert.Equal(HttpStatusCode.Unauthorized, revoked.StatusCode);
        using var management = Client(factory);
        management.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            await Login(client, "auditor@example.com", "test-password-123"));
        using var managementWrite = await management.PostAsJsonAsync("/api/v1/warehouses",
            new { name = "Managed location", address = "A", city = "Amman", countryCode = "JO" });
        Assert.Equal(HttpStatusCode.Created, managementWrite.StatusCode);
        using var managementLogs = await management.GetAsync("/api/v1/logs");
        Assert.Equal(HttpStatusCode.Forbidden, managementLogs.StatusCode);

        using var disabled = await client.SendAsync(Change(HttpMethod.Put, $"/api/v1/users/{id}",
            changed.Headers.ETag!.ToString(), new
            {
                email = "auditor@example.com",
                fullName = "Test Auditor",
                roleId = 2,
                active = false
            }));
        Assert.Equal(HttpStatusCode.OK, disabled.StatusCode);
        using var disabledLogin = await client.PostAsJsonAsync("/api/v1/auth/login", new
        { email = "auditor@example.com", password = "test-password-123", sessionMode = "external" });
        Assert.Equal(HttpStatusCode.Forbidden, disabledLogin.StatusCode);

        using var users = await client.GetAsync("/api/v1/users");
        var system = (await users.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("items")[0];
        using var protectedAdmin = await client.SendAsync(Change(HttpMethod.Delete,
            $"/api/v1/users/{system.GetProperty("id").GetInt32()}", $"\"{system.GetProperty("version").GetString()}\""));
        Assert.Equal(HttpStatusCode.Conflict, protectedAdmin.StatusCode);
    }

    [Fact]
    public async Task PasswordResetAndDeletionInvalidateAccess()
    {
        using var factory = new SqliteAppFactory();
        using var client = Client(factory);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await Login(client));
        using var created = await client.PostAsJsonAsync("/api/v1/users", new
        {
            email = "manager@example.com",
            fullName = "Test Manager",
            roleId = 2,
            active = true,
            initialPassword = "first-password-123"
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
        var managerToken = await Login(client, "manager@example.com", "first-password-123");
        using var manager = Client(factory);
        manager.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", managerToken);
        using var before = await manager.GetAsync("/api/v1/warehouses");
        Assert.Equal(HttpStatusCode.OK, before.StatusCode);
        using var reset = await client.SendAsync(Change(HttpMethod.Put, $"/api/v1/users/{id}/password",
            created.Headers.ETag!.ToString(), new { newPassword = "second-password-123" }));
        Assert.Equal(HttpStatusCode.NoContent, reset.StatusCode);
        using var afterReset = await manager.GetAsync("/api/v1/warehouses");
        Assert.Equal(HttpStatusCode.Unauthorized, afterReset.StatusCode);
        var freshToken = await Login(client, "manager@example.com", "second-password-123");
        manager.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", freshToken);
        using var refreshedUser = await client.GetAsync($"/api/v1/users/{id}");
        using var deleted = await client.SendAsync(Change(HttpMethod.Delete, $"/api/v1/users/{id}",
            refreshedUser.Headers.ETag!.ToString()));
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        using var afterDelete = await manager.GetAsync("/api/v1/warehouses");
        Assert.Equal(HttpStatusCode.Unauthorized, afterDelete.StatusCode);
    }
}
