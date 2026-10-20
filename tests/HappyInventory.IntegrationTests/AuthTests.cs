using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HappyInventory.Domain.Entities;
using HappyInventory.Domain.ValueObjects;
using HappyInventory.Infrastructure.Auth;
using HappyInventory.Infrastructure.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace HappyInventory.IntegrationTests;

public sealed class AuthTests
{
    private const string Origin = "https://localhost:5173";
    private const string Email = "admin@happywarehouse.com";
    private const string Password = "test-only-bootstrap-password";

    private static HttpClient Client(SqliteAppFactory factory) => factory.CreateClient(
        new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), HandleCookies = false });

    private static async Task<(string Cookie, string Token)> CsrfAsync(HttpClient client)
    {
        using var response = await client.GetAsync("/api/v1/auth/csrf");
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var cookie = response.Headers.GetValues("Set-Cookie").Single();
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);
        return (cookie.Split(';')[0], response.Headers.GetValues("X-CSRF-Token").Single());
    }

    private static HttpRequestMessage BrowserPost(string path, string csrfCookie, string csrfToken,
        string? refreshCookie = null, object? body = null, string origin = Origin)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path);
        request.Headers.Add("Origin", origin);
        request.Headers.Add("X-CSRF-Token", csrfToken);
        request.Headers.Add("Cookie", refreshCookie is null ? csrfCookie : $"{csrfCookie}; {refreshCookie}");
        if (body is not null) request.Content = JsonContent.Create(body);
        return request;
    }

    private static string RefreshCookie(HttpResponseMessage response) => response.Headers.GetValues("Set-Cookie")
        .Single(x => x.StartsWith("hi_refresh=", StringComparison.Ordinal)).Split(';')[0];

    private static async Task<string> AccessTokenAsync(HttpResponseMessage response)
    {
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("accessToken").GetString()!;
    }

    private static async Task<HttpStatusCode> MeAsync(HttpClient client, string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/auth/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await client.SendAsync(request);
        return response.StatusCode;
    }

    [Fact]
    public async Task ExternalLoginIssuesOnlyBearerAndChecksCurrentAccount()
    {
        using var factory = new SqliteAppFactory();
        using var client = Client(factory);
        using var response = await client.PostAsJsonAsync("/api/v1/auth/login",
            new { email = Email, password = Password, sessionMode = "external" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(response.Headers.Contains("Set-Cookie"));
        var access = await AccessTokenAsync(response);
        Assert.Equal(HttpStatusCode.OK, await MeAsync(client, access));

        using var scope = factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        var admin = await database.Users.SingleAsync();
        admin.PasswordHash = new PasswordHasher<User>().HashPassword(admin, "a-new-password-for-test");
        await database.SaveChangesAsync();
        Assert.Equal(HttpStatusCode.Unauthorized, await MeAsync(client, access));
    }

    [Fact]
    public async Task BrowserRefreshRotatesAndReplayRevokesDescendantAccess()
    {
        using var factory = new SqliteAppFactory();
        using var client = Client(factory);
        var (csrfCookie, csrfToken) = await CsrfAsync(client);
        using var login = await client.SendAsync(BrowserPost("/api/v1/auth/login", csrfCookie, csrfToken,
            body: new { email = Email, password = Password, sessionMode = "browser" }));
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var oldCookie = RefreshCookie(login);
        var firstAccess = await AccessTokenAsync(login);
        Assert.Equal(HttpStatusCode.OK, await MeAsync(client, firstAccess));

        using var refreshed = await client.SendAsync(BrowserPost("/api/v1/auth/refresh", csrfCookie, csrfToken, oldCookie));
        Assert.Equal(HttpStatusCode.OK, refreshed.StatusCode);
        var nextCookie = RefreshCookie(refreshed);
        Assert.NotEqual(oldCookie, nextCookie);
        var secondAccess = await AccessTokenAsync(refreshed);
        Assert.Equal(HttpStatusCode.OK, await MeAsync(client, secondAccess));

        // Consumed-token evidence remains effective even after that token's idle expiry.
        using (var scope = factory.Services.CreateScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
            var oldHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(oldCookie.Split('=')[1])));
            await database.RefreshSessions.Where(x => x.TokenHash == oldHash)
                .ExecuteUpdateAsync(update => update.SetProperty(x => x.ExpiresAtUtc, DateTime.UtcNow.AddSeconds(-1)));
        }

        using var replay = await client.SendAsync(BrowserPost("/api/v1/auth/refresh", csrfCookie, csrfToken, oldCookie));
        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, await MeAsync(client, firstAccess));
        Assert.Equal(HttpStatusCode.Unauthorized, await MeAsync(client, secondAccess));
        using var afterReplay = await client.SendAsync(BrowserPost("/api/v1/auth/refresh", csrfCookie, csrfToken, nextCookie));
        Assert.Equal(HttpStatusCode.Unauthorized, afterReplay.StatusCode);
    }

    [Fact]
    public async Task LogoutAndCsrfOriginChecksAreEnforced()
    {
        using var factory = new SqliteAppFactory();
        using var client = Client(factory);
        var (csrfCookie, csrfToken) = await CsrfAsync(client);
        using var wrongOrigin = await client.SendAsync(BrowserPost("/api/v1/auth/login", csrfCookie, csrfToken,
            body: new { email = Email, password = Password, sessionMode = "browser" }, origin: "https://attacker.example"));
        Assert.Equal(HttpStatusCode.Forbidden, wrongOrigin.StatusCode);
        using var wrongToken = await client.SendAsync(BrowserPost("/api/v1/auth/login", csrfCookie, "incorrect",
            body: new { email = Email, password = Password, sessionMode = "browser" }));
        Assert.Equal(HttpStatusCode.Forbidden, wrongToken.StatusCode);

        using var login = await client.SendAsync(BrowserPost("/api/v1/auth/login", csrfCookie, csrfToken,
            body: new { email = Email, password = Password, sessionMode = "browser" }));
        var cookie = RefreshCookie(login);
        var access = await AccessTokenAsync(login);
        using var logout = await client.SendAsync(BrowserPost("/api/v1/auth/logout", csrfCookie, csrfToken, cookie));
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, await MeAsync(client, access));
    }

    [Fact]
    public async Task DisabledNoticeIsOnlyReturnedForCorrectPasswordAndAdminIsProtected()
    {
        using var factory = new SqliteAppFactory();
        using var client = Client(factory);
        using var scope = factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        var disabled = new User
        {
            Email = "disabled@example.com",
            NormalizedEmail = NormalizedText.Key("disabled@example.com"),
            FullName = "Disabled User",
            PasswordHash = "placeholder",
            RoleId = 2,
            IsActive = false
        };
        disabled.PasswordHash = new PasswordHasher<User>().HashPassword(disabled, Password);
        database.Users.Add(disabled);
        await database.SaveChangesAsync();

        using var wrong = await client.PostAsJsonAsync("/api/v1/auth/login",
            new { email = disabled.Email, password = "wrong", sessionMode = "external" });
        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        using var correct = await client.PostAsJsonAsync("/api/v1/auth/login",
            new { email = disabled.Email, password = Password, sessionMode = "external" });
        Assert.Equal(HttpStatusCode.Forbidden, correct.StatusCode);
        Assert.Contains("account_disabled", await correct.Content.ReadAsStringAsync());

        var admin = await database.Users.SingleAsync(x => x.IsSystemAdmin);
        admin.IsActive = false;
        await Assert.ThrowsAsync<DbUpdateException>(() => database.SaveChangesAsync());
        database.ChangeTracker.Clear();
        await Assert.ThrowsAsync<SqliteException>(() => database.Database.ExecuteSqlRawAsync(
            "UPDATE Users SET RoleId = 2 WHERE IsSystemAdmin = 1"));
        await Assert.ThrowsAsync<SqliteException>(() => database.Database.ExecuteSqlRawAsync(
            "UPDATE Users SET DeletedAtUtc = '2026-10-17' WHERE IsSystemAdmin = 1"));
        await Assert.ThrowsAsync<SqliteException>(() => database.Database.ExecuteSqlRawAsync(
            "DELETE FROM Users WHERE IsSystemAdmin = 1"));
    }

    [Fact]
    public async Task SimultaneousRefreshHasAtMostOneSuccessAndRevokesOnReplay()
    {
        using var factory = new SqliteAppFactory();
        using var client = Client(factory);
        var (csrfCookie, csrfToken) = await CsrfAsync(client);
        using var login = await client.SendAsync(BrowserPost("/api/v1/auth/login", csrfCookie, csrfToken,
            body: new { email = Email, password = Password, sessionMode = "browser" }));
        var oldCookie = RefreshCookie(login);

        var first = client.SendAsync(BrowserPost("/api/v1/auth/refresh", csrfCookie, csrfToken, oldCookie));
        var second = client.SendAsync(BrowserPost("/api/v1/auth/refresh", csrfCookie, csrfToken, oldCookie));
        using var firstResponse = await first;
        using var secondResponse = await second;
        Assert.Equal(1, new[] { firstResponse, secondResponse }.Count(x => x.StatusCode == HttpStatusCode.OK));
        Assert.Equal(1, new[] { firstResponse, secondResponse }.Count(x => x.StatusCode == HttpStatusCode.Unauthorized));
        var winner = firstResponse.StatusCode == HttpStatusCode.OK ? firstResponse : secondResponse;
        Assert.Equal(HttpStatusCode.Unauthorized, await MeAsync(client, await AccessTokenAsync(winner)));
    }

    [Fact]
    public async Task RoleAndActiveChangesInvalidateExistingTokens()
    {
        using var factory = new SqliteAppFactory();
        using var client = Client(factory);
        using var scope = factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        var user = new User
        {
            Email = "worker@example.com",
            NormalizedEmail = NormalizedText.Key("worker@example.com"),
            FullName = "Worker",
            PasswordHash = "placeholder",
            RoleId = 2,
            IsActive = true
        };
        user.PasswordHash = new PasswordHasher<User>().HashPassword(user, Password);
        database.Users.Add(user);
        await database.SaveChangesAsync();
        using var login = await client.PostAsJsonAsync("/api/v1/auth/login",
            new { email = user.Email, password = Password, sessionMode = "external" });
        var access = await AccessTokenAsync(login);
        Assert.Equal(HttpStatusCode.OK, await MeAsync(client, access));
        await database.Users.Where(x => x.Id == user.Id).ExecuteUpdateAsync(update => update.SetProperty(x => x.RoleId, 3));
        Assert.Equal(HttpStatusCode.Unauthorized, await MeAsync(client, access));

        using var relogin = await client.PostAsJsonAsync("/api/v1/auth/login",
            new { email = user.Email, password = Password, sessionMode = "external" });
        var newAccess = await AccessTokenAsync(relogin);
        Assert.Equal(HttpStatusCode.OK, await MeAsync(client, newAccess));
        await database.Users.Where(x => x.Id == user.Id).ExecuteUpdateAsync(update => update.SetProperty(x => x.IsActive, false));
        Assert.Equal(HttpStatusCode.Unauthorized, await MeAsync(client, newAccess));
    }

    [Fact]
    public async Task ExpiredRefreshSessionCannotRefreshOrAuthorize()
    {
        using var factory = new SqliteAppFactory();
        using var client = Client(factory);
        var (csrfCookie, csrfToken) = await CsrfAsync(client);
        using var login = await client.SendAsync(BrowserPost("/api/v1/auth/login", csrfCookie, csrfToken,
            body: new { email = Email, password = Password, sessionMode = "browser" }));
        var cookie = RefreshCookie(login);
        var access = await AccessTokenAsync(login);
        using var scope = factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        await database.RefreshSessions.ExecuteUpdateAsync(update =>
            update.SetProperty(x => x.ExpiresAtUtc, DateTime.UtcNow.AddSeconds(-1)));
        using var refresh = await client.SendAsync(BrowserPost("/api/v1/auth/refresh", csrfCookie, csrfToken, cookie));
        Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, await MeAsync(client, access));
    }

    [Fact]
    public async Task LoginIsRateLimitedWithoutLeakingAccountExistence()
    {
        using var factory = new SqliteAppFactory();
        using var client = Client(factory);
        for (var attempt = 0; attempt < 10; attempt++)
        {
            using var response = await client.PostAsJsonAsync("/api/v1/auth/login",
                new { email = "unknown@example.com", password = "wrong", sessionMode = "external" });
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.Contains("invalid_credentials", await response.Content.ReadAsStringAsync());
        }
        using var limited = await client.PostAsJsonAsync("/api/v1/auth/login",
            new { email = "unknown@example.com", password = "wrong", sessionMode = "external" });
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.Equal("application/problem+json", limited.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task ApiReadLimitReturnsLocalizedProblemAndRetryAfterWithoutLimitingHealthProbe()
    {
        using var factory = new SqliteAppFactory(new Dictionary<string, string?>
        {
            ["RateLimits:ReadPerMinute"] = "2"
        });
        using var client = Client(factory);
        for (var attempt = 0; attempt < 2; attempt++)
        {
            using var allowed = await client.GetAsync("/api/v1/auth/csrf");
            Assert.Equal(HttpStatusCode.NoContent, allowed.StatusCode);
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/auth/csrf");
        request.Headers.AcceptLanguage.ParseAdd("ar");
        using var limited = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.Equal("application/problem+json", limited.Content.Headers.ContentType?.MediaType);
        Assert.True(limited.Headers.TryGetValues("Retry-After", out var retryValues));
        Assert.True(int.Parse(retryValues.Single()) > 0);
        using var problem = JsonDocument.Parse(await limited.Content.ReadAsStringAsync());
        Assert.Equal("rate_limited", problem.RootElement.GetProperty("code").GetString());
        Assert.Equal("طلبات كثيرة جدًا. حاول مرة أخرى قريبًا.",
            problem.RootElement.GetProperty("title").GetString());

        using var probe = await client.GetAsync("/health/live");
        Assert.NotEqual(HttpStatusCode.TooManyRequests, probe.StatusCode);
    }

    [Fact]
    public async Task ApiWriteLimitAppliesSeparatelyFromReadAndLoginLimits()
    {
        using var factory = new SqliteAppFactory(new Dictionary<string, string?>
        {
            ["RateLimits:ReadPerMinute"] = "1",
            ["RateLimits:WritePerMinute"] = "2"
        });
        using var client = Client(factory);
        using var read = await client.GetAsync("/api/v1/auth/csrf");
        Assert.Equal(HttpStatusCode.NoContent, read.StatusCode);
        for (var attempt = 0; attempt < 2; attempt++)
        {
            using var write = await client.PostAsJsonAsync("/api/v1/auth/login",
                new { email = "unknown@example.com", password = "wrong", sessionMode = "external" });
            Assert.Equal(HttpStatusCode.Unauthorized, write.StatusCode);
        }
        using var limited = await client.PostAsJsonAsync("/api/v1/auth/login",
            new { email = "unknown@example.com", password = "wrong", sessionMode = "external" });
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
    }

    [Fact]
    public async Task ExpiredSignedJwtIsRejected()
    {
        using var factory = new SqliteAppFactory();
        using var client = Client(factory);
        var expired = new JwtSecurityToken(
            issuer: "happy-inventory-api", audience: "happy-inventory-clients",
            claims: [new Claim("sub", "1"), new Claim("role", "Admin"), new Claim("sv", "0")],
            notBefore: DateTime.UtcNow.AddMinutes(-20), expires: DateTime.UtcNow.AddMinutes(-10),
            signingCredentials: new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes("test-only-signing-key-with-at-least-thirty-two-characters")),
                SecurityAlgorithms.HmacSha256));
        var token = new JwtSecurityTokenHandler().WriteToken(expired);
        Assert.Equal(HttpStatusCode.Unauthorized, await MeAsync(client, token));
    }

    [Fact]
    public async Task DisablingAnAccountInvalidatesItsRefreshCookie()
    {
        using var factory = new SqliteAppFactory();
        using var client = Client(factory);
        using var scope = factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        var user = new User
        {
            Email = "browser-worker@example.com",
            NormalizedEmail = NormalizedText.Key("browser-worker@example.com"),
            FullName = "Browser Worker",
            PasswordHash = "placeholder",
            RoleId = 2,
            IsActive = true
        };
        user.PasswordHash = new PasswordHasher<User>().HashPassword(user, Password);
        database.Users.Add(user);
        await database.SaveChangesAsync();
        var (csrfCookie, csrfToken) = await CsrfAsync(client);
        using var login = await client.SendAsync(BrowserPost("/api/v1/auth/login", csrfCookie, csrfToken,
            body: new { email = user.Email, password = Password, sessionMode = "browser" }));
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var cookie = RefreshCookie(login);
        var access = await AccessTokenAsync(login);
        await database.Users.Where(x => x.Id == user.Id)
            .ExecuteUpdateAsync(update => update.SetProperty(x => x.IsActive, false));
        Assert.Equal(HttpStatusCode.Unauthorized, await MeAsync(client, access));
        using var refresh = await client.SendAsync(BrowserPost("/api/v1/auth/refresh", csrfCookie, csrfToken, cookie));
        Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);
    }

    [Fact]
    public async Task MalformedLoginAndBearerFailuresUseLocalizedProblemDetails()
    {
        using var factory = new SqliteAppFactory();
        using var client = Client(factory);
        using var malformed = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login")
        {
            Content = new StringContent("{broken", Encoding.UTF8, "application/json")
        };
        malformed.Headers.AcceptLanguage.ParseAdd("ar");
        using var invalid = await client.SendAsync(malformed);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Equal("application/problem+json", invalid.Content.Headers.ContentType?.MediaType);
        using var invalidBody = JsonDocument.Parse(await invalid.Content.ReadAsStringAsync());
        Assert.Equal("invalid_request", invalidBody.RootElement.GetProperty("code").GetString());
        Assert.Contains("تحقق", invalidBody.RootElement.GetProperty("title").GetString());

        using var unauthorized = new HttpRequestMessage(HttpMethod.Get, "/api/v1/auth/me");
        unauthorized.Headers.AcceptLanguage.ParseAdd("ar");
        using var response = await client.SendAsync(unauthorized);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var unauthorizedBody = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Contains("سجّل", unauthorizedBody.RootElement.GetProperty("title").GetString());
    }

    [Fact]
    public void ProductionAuthSettingsRequireSecretAndExactHttpsOrigin()
    {
        var noSecret = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["Auth:BrowserOrigin"] = "https://example.com" }).Build();
        Assert.Throws<InvalidOperationException>(() => AuthSettings.Load(noSecret, isDevelopment: false));
        var noOrigin = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Auth:SigningKey"] = "test-only-signing-key-with-at-least-thirty-two-characters"
        }).Build();
        Assert.Throws<InvalidOperationException>(() => AuthSettings.Load(noOrigin, isDevelopment: false));
    }
}
