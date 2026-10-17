using System.Globalization;
using System.Net.Mail;
using HappyInventory.Application.Inventory;
using HappyInventory.Application.Users;
using HappyInventory.Domain.Entities;
using HappyInventory.Domain.ValueObjects;
using HappyInventory.Infrastructure.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace HappyInventory.Infrastructure.Users;

public sealed class UserService(InventoryDbContext db) : IUserService
{
    private readonly PasswordHasher<User> _hasher = new();
    private static BusinessFailure Invalid() => new("invalid_request", "InvalidRequest", 400);
    private static BusinessFailure Missing() => new("not_found", "NotFound", 404);
    private static BusinessFailure Conflict() => new("conflict", "Conflict", 409);
    private static BusinessFailure Stale() => new("stale_version", "StaleVersion", 412);

    public async Task<IReadOnlyList<RoleView>> RolesAsync(CancellationToken ct) =>
        await db.Roles.AsNoTracking().OrderBy(x => x.Id).Select(x => new RoleView(x.Id, x.Name)).ToListAsync(ct);

    public async Task<PagedResult<UserView>> UsersAsync(int page, int pageSize, string? search, CancellationToken ct)
    {
        if (page < 1 || pageSize is < 1 or > 10 || search?.Length > 200) throw Invalid();
        var query = db.Users.AsNoTracking().Include(x => x.Role).AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(x => x.Email.Contains(term) || x.FullName.Contains(term));
        }
        var count = await query.LongCountAsync(ct);
        var rows = await query.OrderBy(x => x.Id).Skip((int)Math.Min((long)(page - 1) * pageSize, int.MaxValue))
            .Take(pageSize).ToListAsync(ct);
        return new PagedResult<UserView>(rows.Select(Map).ToList(), page, pageSize,
            count.ToString(CultureInfo.InvariantCulture), ((count + pageSize - 1) / pageSize).ToString(CultureInfo.InvariantCulture));
    }

    public async Task<UserView?> UserAsync(int id, CancellationToken ct) =>
        await db.Users.AsNoTracking().Include(x => x.Role).FirstOrDefaultAsync(x => x.Id == id, ct) is { } row
            ? Map(row) : null;

    public async Task<UserView> CreateAsync(UserCreate write, CancellationToken ct)
    {
        var (email, key, name, roleId) = Validate(write.Email, write.FullName, write.RoleId);
        ValidatePassword(write.InitialPassword);
        if (await db.Users.AnyAsync(x => x.NormalizedEmail == key, ct)) throw Conflict();
        var now = DateTime.UtcNow;
        var row = new User
        {
            Email = email,
            NormalizedEmail = key,
            FullName = name,
            PasswordHash = "",
            RoleId = roleId,
            IsActive = write.Active,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        row.PasswordHash = _hasher.HashPassword(row, write.InitialPassword!);
        db.Users.Add(row);
        await SaveAsync(ct);
        await db.Entry(row).Reference(x => x.Role).LoadAsync(ct);
        return Map(row);
    }

    public async Task<UserView> UpdateAsync(int id, Guid version, UserUpdate write, CancellationToken ct)
    {
        var (email, key, name, roleId) = Validate(write.Email, write.FullName, write.RoleId);
        var row = await db.Users.Include(x => x.Role).FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw Missing();
        if (row.Version != version) throw Stale();
        if (row.IsSystemAdmin && (!write.Active || roleId != 1)) throw Conflict();
        if (await db.Users.AnyAsync(x => x.Id != id && x.NormalizedEmail == key, ct)) throw Conflict();
        row.Email = email;
        row.NormalizedEmail = key;
        row.FullName = name;
        row.RoleId = roleId;
        row.IsActive = write.Active;
        row.Version = Guid.NewGuid();
        row.UpdatedAtUtc = DateTime.UtcNow;
        await SaveAsync(ct);
        await db.Entry(row).Reference(x => x.Role).LoadAsync(ct);
        return Map(row);
    }

    public async Task DeleteAsync(int id, Guid version, int actorId, CancellationToken ct)
    {
        var row = await db.Users.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw Missing();
        if (row.Version != version) throw Stale();
        if (row.IsSystemAdmin || row.Id == actorId) throw Conflict();
        row.DeletedAtUtc = DateTime.UtcNow;
        row.UpdatedAtUtc = row.DeletedAtUtc.Value;
        row.Version = Guid.NewGuid();
        await SaveAsync(ct);
    }

    public async Task ResetPasswordAsync(int id, Guid version, PasswordReset write, CancellationToken ct)
    {
        ValidatePassword(write.NewPassword);
        var row = await db.Users.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw Missing();
        if (row.Version != version) throw Stale();
        row.PasswordHash = _hasher.HashPassword(row, write.NewPassword!);
        row.Version = Guid.NewGuid();
        row.UpdatedAtUtc = DateTime.UtcNow;
        await SaveAsync(ct);
    }

    private (string Email, string Key, string Name, int RoleId) Validate(string? email, string? fullName, int roleId)
    {
        var trimmedEmail = email?.Trim();
        var name = fullName?.Trim();
        if (string.IsNullOrWhiteSpace(trimmedEmail) || trimmedEmail.Length > 254 ||
            string.IsNullOrWhiteSpace(name) || name.Length > 200 ||
            !MailAddress.TryCreate(trimmedEmail, out var parsed) || parsed.Address != trimmedEmail ||
            !db.Roles.Any(x => x.Id == roleId)) throw Invalid();
        return (trimmedEmail, NormalizedText.Key(trimmedEmail), name, roleId);
    }

    private static void ValidatePassword(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length is < 8 or > 128) throw Invalid();
    }

    private static UserView Map(User x) => new(x.Id, x.Email, x.FullName, x.Role.Name,
        x.IsActive, x.IsSystemAdmin, x.Version);

    private async Task SaveAsync(CancellationToken ct)
    {
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { throw Stale(); }
        catch (DbUpdateException ex) when (ex.InnerException is SqliteException { SqliteErrorCode: 19 }) { throw Conflict(); }
    }
}
