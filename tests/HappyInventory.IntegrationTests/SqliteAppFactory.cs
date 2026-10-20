using HappyInventory.Api;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace HappyInventory.IntegrationTests;

public sealed class SqliteAppFactory : WebApplicationFactory<Program>
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "happy-inventory-tests", Guid.NewGuid().ToString("N"));
    private readonly IReadOnlyDictionary<string, string?> _settings;

    public SqliteAppFactory() : this(new Dictionary<string, string?>()) { }

    internal SqliteAppFactory(IReadOnlyDictionary<string, string?> settings) => _settings = settings;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        Directory.CreateDirectory(_directory);
        builder.ConfigureAppConfiguration((_, config) =>
        {
            var settings = new Dictionary<string, string?>
            {
                ["ConnectionStrings:Inventory"] = $"Data Source={Path.Combine(_directory, "inventory.db")};Pooling=False",
                ["BootstrapAdmin:Password"] = "test-only-bootstrap-password",
                ["Auth:SigningKey"] = "test-only-signing-key-with-at-least-thirty-two-characters",
                ["Auth:BrowserOrigin"] = "https://localhost:5173",
                ["EventLogs:Path"] = Path.Combine(_directory, "logs"),
                ["SupplierOutbox:WorkerEnabled"] = "false"
            };
            foreach (var (key, value) in _settings) settings[key] = value;
            config.AddInMemoryCollection(settings);
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing && Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}
