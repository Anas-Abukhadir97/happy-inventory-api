using HappyInventory.Application.Inventory;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;

namespace HappyInventory.Api.Controllers;

public abstract class BusinessControllerBase(IStringLocalizer<SharedResource> text) : ControllerBase
{
    protected async Task<IActionResult> Execute(Func<Task<IActionResult>> action)
    {
        try { return await action(); }
        catch (BusinessFailure failure) { return Failure(failure.Code, failure.ResourceKey, failure.StatusCode); }
    }

    protected IActionResult Failure(string code, string key, int status)
    {
        var value = text[key].Value;
        var problem = new ProblemDetails
        {
            Type = "about:blank",
            Title = value,
            Status = status,
            Detail = value,
            Instance = HttpContext.Request.Path
        };
        problem.Extensions["code"] = code;
        problem.Extensions["traceId"] = HttpContext.TraceIdentifier;
        return new ObjectResult(problem) { StatusCode = status, ContentTypes = { "application/problem+json" } };
    }

    protected Guid RequireVersion()
    {
        if (!Request.Headers.TryGetValue("If-Match", out var header) || header.Count == 0)
            throw new BusinessFailure("precondition_required", "PreconditionRequired", 428);
        var value = header.ToString();
        if (header.Count != 1 || value.Length != 38 || value[0] != '"' || value[^1] != '"' ||
            !Guid.TryParseExact(value[1..^1], "D", out var version))
            throw new BusinessFailure("stale_version", "StaleVersion", 412);
        return version;
    }

    protected void SetEtag(Guid version) => Response.Headers.ETag = $"\"{version:D}\"";
}
