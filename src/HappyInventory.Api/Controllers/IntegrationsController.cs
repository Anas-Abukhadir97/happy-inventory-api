using HappyInventory.Application.Auth;
using HappyInventory.Application.Integrations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;

namespace HappyInventory.Api.Controllers;

[ApiController]
[Authorize(Roles = "Admin,Management")]
[Route("api/v1/integrations")]
public sealed class IntegrationsController(IIntegrationService service, IStringLocalizer<SharedResource> text)
    : BusinessControllerBase(text)
{
    [HttpPost("inventory-deductions")]
    public Task<IActionResult> Deduct([FromBody] DeductionWrite write, CancellationToken ct) => Execute(async () =>
    {
        var caller = (CurrentUser)HttpContext.Items["CurrentUser"]!;
        var keys = Request.Headers["Idempotency-Key"];
        if (keys.Count != 1) return Failure("invalid_request", "InvalidRequest", 400);
        var result = await service.DeductAsync(caller.Id, keys[0], write, ct);
        return Ok(result);
    });
}
