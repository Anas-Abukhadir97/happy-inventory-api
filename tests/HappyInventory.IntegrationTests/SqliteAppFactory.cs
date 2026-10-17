using HappyInventory.Api;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace HappyInventory.IntegrationTests;

public sealed class SqliteAppFactory : WebApplicationFactory<Program>
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "happy-inventory-tests", Guid.NewGuid().ToString("N"));

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        Directory.CreateDirectory(_directory);
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["ConnectionStrings:Inventory"] = $"Data Source={Path.Combine(_directory, "inventory.db")};Pooling=False",
                ["BootstrapAdmin:Password"] = "test-only-bootstrap-password",
                ["Auth:SigningKey"] = "test-only-signing-key-with-at-least-thirty-two-characters",
                ["Auth:BrowserOrigin"] = "https://localhost:5173",
                ["EventLogs:Path"] = Path.Combine(_directory, "logs"),
                ["SupplierOutbox:WorkerEnabled"] = "false"
            }));
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing && Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}
