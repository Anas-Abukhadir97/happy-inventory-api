using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace HappyInventory.IntegrationTests;

public sealed class NotificationTests
{
    private static HttpClient Client(SqliteAppFactory factory) => factory.CreateClient(
        new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });

    private static async Task<string> Login(HttpClient client, string email, string password)
    {
        using var response = await client.PostAsJsonAsync("/api/v1/auth/login",
            new { email, password, sessionMode = "external" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString()!;
    }

    [Fact]
    public async Task AdminCanNotifyOneUserAndRecipientReceivesLiveEvent()
    {
        using var factory = new SqliteAppFactory();
        using var admin = Client(factory);
        admin.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            await Login(admin, "admin@happywarehouse.com", "test-only-bootstrap-password"));
        using var userCreated = await admin.PostAsJsonAsync("/api/v1/users", new
        {
            email = "auditor-notify@example.test",
            fullName = "Notification Auditor",
            roleId = 3,
            active = true,
            initialPassword = "auditor-test-password"
        });
        Assert.Equal(HttpStatusCode.Created, userCreated.StatusCode);
        var userId = (await userCreated.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();

        using var auditor = Client(factory);
        auditor.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            await Login(auditor, "auditor-notify@example.test", "auditor-test-password"));
        using var denied = await auditor.PostAsJsonAsync("/api/v1/notifications", new
        {
            recipientUserId = userId,
            title = "Denied",
            body = "No",
            sendEmail = false,
            sendSms = false
        });
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using var noEmail = await admin.PostAsJsonAsync("/api/v1/notifications", new
        {
            recipientUserId = userId,
            title = "Email",
            body = "No SMTP configured",
            sendEmail = true,
            sendSms = false
        });
        Assert.Equal(HttpStatusCode.ServiceUnavailable, noEmail.StatusCode);

        using var streamRequest = new HttpRequestMessage(HttpMethod.Get, "/api/v1/notifications/stream");
        using var stream = await auditor.SendAsync(streamRequest, HttpCompletionOption.ResponseHeadersRead);
        Assert.Equal(HttpStatusCode.OK, stream.StatusCode);
        Assert.Equal("text/event-stream", stream.Content.Headers.ContentType?.MediaType);
        using var reader = new StreamReader(await stream.Content.ReadAsStreamAsync());
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        Assert.Equal(": connected", await reader.ReadLineAsync(deadline.Token));
        Assert.Equal(string.Empty, await reader.ReadLineAsync(deadline.Token));

        using var created = await admin.PostAsJsonAsync("/api/v1/notifications", new
        {
            recipientUserId = userId,
            title = "Count stock",
            body = "Please verify aisle A.",
            sendEmail = false,
            sendSms = false
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString();
        Assert.Equal("event: notification", await reader.ReadLineAsync(deadline.Token));
        Assert.Equal($"data: {id}", await reader.ReadLineAsync(deadline.Token));

        using var own = await auditor.GetAsync("/api/v1/notifications");
        var items = (await own.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("items");
        Assert.Single(items.EnumerateArray());
        Assert.Equal("Count stock", items[0].GetProperty("title").GetString());
        Assert.Equal("NotRequested", items[0].GetProperty("emailStatus").GetString());
        using var adminList = await admin.GetAsync("/api/v1/notifications");
        Assert.Empty((await adminList.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("items").EnumerateArray());
        using var read = await auditor.PostAsync($"/api/v1/notifications/{id}/read", null);
        Assert.Equal(HttpStatusCode.NoContent, read.StatusCode);
        using var reread = await auditor.GetAsync("/api/v1/notifications");
        Assert.NotEqual(JsonValueKind.Null,
            (await reread.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("items")[0].GetProperty("readAtUtc").ValueKind);
    }
}
