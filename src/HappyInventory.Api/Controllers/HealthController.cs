using HappyInventory.Application.Health;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;

namespace HappyInventory.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/health")]
public sealed class HealthController(IHealthService health, IStringLocalizer<SharedResource> text)
    : BusinessControllerBase(text)
{
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct) =>
        await health.IsReadyAsync(ct)
            ? Ok(new { status = "healthy" })
            : Failure("service_unavailable", "ServiceUnavailable", 503);
}
