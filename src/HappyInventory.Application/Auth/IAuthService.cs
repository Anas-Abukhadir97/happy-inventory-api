namespace HappyInventory.Application.Auth;

public interface IAuthService
{
    Task<AuthResult> LoginAsync(string email, string password, bool browserSession, CancellationToken cancellationToken);
    Task<AuthResult> RefreshAsync(string refreshToken, CancellationToken cancellationToken);
    Task LogoutAsync(string refreshToken, CancellationToken cancellationToken);
    Task<CurrentUser?> ValidateAccessAsync(int userId, int securityVersion, Guid? familyId, CancellationToken cancellationToken);
}

public sealed record CurrentUser(int Id, string Email, string FullName, string Role);

public sealed record AuthResult(string AccessToken, DateTime ExpiresAtUtc, CurrentUser User, string? RefreshToken);

public sealed class AuthFailure(string code, string resourceKey, int statusCode) : Exception(code)
{
    public string Code { get; } = code;
    public string ResourceKey { get; } = resourceKey;
    public int StatusCode { get; } = statusCode;
}
