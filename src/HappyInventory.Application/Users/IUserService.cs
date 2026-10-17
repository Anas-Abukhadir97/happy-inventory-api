namespace HappyInventory.Application.Users;

using HappyInventory.Application.Inventory;

public sealed record RoleView(int Id, string Name);
public sealed record UserView(int Id, string Email, string FullName, string Role, bool Active,
    bool IsSystemAdmin, Guid Version);
public sealed record UserCreate(string? Email, string? FullName, int RoleId, bool Active, string? InitialPassword);
public sealed record UserUpdate(string? Email, string? FullName, int RoleId, bool Active);
public sealed record PasswordReset(string? NewPassword);

public interface IUserService
{
    Task<IReadOnlyList<RoleView>> RolesAsync(CancellationToken cancellationToken);
    Task<PagedResult<UserView>> UsersAsync(int page, int pageSize, string? search, CancellationToken cancellationToken);
    Task<UserView?> UserAsync(int id, CancellationToken cancellationToken);
    Task<UserView> CreateAsync(UserCreate write, CancellationToken cancellationToken);
    Task<UserView> UpdateAsync(int id, Guid version, UserUpdate write, CancellationToken cancellationToken);
    Task DeleteAsync(int id, Guid version, int actorId, CancellationToken cancellationToken);
    Task ResetPasswordAsync(int id, Guid version, PasswordReset write, CancellationToken cancellationToken);
}
