using System.Text.Json;
using Microsoft.Extensions.Localization;

namespace HappyInventory.Api;

internal static class ApiProblemResponses
{
    public static Task WriteAsync(HttpContext context, int status, string code, string resourceKey)
    {
        var text = context.RequestServices.GetRequiredService<IStringLocalizer<SharedResource>>();
        context.Response.StatusCode = status;
        context.Response.ContentType = "application/problem+json";
        context.Response.Headers.CacheControl = "no-store";
        return context.Response.WriteAsync(JsonSerializer.Serialize(new
        {
            type = "about:blank",
            title = text[resourceKey].Value,
            status,
            detail = text[resourceKey].Value,
            code,
            traceId = context.TraceIdentifier
        }));
    }
}
