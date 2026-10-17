using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;

namespace HappyInventory.Infrastructure.Auth;

public sealed class AuthSettings
{
    public required string Issuer { get; init; }
    public required string Audience { get; init; }
    public required byte[] SigningKey { get; init; }

    public static AuthSettings Load(IConfiguration configuration, bool isDevelopment)
    {
        var key = configuration["Auth:SigningKey"];
        if (string.IsNullOrWhiteSpace(key) && isDevelopment)
        {
            var directory = Path.GetFullPath("data");
            Directory.CreateDirectory(directory);
            var file = Path.Combine(directory, "dev-jwt-key");
            if (!File.Exists(file))
            {
                var created = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
                try
                {
                    using var stream = new FileStream(file, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                    using var writer = new StreamWriter(stream);
                    writer.Write(created);
                }
                catch (IOException) when (File.Exists(file)) { }
            }
            key = File.ReadAllText(file);
        }

        if (string.IsNullOrWhiteSpace(key) || Encoding.UTF8.GetByteCount(key) < 32)
            throw new InvalidOperationException("Auth:SigningKey must contain at least 32 UTF-8 bytes.");

        var browserOrigin = configuration["Auth:BrowserOrigin"];
        if (!isDevelopment &&
            (string.IsNullOrWhiteSpace(browserOrigin) ||
             !Uri.TryCreate(browserOrigin, UriKind.Absolute, out var parsedOrigin) ||
             parsedOrigin.Scheme != Uri.UriSchemeHttps || parsedOrigin.AbsolutePath != "/" ||
             !string.IsNullOrEmpty(parsedOrigin.Query) || !string.IsNullOrEmpty(parsedOrigin.Fragment) ||
             !string.IsNullOrEmpty(parsedOrigin.UserInfo)))
            throw new InvalidOperationException("Auth:BrowserOrigin must be an exact HTTPS origin in production.");

        var issuer = configuration["Auth:Issuer"] ?? "happy-inventory-api";
        var audience = configuration["Auth:Audience"] ?? "happy-inventory-clients";
        if (string.IsNullOrWhiteSpace(issuer) || string.IsNullOrWhiteSpace(audience))
            throw new InvalidOperationException("Auth:Issuer and Auth:Audience must be set.");

        return new AuthSettings
        {
            Issuer = issuer,
            Audience = audience,
            SigningKey = Encoding.UTF8.GetBytes(key)
        };
    }
}
