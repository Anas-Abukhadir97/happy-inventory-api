using HappyInventory.Application.Auth;
using HappyInventory.Application.Integrations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;

namespace HappyInventory.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/supplier-orders")]
public sealed class SupplierOrdersController(IIntegrationService service, IStringLocalizer<SharedResource> text)
    : BusinessControllerBase(text)
{
    [HttpGet]
    public Task<IActionResult> List([FromQuery] int page = 1, [FromQuery] int pageSize = 10,
        CancellationToken ct = default) =>
        Execute(async () => Ok(await service.OrdersAsync(page, pageSize, ct)));

    [HttpPost]
    [Authorize(Roles = "Admin,Management")]
    public Task<IActionResult> Create([FromBody] SupplierOrderWrite write, CancellationToken ct) => Execute(async () =>
    {
        var caller = (CurrentUser)HttpContext.Items["CurrentUser"]!;
        var order = await service.CreateOrderAsync(caller.Id, write, ct);
        return Created($"/api/v1/supplier-orders/{order.Id}", order);
    });
}
