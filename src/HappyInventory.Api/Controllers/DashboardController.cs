using HappyInventory.Application.Inventory;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;

namespace HappyInventory.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/dashboard")]
public sealed class DashboardController(IInventoryService service, IStringLocalizer<SharedResource> text)
    : BusinessControllerBase(text)
{
    [HttpGet("warehouses")]
    public Task<IActionResult> Warehouses([FromQuery] int page = 1, [FromQuery] int pageSize = 10,
        CancellationToken ct = default) => Execute(async () => Ok(await service.WarehouseStatusesAsync(page, pageSize, ct)));

    [HttpGet("items/highest")]
    public async Task<IActionResult> Highest(CancellationToken ct) => Ok(await service.QuantityItemsAsync(true, ct));

    [HttpGet("items/lowest")]
    public async Task<IActionResult> Lowest(CancellationToken ct) => Ok(await service.QuantityItemsAsync(false, ct));
}
