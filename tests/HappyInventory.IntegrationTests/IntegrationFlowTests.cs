using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using HappyInventory.Application.Integrations;
using HappyInventory.Infrastructure.Data;
using HappyInventory.Infrastructure.Integrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace HappyInventory.IntegrationTests;

public sealed class IntegrationFlowTests
{
    private static async Task<string> Login(HttpClient client)
    {
        using var login = await client.PostAsJsonAsync("/api/v1/auth/login", new
        {
            email = "admin@happywarehouse.com",
            password = "test-only-bootstrap-password",
            sessionMode = "external"
        });
        login.EnsureSuccessStatusCode();
        return (await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString()!;
    }

    private static async Task<(int Warehouse, int Item)> Inventory(HttpClient client)
    {
        using var warehouse = await client.PostAsJsonAsync("/api/v1/warehouses", new
        { name = "Integration warehouse", address = "A", city = "Amman", countryCode = "JO" });
        warehouse.EnsureSuccessStatusCode();
        var warehouseId = (await warehouse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
        using var item = await client.PostAsJsonAsync($"/api/v1/warehouses/{warehouseId}/items", new
        { name = "Integration widget", qty = 5, costPrice = "1.00" });
        item.EnsureSuccessStatusCode();
        return (warehouseId, (await item.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32());
    }

    private static HttpRequestMessage Deduction(int warehouse, int item, int quantity, string key,
        string reference = "order-line-1")
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/integrations/inventory-deductions");
        request.Headers.TryAddWithoutValidation("Idempotency-Key", key);
        request.Content = JsonContent.Create(new
        {
            externalReference = reference,
            warehouseId = warehouse,
            itemId = item,
            quantity
        });
        return request;
    }

    [Fact]
    public async Task DeductionIsAtomicIdempotentAndPreservesMinimum()
    {
        using var factory = new SqliteAppFactory();
        using var client = factory.CreateClient(new() { AllowAutoRedirect = false });
        using var unauthorized = await client.SendAsync(Deduction(1, 1, 1, "unauthed"));
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await Login(client));
        var (warehouse, item) = await Inventory(client);

        using var first = await client.SendAsync(Deduction(warehouse, item, 2, "stable-key"));
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var firstBody = await first.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(3, firstBody.GetProperty("resultQuantity").GetInt32());
        Assert.False(firstBody.GetProperty("replayed").GetBoolean());
        using var replay = await client.SendAsync(Deduction(warehouse, item, 2, "stable-key"));
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        var replayBody = await replay.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(replayBody.GetProperty("replayed").GetBoolean());
        Assert.Equal(firstBody.GetProperty("deductionId").GetString(), replayBody.GetProperty("deductionId").GetString());
        using var changed = await client.SendAsync(Deduction(warehouse, item, 1, "stable-key"));
        Assert.Equal(HttpStatusCode.Conflict, changed.StatusCode);
        using var insufficient = await client.SendAsync(Deduction(warehouse, item, 3, "too-many"));
        Assert.Equal(HttpStatusCode.Conflict, insufficient.StatusCode);
        using var wrongWarehouse = await client.SendAsync(Deduction(warehouse + 1, item, 1, "wrong-place"));
        Assert.Equal(HttpStatusCode.NotFound, wrongWarehouse.StatusCode);
        using var second = await client.SendAsync(Deduction(warehouse, item, 2, "second-order", "order-line-2"));
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal(1, (await second.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("resultQuantity").GetInt32());
        using var read = await client.GetAsync($"/api/v1/warehouses/{warehouse}/items/{item}");
        Assert.Equal(1, (await read.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("qty").GetInt32());

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        Assert.Equal(2, await db.InventoryDeductions.CountAsync());
    }

    [Fact]
    public async Task SupplierOrderPersistsOutboxAndRetriesWithStableMessageId()
    {
        using var factory = new SqliteAppFactory();
        using var client = factory.CreateClient(new() { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await Login(client));
        var (warehouse, item) = await Inventory(client);
        using var created = await client.PostAsJsonAsync("/api/v1/supplier-orders", new
        {
            warehouseId = warehouse,
            itemId = item,
            requestedQuantity = 8,
            supplierDestination = "local-supplier@example.test"
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        var outbox = await db.SupplierOutboxMessages.SingleAsync(x => x.OrderRequestId == id);
        Assert.Equal("Pending", outbox.Status);
        var adapter = new FlakyAdapter();
        var dispatcher = new SupplierOutboxDispatcher(db, adapter, NullLogger<SupplierOutboxDispatcher>.Instance);
        Assert.Equal(0, await dispatcher.DispatchDueAsync(CancellationToken.None));
        Assert.Equal("Failed", outbox.Status);
        Assert.Equal(1, outbox.AttemptCount);
        outbox.NextAttemptAtUtc = DateTime.UtcNow.AddSeconds(-1);
        await db.SaveChangesAsync();
        Assert.Equal(1, await dispatcher.DispatchDueAsync(CancellationToken.None));
        Assert.Equal("Sent", outbox.Status);
        Assert.Equal(0, await dispatcher.DispatchDueAsync(CancellationToken.None));
        Assert.Equal(new[] { outbox.Id, outbox.Id }, adapter.Seen);
        using var list = await client.GetAsync("/api/v1/supplier-orders");
        Assert.Equal("Sent", (await list.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("items")[0].GetProperty("status").GetString());
    }

    [Fact]
    public async Task ConcurrentDistinctOrdersCannotOversell()
    {
        using var factory = new SqliteAppFactory();
        using var client = factory.CreateClient(new() { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await Login(client));
        var (warehouse, item) = await Inventory(client);
        using var one = Deduction(warehouse, item, 3, "concurrent-a", "line-a");
        using var two = Deduction(warehouse, item, 3, "concurrent-b", "line-b");
        var responses = await Task.WhenAll(client.SendAsync(one), client.SendAsync(two));
        using (responses[0])
        using (responses[1])
        {
            Assert.Single(responses, x => x.StatusCode == HttpStatusCode.OK);
            Assert.Single(responses, x => x.StatusCode == HttpStatusCode.Conflict);
        }
        using var read = await client.GetAsync($"/api/v1/warehouses/{warehouse}/items/{item}");
        Assert.Equal(2, (await read.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("qty").GetInt32());
        using var scope = factory.Services.CreateScope();
        Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<InventoryDbContext>()
            .InventoryDeductions.CountAsync());
    }

    [Fact]
    public async Task DeductionRecordFailureRollsBackStockUpdate()
    {
        using var factory = new SqliteAppFactory();
        using var client = factory.CreateClient(new() { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await Login(client));
        var (warehouse, item) = await Inventory(client);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
            await db.Database.ExecuteSqlRawAsync("""
                CREATE TRIGGER fail_deduction_insert BEFORE INSERT ON InventoryDeductions
                BEGIN SELECT RAISE(ABORT, 'simulated insert failure'); END;
                """);
        }
        using var failedRequest = Deduction(warehouse, item, 2, "failed-record");
        failedRequest.Headers.AcceptLanguage.ParseAdd("ar");
        using var failed = await client.SendAsync(failedRequest);
        Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode);
        Assert.Equal("application/problem+json", failed.Content.Headers.ContentType?.MediaType);
        var problem = await failed.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("internal_error", problem.GetProperty("code").GetString());
        Assert.Equal("حدث خطأ غير متوقع. يُرجى المحاولة مرة أخرى لاحقًا.",
            problem.GetProperty("title").GetString());
        Assert.False(string.IsNullOrWhiteSpace(problem.GetProperty("traceId").GetString()));
        Assert.DoesNotContain("simulated", problem.ToString(), StringComparison.OrdinalIgnoreCase);
        using var read = await client.GetAsync($"/api/v1/warehouses/{warehouse}/items/{item}");
        Assert.Equal(5, (await read.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("qty").GetInt32());
        using var check = factory.Services.CreateScope();
        Assert.Equal(0, await check.ServiceProvider.GetRequiredService<InventoryDbContext>()
            .InventoryDeductions.CountAsync());
    }

    [Fact]
    public async Task AuditorCanReadOrdersButCannotCreateOrDeduct()
    {
        using var factory = new SqliteAppFactory();
        using var admin = factory.CreateClient(new() { AllowAutoRedirect = false });
        admin.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await Login(admin));
        var (warehouse, item) = await Inventory(admin);
        using var created = await admin.PostAsJsonAsync("/api/v1/users", new
        {
            email = "integration-auditor@example.test",
            fullName = "Integration Auditor",
            roleId = 3,
            active = true,
            initialPassword = "auditor-password-123"
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        using var auditor = factory.CreateClient(new() { AllowAutoRedirect = false });
        using var login = await auditor.PostAsJsonAsync("/api/v1/auth/login", new
        {
            email = "integration-auditor@example.test",
            password = "auditor-password-123",
            sessionMode = "external"
        });
        auditor.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            (await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString());
        using var list = await auditor.GetAsync("/api/v1/supplier-orders");
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        using var forbiddenOrder = await auditor.PostAsJsonAsync("/api/v1/supplier-orders", new
        {
            warehouseId = warehouse,
            itemId = item,
            requestedQuantity = 1,
            supplierDestination = "local@example.test"
        });
        Assert.Equal(HttpStatusCode.Forbidden, forbiddenOrder.StatusCode);
        using var forbiddenDeduction = await auditor.SendAsync(Deduction(warehouse, item, 1, "auditor-key"));
        Assert.Equal(HttpStatusCode.Forbidden, forbiddenDeduction.StatusCode);
    }

    private sealed class FlakyAdapter : ISupplierNotificationAdapter
    {
        public List<Guid> Seen { get; } = [];
        public Task DeliverAsync(Guid messageId, string payloadJson, CancellationToken ct)
        {
            Seen.Add(messageId);
            if (Seen.Count == 1) throw new InvalidOperationException("Simulated local delivery failure");
            return Task.CompletedTask;
        }
    }
}
