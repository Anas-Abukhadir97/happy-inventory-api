using System.Globalization;
using System.Text.RegularExpressions;
using HappyInventory.Application.Inventory;
using HappyInventory.Domain.Entities;
using HappyInventory.Domain.ValueObjects;
using HappyInventory.Infrastructure.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace HappyInventory.Infrastructure.Inventory;

public sealed class InventoryService(InventoryDbContext db) : IInventoryService
{
    private static readonly Regex PricePattern = new("^(0|[1-9][0-9]{0,8})\\.[0-9]{2}$", RegexOptions.Compiled);
    private static BusinessFailure Invalid() => new("invalid_request", "InvalidRequest", 400);
    private static BusinessFailure Missing() => new("not_found", "NotFound", 404);
    private static BusinessFailure Conflict() => new("conflict", "Conflict", 409);
    private static BusinessFailure Stale() => new("stale_version", "StaleVersion", 412);

    public async Task<IReadOnlyList<CountryView>> CountriesAsync(CancellationToken ct) =>
        await db.Countries.AsNoTracking().OrderBy(x => x.Code)
            .Select(x => new CountryView(x.Code, x.NameKey)).ToListAsync(ct);

    public async Task<PagedResult<WarehouseView>> WarehousesAsync(int page, int pageSize, string? search, CancellationToken ct)
    {
        ValidatePage(page, pageSize, search);
        var query = db.Warehouses.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(x => x.Name.Contains(term) || x.City.Contains(term));
        }
        var count = await query.LongCountAsync(ct);
        var rows = await query.OrderBy(x => x.Id).Skip(Offset(page, pageSize)).Take(pageSize).ToListAsync(ct);
        return Paged(rows.Select(Map).ToList(), page, pageSize, count);
    }

    public async Task<WarehouseView?> WarehouseAsync(int id, CancellationToken ct) =>
        await db.Warehouses.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct) is { } row ? Map(row) : null;

    public async Task<WarehouseView> CreateWarehouseAsync(WarehouseWrite write, CancellationToken ct)
    {
        var (name, normalized, address, city, country) = ValidateWarehouse(write);
        if (!await db.Countries.AnyAsync(x => x.Code == country, ct)) throw Invalid();
        if (await db.Warehouses.AnyAsync(x => x.NormalizedName == normalized, ct)) throw Conflict();
        var now = DateTime.UtcNow;
        var row = new Warehouse
        {
            Name = name,
            NormalizedName = normalized,
            Address = address,
            City = city,
            CountryCode = country,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        db.Warehouses.Add(row);
        await SaveAsync(ct);
        return Map(row);
    }

    public async Task<WarehouseView> UpdateWarehouseAsync(int id, Guid version, WarehouseWrite write, CancellationToken ct)
    {
        var (name, normalized, address, city, country) = ValidateWarehouse(write);
        var row = await db.Warehouses.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw Missing();
        if (row.Version != version) throw Stale();
        if (!await db.Countries.AnyAsync(x => x.Code == country, ct)) throw Invalid();
        if (await db.Warehouses.AnyAsync(x => x.Id != id && x.NormalizedName == normalized, ct)) throw Conflict();
        row.Name = name;
        row.NormalizedName = normalized;
        row.Address = address;
        row.City = city;
        row.CountryCode = country;
        row.Version = Guid.NewGuid();
        row.UpdatedAtUtc = DateTime.UtcNow;
        await SaveAsync(ct);
        return Map(row);
    }

    public async Task DeleteWarehouseAsync(int id, Guid version, CancellationToken ct)
    {
        var row = await db.Warehouses.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw Missing();
        if (row.Version != version) throw Stale();
        if (await db.Items.AnyAsync(x => x.WarehouseId == id, ct)) throw Conflict();
        row.DeletedAtUtc = DateTime.UtcNow;
        row.UpdatedAtUtc = row.DeletedAtUtc.Value;
        row.Version = Guid.NewGuid();
        await SaveAsync(ct);
    }

    public async Task<PagedResult<ItemView>> ItemsAsync(int warehouseId, int page, int pageSize, string? search, CancellationToken ct)
    {
        ValidatePage(page, pageSize, search);
        if (!await db.Warehouses.AnyAsync(x => x.Id == warehouseId, ct)) throw Missing();
        var query = db.Items.AsNoTracking().Where(x => x.WarehouseId == warehouseId);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(x => x.Name.Contains(term) || (x.Sku != null && x.Sku.Contains(term)));
        }
        var count = await query.LongCountAsync(ct);
        var rows = await query.OrderBy(x => x.Id).Skip(Offset(page, pageSize)).Take(pageSize).ToListAsync(ct);
        return Paged(rows.Select(Map).ToList(), page, pageSize, count);
    }

    public async Task<ItemView?> ItemAsync(int warehouseId, int itemId, CancellationToken ct)
    {
        if (!await db.Warehouses.AnyAsync(x => x.Id == warehouseId, ct)) return null;
        return await db.Items.AsNoTracking().FirstOrDefaultAsync(x => x.Id == itemId && x.WarehouseId == warehouseId, ct)
            is { } row ? Map(row) : null;
    }

    public async Task<ItemView> CreateItemAsync(int warehouseId, ItemWrite write, CancellationToken ct)
    {
        var (name, normalized, sku, qty, cost, msrp) = ValidateItem(write);
        if (!await db.Warehouses.AnyAsync(x => x.Id == warehouseId, ct)) throw Missing();
        if (await db.Items.AnyAsync(x => x.NormalizedName == normalized, ct)) throw Conflict();
        var now = DateTime.UtcNow;
        var row = new Item
        {
            WarehouseId = warehouseId,
            Name = name,
            NormalizedName = normalized,
            Sku = sku,
            Qty = qty,
            CostMinor = cost,
            MsrpMinor = msrp,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        db.Items.Add(row);
        await SaveAsync(ct);
        return Map(row);
    }

    public async Task<ItemView> UpdateItemAsync(int warehouseId, int itemId, Guid version, ItemWrite write, CancellationToken ct)
    {
        var (name, normalized, sku, qty, cost, msrp) = ValidateItem(write);
        var row = await db.Items.FirstOrDefaultAsync(x => x.Id == itemId && x.WarehouseId == warehouseId, ct)
            ?? throw Missing();
        if (row.Version != version) throw Stale();
        if (await db.Items.AnyAsync(x => x.Id != itemId && x.NormalizedName == normalized, ct)) throw Conflict();
        row.Name = name;
        row.NormalizedName = normalized;
        row.Sku = sku;
        row.Qty = qty;
        row.CostMinor = cost;
        row.MsrpMinor = msrp;
        row.Version = Guid.NewGuid();
        row.UpdatedAtUtc = DateTime.UtcNow;
        await SaveAsync(ct);
        return Map(row);
    }

    public async Task DeleteItemAsync(int warehouseId, int itemId, Guid version, CancellationToken ct)
    {
        var row = await db.Items.FirstOrDefaultAsync(x => x.Id == itemId && x.WarehouseId == warehouseId, ct)
            ?? throw Missing();
        if (row.Version != version) throw Stale();
        row.DeletedAtUtc = DateTime.UtcNow;
        row.UpdatedAtUtc = row.DeletedAtUtc.Value;
        row.Version = Guid.NewGuid();
        await SaveAsync(ct);
    }

    public async Task<PagedResult<WarehouseStatus>> WarehouseStatusesAsync(int page, int pageSize, CancellationToken ct)
    {
        ValidatePage(page, pageSize, null);
        var count = await db.Warehouses.LongCountAsync(ct);
        var rows = await db.Warehouses.AsNoTracking().OrderBy(x => x.Id)
            .Skip(Offset(page, pageSize)).Take(pageSize).Select(x => new { x.Id, x.Name }).ToListAsync(ct);
        var ids = rows.Select(x => x.Id).ToList();
        var quantities = await db.Items.AsNoTracking().Where(x => ids.Contains(x.WarehouseId))
            .GroupBy(x => x.WarehouseId).Select(x => new { WarehouseId = x.Key, Count = x.LongCount(), Total = x.Sum(i => (long)i.Qty) })
            .ToDictionaryAsync(x => x.WarehouseId, ct);
        var items = rows.Select(x => new WarehouseStatus(x.Id, x.Name,
            (quantities.TryGetValue(x.Id, out var q) ? q.Count : 0).ToString(CultureInfo.InvariantCulture),
            (quantities.TryGetValue(x.Id, out q) ? q.Total : 0).ToString(CultureInfo.InvariantCulture))).ToList();
        return Paged(items, page, pageSize, count);
    }

    public async Task<IReadOnlyList<ItemQuantity>> QuantityItemsAsync(bool highest, CancellationToken ct)
    {
        var query = db.Items.AsNoTracking().Join(db.Warehouses.AsNoTracking(),
            item => item.WarehouseId, warehouse => warehouse.Id,
            (item, warehouse) => new { item, warehouse });
        query = highest ? query.OrderByDescending(x => x.item.Qty).ThenBy(x => x.item.Id)
            : query.OrderBy(x => x.item.Qty).ThenBy(x => x.item.Id);
        return await query.Take(10).Select(x => new ItemQuantity(x.item.Id, x.item.Name,
            x.warehouse.Id, x.warehouse.Name, x.item.Qty)).ToListAsync(ct);
    }

    private static (string Name, string Normalized, string Address, string City, string Country) ValidateWarehouse(WarehouseWrite write)
    {
        if (write is null) throw Invalid();
        var name = Required(write.Name, 200);
        var address = Required(write.Address, 1000);
        var city = Required(write.City, 100);
        var country = Required(write.CountryCode, 2).ToUpperInvariant();
        if (country.Length != 2) throw Invalid();
        return (name, NormalizedText.Key(name), address, city, country);
    }

    private static (string Name, string Normalized, string? Sku, int Qty, long Cost, long? Msrp) ValidateItem(ItemWrite write)
    {
        if (write is null) throw Invalid();
        var name = Required(write.Name, 200);
        var sku = write.Sku?.Trim();
        if (sku?.Length > 100) throw Invalid();
        if (sku?.Length == 0) sku = null;
        if (write.Qty < 1) throw Invalid();
        var cost = ParsePrice(write.CostPrice);
        var msrp = write.MsrpPrice is null ? (long?)null : ParsePrice(write.MsrpPrice);
        return (name, NormalizedText.Key(name), sku, write.Qty, cost, msrp);
    }

    private static string Required(string? value, int max)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed) || trimmed.Length > max) throw Invalid();
        return trimmed;
    }

    private static long ParsePrice(string? value)
    {
        if (value is null || !PricePattern.IsMatch(value) ||
            !decimal.TryParse(value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var amount))
            throw Invalid();
        return Money.ToMinor(amount);
    }

    private static void ValidatePage(int page, int size, string? search)
    {
        if (page < 1 || size is < 1 or > 10 || search?.Length > 200) throw Invalid();
    }

    private static int Offset(int page, int size) => (int)Math.Min((long)(page - 1) * size, int.MaxValue);
    private static PagedResult<T> Paged<T>(IReadOnlyList<T> rows, int page, int size, long count) =>
        new(rows, page, size, count.ToString(CultureInfo.InvariantCulture),
            ((count + size - 1) / size).ToString(CultureInfo.InvariantCulture));

    private static WarehouseView Map(Warehouse x) => new(x.Id, x.Name, x.Address, x.City,
        x.CountryCode, x.Version, x.CreatedAtUtc, x.UpdatedAtUtc);
    private static ItemView Map(Item x) => new(x.Id, x.WarehouseId, x.Name, x.Sku, x.Qty,
        Money.FromMinor(x.CostMinor).ToString("F2", CultureInfo.InvariantCulture),
        x.MsrpMinor is null ? null : Money.FromMinor(x.MsrpMinor.Value).ToString("F2", CultureInfo.InvariantCulture),
        x.Version, x.CreatedAtUtc, x.UpdatedAtUtc);

    private async Task SaveAsync(CancellationToken ct)
    {
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { throw Stale(); }
        catch (DbUpdateException ex) when (ex.InnerException is SqliteException { SqliteErrorCode: 19 }) { throw Conflict(); }
    }
}
