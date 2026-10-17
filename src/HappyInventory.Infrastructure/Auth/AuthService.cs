using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using HappyInventory.Application.Auth;
using HappyInventory.Domain.Entities;
using HappyInventory.Domain.ValueObjects;
using HappyInventory.Infrastructure.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace HappyInventory.Infrastructure.Auth;

public sealed class AuthService(InventoryDbContext database, AuthSettings settings) : IAuthService
{
    private static readonly TimeSpan AccessLifetime = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan RefreshIdleLifetime = TimeSpan.FromDays(7);
    private static readonly TimeSpan RefreshAbsoluteLifetime = TimeSpan.FromDays(30);
    private static readonly PasswordHasher<User> Hasher = new();
    private static readonly User DummyUser = new()
    {
        Email = "unused@example.invalid",
        NormalizedEmail = "UNUSED@EXAMPLE.INVALID",
        FullName = "Unused",
        PasswordHash = "",
        RoleId = 1
    };
    private static readonly string DummyHash = Hasher.HashPassword(DummyUser, "unused-password");

    public async Task<AuthResult> LoginAsync(string email, string password, bool browserSession, CancellationToken cancellationToken)
    {
        var normalized = NormalizedText.Key(email);
        var user = await database.Users.Include(x => x.Role)
            .SingleOrDefaultAsync(x => x.NormalizedEmail == normalized, cancellationToken);
        if (user is null)
        {
            _ = Hasher.VerifyHashedPassword(DummyUser, DummyHash, password);
            throw new AuthFailure("invalid_credentials", "InvalidCredentials", 401);
        }

        var now = DateTime.UtcNow;
        if (user.LockoutUntilUtc > now)
            throw new AuthFailure("invalid_credentials", "InvalidCredentials", 401);

        var verification = Hasher.VerifyHashedPassword(user, user.PasswordHash, password);
        if (verification == PasswordVerificationResult.Failed)
        {
            user.FailedLoginCount++;
            if (user.FailedLoginCount >= 5)
            {
                user.FailedLoginCount = 0;
                user.LockoutUntilUtc = now.AddMinutes(15);
            }
            await database.SaveChangesAsync(cancellationToken);
            throw new AuthFailure("invalid_credentials", "InvalidCredentials", 401);
        }

        if (!user.IsActive)
            throw new AuthFailure("account_disabled", "AccountDisabled", 403);

        user.FailedLoginCount = 0;
        user.LockoutUntilUtc = null;
        if (verification == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.PasswordHash = Hasher.HashPassword(user, password);
            await database.SaveChangesAsync(cancellationToken);
            await database.Entry(user).ReloadAsync(cancellationToken);
        }

        string? refreshToken = null;
        Guid? familyId = null;
        if (browserSession)
        {
            refreshToken = NewToken();
            familyId = Guid.NewGuid();
            database.RefreshSessions.Add(new RefreshSession
            {
                FamilyId = familyId.Value,
                UserId = user.Id,
                SecurityVersion = user.SecurityVersion,
                TokenHash = Hash(refreshToken),
                CreatedAtUtc = now,
                ExpiresAtUtc = now.Add(RefreshIdleLifetime),
                AbsoluteExpiresAtUtc = now.Add(RefreshAbsoluteLifetime)
            });
        }
        await database.SaveChangesAsync(cancellationToken);
        return Issue(user, familyId, refreshToken, now);
    }

    public async Task<AuthResult> RefreshAsync(string refreshToken, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var hash = Hash(refreshToken);
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        var prior = await database.RefreshSessions.AsNoTracking()
            .SingleOrDefaultAsync(x => x.TokenHash == hash, cancellationToken);
        if (prior is null || prior.RevokedAtUtc is not null || prior.AbsoluteExpiresAtUtc <= now)
            throw new AuthFailure("invalid_session", "InvalidSession", 401);

        if (prior.ConsumedAtUtc is not null)
        {
            await RevokeFamilyAsync(prior.FamilyId, now, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            throw new AuthFailure("session_reused", "InvalidSession", 401);
        }

        if (prior.ExpiresAtUtc <= now)
            throw new AuthFailure("invalid_session", "InvalidSession", 401);

        var user = await database.Users.Include(x => x.Role)
            .SingleOrDefaultAsync(x => x.Id == prior.UserId, cancellationToken);
        if (user is null || !user.IsActive || user.SecurityVersion != prior.SecurityVersion)
        {
            await RevokeFamilyAsync(prior.FamilyId, now, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            throw new AuthFailure("invalid_session", "InvalidSession", 401);
        }

        var consumed = await database.RefreshSessions
            .Where(x => x.Id == prior.Id && x.ConsumedAtUtc == null && x.RevokedAtUtc == null)
            .ExecuteUpdateAsync(update => update.SetProperty(x => x.ConsumedAtUtc, now), cancellationToken);
        if (consumed != 1)
        {
            await RevokeFamilyAsync(prior.FamilyId, now, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            throw new AuthFailure("session_reused", "InvalidSession", 401);
        }

        var nextToken = NewToken();
        var next = new RefreshSession
        {
            FamilyId = prior.FamilyId,
            UserId = prior.UserId,
            SecurityVersion = prior.SecurityVersion,
            TokenHash = Hash(nextToken),
            CreatedAtUtc = now,
            ExpiresAtUtc = Min(now.Add(RefreshIdleLifetime), prior.AbsoluteExpiresAtUtc),
            AbsoluteExpiresAtUtc = prior.AbsoluteExpiresAtUtc
        };
        database.RefreshSessions.Add(next);
        await database.SaveChangesAsync(cancellationToken);
        await database.RefreshSessions.Where(x => x.Id == prior.Id)
            .ExecuteUpdateAsync(update => update.SetProperty(x => x.ReplacementId, next.Id), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Issue(user, prior.FamilyId, nextToken, now);
    }

    public async Task LogoutAsync(string refreshToken, CancellationToken cancellationToken)
    {
        var hash = Hash(refreshToken);
        var family = await database.RefreshSessions.AsNoTracking()
            .Where(x => x.TokenHash == hash).Select(x => (Guid?)x.FamilyId)
            .SingleOrDefaultAsync(cancellationToken);
        if (family is not null)
            await RevokeFamilyAsync(family.Value, DateTime.UtcNow, cancellationToken);
    }

    public async Task<CurrentUser?> ValidateAccessAsync(int userId, int securityVersion, Guid? familyId, CancellationToken cancellationToken)
    {
        var user = await database.Users.AsNoTracking().Include(x => x.Role)
            .SingleOrDefaultAsync(x => x.Id == userId, cancellationToken);
        if (user is null || !user.IsActive || user.SecurityVersion != securityVersion)
            return null;

        if (familyId is not null && !await database.RefreshSessions.AsNoTracking()
            .AnyAsync(x => x.FamilyId == familyId && x.UserId == userId && x.RevokedAtUtc == null &&
                x.ConsumedAtUtc == null && x.ExpiresAtUtc > DateTime.UtcNow && x.AbsoluteExpiresAtUtc > DateTime.UtcNow,
                cancellationToken))
            return null;
        return new CurrentUser(user.Id, user.Email, user.FullName, user.Role.Name);
    }

    private AuthResult Issue(User user, Guid? familyId, string? refreshToken, DateTime now)
    {
        var expires = now.Add(AccessLifetime);
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
            new("role", user.Role.Name),
            new("sv", user.SecurityVersion.ToString(System.Globalization.CultureInfo.InvariantCulture))
        };
        if (familyId is not null) claims.Add(new Claim("fid", familyId.Value.ToString("D")));
        var jwt = new JwtSecurityToken(settings.Issuer, settings.Audience, claims,
            notBefore: now, expires: expires,
            signingCredentials: new SigningCredentials(new SymmetricSecurityKey(settings.SigningKey), SecurityAlgorithms.HmacSha256));
        return new AuthResult(new JwtSecurityTokenHandler().WriteToken(jwt), expires,
            new CurrentUser(user.Id, user.Email, user.FullName, user.Role.Name), refreshToken);
    }

    private Task<int> RevokeFamilyAsync(Guid familyId, DateTime now, CancellationToken cancellationToken) =>
        database.RefreshSessions.Where(x => x.FamilyId == familyId && x.RevokedAtUtc == null)
            .ExecuteUpdateAsync(update => update.SetProperty(x => x.RevokedAtUtc, now), cancellationToken);

    private static string NewToken() => Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(48));

    private static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private static DateTime Min(DateTime first, DateTime second) => first <= second ? first : second;
}
