using HappyInventory.Domain.Entities;
using HappyInventory.Domain.ValueObjects;
using HappyInventory.Infrastructure.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace HappyInventory.IntegrationTests;

public sealed class PersistenceTests
{
    private static (SqliteConnection Connection, InventoryDbContext Database) OpenDatabase()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<InventoryDbContext>()
            .UseSqlite(connection).Options;
        return (connection, new InventoryDbContext(options));
    }

    private static IConfiguration Configuration(string? password = "test-only-bootstrap-password") =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["BootstrapAdmin:Password"] = password
        }).Build();

    [Fact]
    public async Task MigrationAndSeedingCanRunTwiceWithoutChangingAdmin()
    {
        var (connection, database) = OpenDatabase();
        await using var ownedConnection = connection;
        await using var ownedDatabase = database;
        await DatabaseInitializer.InitializeAsync(database, Configuration(), isDevelopment: false);
        var original = await database.Users.SingleAsync();
        Assert.Equal(1, original.RoleId);
        Assert.True(original.IsSystemAdmin);
        Assert.True(original.IsActive);
        Assert.NotEqual("test-only-bootstrap-password", original.PasswordHash);
        Assert.Equal(PasswordVerificationResult.Success,
            new PasswordHasher<User>().VerifyHashedPassword(original, original.PasswordHash, "test-only-bootstrap-password"));
        Assert.Equal(["Admin", "Management", "Auditor"],
            await database.Roles.OrderBy(x => x.Id).Select(x => x.Name).ToArrayAsync());
        Assert.Equal(5, await database.Countries.CountAsync());

        await DatabaseInitializer.InitializeAsync(database, Configuration("a-different-password"), isDevelopment: false);
        Assert.Equal(1, await database.Users.CountAsync());
        Assert.Equal(original.PasswordHash, (await database.Users.SingleAsync()).PasswordHash);
    }

    [Fact]
    public async Task FirstProductionStartRequiresConfiguredPassword()
    {
        var (connection, database) = OpenDatabase();
        await using var ownedConnection = connection;
        await using var ownedDatabase = database;
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            DatabaseInitializer.InitializeAsync(database, Configuration(null), isDevelopment: false));
        Assert.Empty(await database.Users.ToListAsync());
    }

    [Fact]
    public async Task SQLiteEnforcesInventoryUniquenessQuantityPriceAndForeignKeys()
    {
        var (connection, database) = OpenDatabase();
        await using var ownedConnection = connection;
        await using var ownedDatabase = database;
        await DatabaseInitializer.InitializeAsync(database, Configuration(), isDevelopment: false);
        var warehouse = new Warehouse
        {
            Name = "Main",
            NormalizedName = NormalizedText.Key("main"),
            Address = "Address",
            City = "Amman",
            CountryCode = "JO"
        };
        database.Warehouses.Add(warehouse);
        await database.SaveChangesAsync();

        database.Warehouses.Add(new Warehouse
        {
            Name = "MAIN",
            NormalizedName = NormalizedText.Key("MAIN"),
            Address = "Other",
            City = "Amman",
            CountryCode = "JO"
        });
        await Assert.ThrowsAsync<DbUpdateException>(() => database.SaveChangesAsync());
        database.ChangeTracker.Clear();

        var item = new Item
        {
            Name = "Widget",
            NormalizedName = NormalizedText.Key("Widget"),
            WarehouseId = warehouse.Id,
            Qty = 1,
            CostMinor = Money.ToMinor(12.50m),
            MsrpMinor = Money.ToMinor(15.00m)
        };
        database.Items.Add(item);
        await database.SaveChangesAsync();
        Assert.Equal(12.50m, Money.FromMinor((await database.Items.SingleAsync()).CostMinor));

        database.Items.Add(new Item
        {
            Name = "widget",
            NormalizedName = NormalizedText.Key("widget"),
            WarehouseId = warehouse.Id,
            Qty = 1,
            CostMinor = 1
        });
        await Assert.ThrowsAsync<DbUpdateException>(() => database.SaveChangesAsync());
        database.ChangeTracker.Clear();

        database.Items.Add(new Item
        {
            Name = "Invalid quantity",
            NormalizedName = NormalizedText.Key("Invalid quantity"),
            WarehouseId = warehouse.Id,
            Qty = 0,
            CostMinor = 1
        });
        await Assert.ThrowsAsync<DbUpdateException>(() => database.SaveChangesAsync());
        database.ChangeTracker.Clear();

        database.Items.Add(new Item
        {
            Name = "Invalid price",
            NormalizedName = NormalizedText.Key("Invalid price"),
            WarehouseId = warehouse.Id,
            Qty = 1,
            CostMinor = -1
        });
        await Assert.ThrowsAsync<DbUpdateException>(() => database.SaveChangesAsync());
        database.ChangeTracker.Clear();

        database.Items.Add(new Item
        {
            Name = "Missing warehouse",
            NormalizedName = NormalizedText.Key("Missing warehouse"),
            WarehouseId = 999999,
            Qty = 1,
            CostMinor = 1
        });
        await Assert.ThrowsAsync<DbUpdateException>(() => database.SaveChangesAsync());
        database.ChangeTracker.Clear();

        var existing = await database.Items.SingleAsync();
        existing.DeletedAtUtc = DateTime.UtcNow;
        await database.SaveChangesAsync();
        database.Items.Add(new Item
        {
            Name = "widget",
            NormalizedName = NormalizedText.Key("widget"),
            WarehouseId = warehouse.Id,
            Qty = 1,
            CostMinor = 1
        });
        await database.SaveChangesAsync();
        Assert.Single(await database.Items.ToListAsync());
        Assert.Equal(2, await database.Items.IgnoreQueryFilters().CountAsync());
    }

    [Theory]
    [InlineData("0.001")]
    [InlineData("-1")]
    [InlineData("1000000000")]
    public void MinorUnitConversionRejectsUnsupportedPrices(string text)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Money.ToMinor(decimal.Parse(text, System.Globalization.CultureInfo.InvariantCulture)));
    }
}
