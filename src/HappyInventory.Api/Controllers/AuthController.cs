using System.Security.Cryptography;
using System.Text;
using HappyInventory.Application.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Localization;
using Microsoft.IdentityModel.Tokens;

namespace HappyInventory.Api.Controllers;

[ApiController]
[Route("api/v1/auth")]
public sealed class AuthController(IAuthService auth, IConfiguration configuration, IHostEnvironment environment,
    IStringLocalizer<SharedResource> text) : ControllerBase
{
    private const string RefreshCookie = "hi_refresh";
    private const string CsrfCookie = "hi_csrf";
    private const string CookiePath = "/api/v1/auth";

    [HttpGet("csrf")]
    [AllowAnonymous]
    public IActionResult Csrf()
    {
        Response.Headers.CacheControl = "no-store";
        var token = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));
        Response.Cookies.Append(CsrfCookie, token, new CookieOptions
        {
            HttpOnly = false,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Path = CookiePath,
            IsEssential = true,
            MaxAge = TimeSpan.FromHours(1)
        });
        Response.Headers["X-CSRF-Token"] = token;
        return NoContent();
    }

    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest? request, CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        if (request is null || string.IsNullOrWhiteSpace(request.Email) || request.Email.Length > 254 ||
            string.IsNullOrEmpty(request.Password) || request.Password.Length > 128 ||
            request.SessionMode is not ("browser" or "external"))
            return Failure("invalid_request", "InvalidRequest", 400);

        var browser = request.SessionMode == "browser";
        if (browser && !ValidBrowserRequest()) return Failure("csrf_failed", "CsrfFailed", 403);
        try
        {
            var result = await auth.LoginAsync(request.Email, request.Password, browser, cancellationToken);
            if (result.RefreshToken is not null) SetRefreshCookie(result.RefreshToken);
            return Ok(ToResponse(result));
        }
        catch (AuthFailure failure) { return Failure(failure.Code, failure.ResourceKey, failure.StatusCode); }
    }

    [HttpPost("refresh")]
    [AllowAnonymous]
    public async Task<IActionResult> Refresh(CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        if (!ValidBrowserRequest()) return Failure("csrf_failed", "CsrfFailed", 403);
        if (!Request.Cookies.TryGetValue(RefreshCookie, out var token) || string.IsNullOrWhiteSpace(token))
            return Failure("invalid_session", "InvalidSession", 401);
        try
        {
            var result = await auth.RefreshAsync(token, cancellationToken);
            SetRefreshCookie(result.RefreshToken!);
            return Ok(ToResponse(result));
        }
        catch (AuthFailure failure)
        {
            ClearRefreshCookie();
            return Failure(failure.Code, failure.ResourceKey, failure.StatusCode);
        }
    }

    [HttpPost("logout")]
    [AllowAnonymous]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        if (!ValidBrowserRequest()) return Failure("csrf_failed", "CsrfFailed", 403);
        if (Request.Cookies.TryGetValue(RefreshCookie, out var token) && !string.IsNullOrWhiteSpace(token))
            await auth.LogoutAsync(token, cancellationToken);
        ClearRefreshCookie();
        return NoContent();
    }

    [HttpGet("me")]
    [Authorize]
    public IActionResult Me() => HttpContext.Items["CurrentUser"] is CurrentUser user
        ? Ok(user)
        : Unauthorized();

    private bool ValidBrowserRequest()
    {
        if (!environment.IsDevelopment() && !Request.IsHttps) return false;
        var expectedOrigin = configuration["Auth:BrowserOrigin"];
        if (string.IsNullOrWhiteSpace(expectedOrigin))
            expectedOrigin = $"{Request.Scheme}://{Request.Host}";
        if (!Uri.TryCreate(expectedOrigin, UriKind.Absolute, out var expected) || expected.Scheme != Uri.UriSchemeHttps)
            return false;
        if (!Request.Headers.TryGetValue("Origin", out var origin) || origin.Count != 1 ||
            !string.Equals(origin[0], expected.GetLeftPart(UriPartial.Authority), StringComparison.Ordinal))
            return false;
        if (!Request.Cookies.TryGetValue(CsrfCookie, out var cookie) ||
            !Request.Headers.TryGetValue("X-CSRF-Token", out var header) || header.Count != 1)
            return false;
        var left = Encoding.UTF8.GetBytes(cookie);
        var right = Encoding.UTF8.GetBytes(header[0]!);
        return left.Length == right.Length && left.Length >= 32 && CryptographicOperations.FixedTimeEquals(left, right);
    }

    private void SetRefreshCookie(string token) => Response.Cookies.Append(RefreshCookie, token, new CookieOptions
    {
        HttpOnly = true,
        Secure = true,
        SameSite = SameSiteMode.Strict,
        Path = CookiePath,
        IsEssential = true,
        MaxAge = TimeSpan.FromDays(7)
    });

    private void ClearRefreshCookie() => Response.Cookies.Delete(RefreshCookie, new CookieOptions
    {
        HttpOnly = true,
        Secure = true,
        SameSite = SameSiteMode.Strict,
        Path = CookiePath
    });

    private IActionResult Failure(string code, string resourceKey, int status)
    {
        var problem = new ProblemDetails
        {
            Type = "about:blank",
            Title = text[resourceKey],
            Status = status,
            Detail = text[resourceKey],
            Instance = HttpContext.Request.Path
        };
        problem.Extensions["code"] = code;
        problem.Extensions["traceId"] = HttpContext.TraceIdentifier;
        return new ObjectResult(problem) { StatusCode = status, ContentTypes = { "application/problem+json" } };
    }

    private static object ToResponse(AuthResult result) => new
    {
        result.AccessToken,
        TokenType = "Bearer",
        result.ExpiresAtUtc,
        result.User
    };

    public sealed record LoginRequest(string? Email, string? Password, string? SessionMode);
}
