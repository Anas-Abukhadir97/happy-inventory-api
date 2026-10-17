using HappyInventory.Domain.Entities;
using HappyInventory.Domain.ValueObjects;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace HappyInventory.Infrastructure.Data;

public static class DatabaseInitializer
{
    public static async Task InitializeAsync(
        InventoryDbContext database,
        IConfiguration configuration,
        bool isDevelopment,
        CancellationToken cancellationToken = default)
    {
        if (database.Database.GetConnectionString() is { } connectionString)
        {
            var source = new SqliteConnectionStringBuilder(connectionString).DataSource;
            if (source != ":memory:" && !string.IsNullOrWhiteSpace(source))
            {
                var directory = Path.GetDirectoryName(Path.GetFullPath(source));
                if (directory is not null) Directory.CreateDirectory(directory);
            }
        }

        await database.Database.MigrateAsync(cancellationToken);
        if (await database.Users.AnyAsync(cancellationToken)) return;

        var email = configuration["BootstrapAdmin:Email"] ?? "admin@happywarehouse.com";
        var password = configuration["BootstrapAdmin:Password"];
        if (string.IsNullOrWhiteSpace(password) && isDevelopment) password = "P@ssw0rd";
        if (string.IsNullOrWhiteSpace(password))
        {
            throw new InvalidOperationException("BootstrapAdmin:Password must be configured before the first production start.");
        }

        var now = DateTime.UtcNow;
        var user = new User
        {
            Email = email.Trim(),
            NormalizedEmail = NormalizedText.Key(email),
            FullName = "System Administrator",
            PasswordHash = string.Empty,
            RoleId = 1,
            IsActive = true,
            IsSystemAdmin = true,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        user.PasswordHash = new PasswordHasher<User>().HashPassword(user, password);
        database.Users.Add(user);
        await database.SaveChangesAsync(cancellationToken);
    }
}
