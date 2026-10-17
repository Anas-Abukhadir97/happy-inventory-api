using HappyInventory.Domain.Entities;
using HappyInventory.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace HappyInventory.Infrastructure.Data;

public sealed class InventoryDbContext(DbContextOptions<InventoryDbContext> options) : DbContext(options)
{
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<Country> Countries => Set<Country>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Warehouse> Warehouses => Set<Warehouse>();
    public DbSet<Item> Items => Set<Item>();
    public DbSet<RefreshSession> RefreshSessions => Set<RefreshSession>();
    public DbSet<InventoryDeduction> InventoryDeductions => Set<InventoryDeduction>();
    public DbSet<SupplierOrderRequest> SupplierOrderRequests => Set<SupplierOrderRequest>();
    public DbSet<SupplierOutboxMessage> SupplierOutboxMessages => Set<SupplierOutboxMessage>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<Role>(entity =>
        {
            entity.ToTable("Roles");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(30).IsRequired();
            entity.HasIndex(x => x.Name).IsUnique();
            entity.HasData(
                new Role { Id = 1, Name = "Admin" },
                new Role { Id = 2, Name = "Management" },
                new Role { Id = 3, Name = "Auditor" });
        });

        model.Entity<Country>(entity =>
        {
            entity.ToTable("Countries");
            entity.HasKey(x => x.Code);
            entity.Property(x => x.Code).HasMaxLength(2).IsRequired();
            entity.Property(x => x.NameKey).HasMaxLength(50).IsRequired();
            entity.HasData(
                new Country { Code = "JO", NameKey = "country.jo" },
                new Country { Code = "US", NameKey = "country.us" },
                new Country { Code = "GB", NameKey = "country.gb" },
                new Country { Code = "AE", NameKey = "country.ae" },
                new Country { Code = "SA", NameKey = "country.sa" });
        });

        model.Entity<User>(entity =>
        {
            entity.ToTable("Users", table =>
            {
                table.HasCheckConstraint("CK_Users_Email", "length(trim(\"Email\")) > 0 AND length(\"Email\") <= 254");
                table.HasCheckConstraint("CK_Users_Name", "length(trim(\"FullName\")) > 0 AND length(\"FullName\") <= 200");
                table.HasCheckConstraint("CK_Users_Failures", "\"FailedLoginCount\" >= 0 AND \"SecurityVersion\" >= 0");
            });
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Email).HasMaxLength(254).IsRequired();
            entity.Property(x => x.NormalizedEmail).HasMaxLength(254).IsRequired();
            entity.Property(x => x.FullName).HasMaxLength(200).IsRequired();
            entity.Property(x => x.PasswordHash).HasMaxLength(500).IsRequired();
            entity.Property(x => x.Version).IsConcurrencyToken();
            entity.HasIndex(x => x.NormalizedEmail).IsUnique().HasFilter("\"DeletedAtUtc\" IS NULL");
            entity.HasIndex(x => x.IsSystemAdmin).IsUnique().HasFilter("\"IsSystemAdmin\" = 1");
            entity.HasIndex(x => x.RoleId);
            entity.HasOne(x => x.Role).WithMany().HasForeignKey(x => x.RoleId).OnDelete(DeleteBehavior.Restrict);
            entity.HasQueryFilter(x => x.DeletedAtUtc == null);
        });

        model.Entity<Warehouse>(entity =>
        {
            entity.ToTable("Warehouses", table =>
            {
                table.HasCheckConstraint("CK_Warehouses_Name", "length(trim(\"Name\")) > 0 AND length(\"Name\") <= 200");
                table.HasCheckConstraint("CK_Warehouses_Address", "length(trim(\"Address\")) > 0 AND length(\"Address\") <= 1000");
                table.HasCheckConstraint("CK_Warehouses_City", "length(trim(\"City\")) > 0 AND length(\"City\") <= 100");
            });
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
            entity.Property(x => x.NormalizedName).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Address).HasMaxLength(1000).IsRequired();
            entity.Property(x => x.City).HasMaxLength(100).IsRequired();
            entity.Property(x => x.CountryCode).HasMaxLength(2).IsRequired();
            entity.Property(x => x.Version).IsConcurrencyToken();
            entity.HasIndex(x => x.NormalizedName).IsUnique().HasFilter("\"DeletedAtUtc\" IS NULL");
            entity.HasIndex(x => x.CountryCode);
            entity.HasOne(x => x.Country).WithMany().HasForeignKey(x => x.CountryCode).OnDelete(DeleteBehavior.Restrict);
            entity.HasQueryFilter(x => x.DeletedAtUtc == null);
        });

        model.Entity<Item>(entity =>
        {
            entity.ToTable("Items", table =>
            {
                table.HasCheckConstraint("CK_Items_Name", "length(trim(\"Name\")) > 0 AND length(\"Name\") <= 200");
                table.HasCheckConstraint("CK_Items_Qty", "typeof(\"Qty\") = 'integer' AND \"Qty\" >= 1");
                table.HasCheckConstraint("CK_Items_Cost", $"typeof(\"CostMinor\") = 'integer' AND \"CostMinor\" BETWEEN 0 AND {Money.MaxMinor}");
                table.HasCheckConstraint("CK_Items_Msrp", $"\"MsrpMinor\" IS NULL OR (typeof(\"MsrpMinor\") = 'integer' AND \"MsrpMinor\" BETWEEN 0 AND {Money.MaxMinor})");
            });
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
            entity.Property(x => x.NormalizedName).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Sku).HasMaxLength(100);
            entity.Property(x => x.Version).IsConcurrencyToken();
            entity.HasIndex(x => x.NormalizedName).IsUnique().HasFilter("\"DeletedAtUtc\" IS NULL");
            entity.HasIndex(x => new { x.WarehouseId, x.Id });
            entity.HasIndex(x => new { x.Qty, x.Id });
            entity.HasOne(x => x.Warehouse).WithMany().HasForeignKey(x => x.WarehouseId).OnDelete(DeleteBehavior.Restrict);
            entity.HasQueryFilter(x => x.DeletedAtUtc == null);
        });

        model.Entity<RefreshSession>(entity =>
        {
            entity.ToTable("RefreshSessions");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.TokenHash).HasMaxLength(64).IsRequired();
            entity.HasIndex(x => x.TokenHash).IsUnique();
            entity.HasIndex(x => x.FamilyId);
            entity.HasIndex(x => x.UserId);
            entity.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        });

        model.Entity<InventoryDeduction>(entity =>
        {
            entity.ToTable("InventoryDeductions", table =>
                table.HasCheckConstraint("CK_Deductions_Qty", "\"Quantity\" >= 1 AND \"ResultQuantity\" >= 1"));
            entity.HasKey(x => x.Id);
            entity.Property(x => x.IdempotencyKey).HasMaxLength(128).IsRequired();
            entity.Property(x => x.RequestFingerprint).HasMaxLength(64).IsRequired();
            entity.Property(x => x.ExternalReference).HasMaxLength(200).IsRequired();
            entity.HasIndex(x => new { x.CallerUserId, x.IdempotencyKey }).IsUnique();
            entity.HasIndex(x => new { x.WarehouseId, x.ItemId });
            entity.HasOne(x => x.CallerUser).WithMany().HasForeignKey(x => x.CallerUserId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.Warehouse).WithMany().HasForeignKey(x => x.WarehouseId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.Item).WithMany().HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.Restrict);
        });

        model.Entity<SupplierOrderRequest>(entity =>
        {
            entity.ToTable("SupplierOrderRequests", table =>
                table.HasCheckConstraint("CK_SupplierOrders_Qty", "\"RequestedQuantity\" >= 1"));
            entity.HasKey(x => x.Id);
            entity.Property(x => x.SupplierDestination).HasMaxLength(254).IsRequired();
            entity.HasOne(x => x.Warehouse).WithMany().HasForeignKey(x => x.WarehouseId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.Item).WithMany().HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.RequestedByUser).WithMany().HasForeignKey(x => x.RequestedByUserId).OnDelete(DeleteBehavior.Restrict);
        });

        model.Entity<SupplierOutboxMessage>(entity =>
        {
            entity.ToTable("SupplierOutboxMessages", table =>
            {
                table.HasCheckConstraint("CK_Outbox_Status", "\"Status\" IN ('Pending', 'Sent', 'Failed')");
                table.HasCheckConstraint("CK_Outbox_Attempts", "\"AttemptCount\" >= 0");
            });
            entity.HasKey(x => x.Id);
            entity.Property(x => x.PayloadJson).IsRequired();
            entity.Property(x => x.Status).HasMaxLength(10).IsRequired();
            entity.HasIndex(x => x.OrderRequestId).IsUnique();
            entity.HasIndex(x => new { x.Status, x.NextAttemptAtUtc });
            entity.HasOne(x => x.OrderRequest).WithMany().HasForeignKey(x => x.OrderRequestId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
