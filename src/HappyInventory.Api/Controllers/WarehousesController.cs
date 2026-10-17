using HappyInventory.Application.Inventory;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;

namespace HappyInventory.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/warehouses")]
public sealed class WarehousesController(IInventoryService service, IStringLocalizer<SharedResource> text)
    : BusinessControllerBase(text)
{
    [HttpGet]
    public Task<IActionResult> List([FromQuery] int page = 1, [FromQuery] int pageSize = 10,
        [FromQuery] string? search = null, CancellationToken ct = default) =>
        Execute(async () => Ok(await service.WarehousesAsync(page, pageSize, search, ct)));

    [HttpGet("{id:int}")]
    public Task<IActionResult> Get(int id, CancellationToken ct) => Execute(async () =>
    {
        var row = await service.WarehouseAsync(id, ct);
        if (row is null) return Failure("not_found", "NotFound", 404);
        SetEtag(row.Version);
        return Ok(row);
    });

    [HttpPost]
    [Authorize(Roles = "Admin,Management")]
    public Task<IActionResult> Create([FromBody] WarehouseWrite write, CancellationToken ct) => Execute(async () =>
    {
        var row = await service.CreateWarehouseAsync(write, ct);
        SetEtag(row.Version);
        return Created($"/api/v1/warehouses/{row.Id}", row);
    });

    [HttpPut("{id:int}")]
    [Authorize(Roles = "Admin,Management")]
    public Task<IActionResult> Update(int id, [FromBody] WarehouseWrite write, CancellationToken ct) => Execute(async () =>
    {
        var row = await service.UpdateWarehouseAsync(id, RequireVersion(), write, ct);
        SetEtag(row.Version);
        return Ok(row);
    });

    [HttpDelete("{id:int}")]
    [Authorize(Roles = "Admin,Management")]
    public Task<IActionResult> Delete(int id, CancellationToken ct) => Execute(async () =>
    {
        await service.DeleteWarehouseAsync(id, RequireVersion(), ct);
        return NoContent();
    });

    [HttpGet("{id:int}/items")]
    public Task<IActionResult> ListItems(int id, [FromQuery] int page = 1, [FromQuery] int pageSize = 10,
        [FromQuery] string? search = null, CancellationToken ct = default) =>
        Execute(async () => Ok(await service.ItemsAsync(id, page, pageSize, search, ct)));

    [HttpGet("{id:int}/items/{itemId:int}")]
    public Task<IActionResult> GetItem(int id, int itemId, CancellationToken ct) => Execute(async () =>
    {
        var row = await service.ItemAsync(id, itemId, ct);
        if (row is null) return Failure("not_found", "NotFound", 404);
        SetEtag(row.Version);
        return Ok(row);
    });

    [HttpPost("{id:int}/items")]
    [Authorize(Roles = "Admin,Management")]
    public Task<IActionResult> CreateItem(int id, [FromBody] ItemWrite write, CancellationToken ct) => Execute(async () =>
    {
        var row = await service.CreateItemAsync(id, write, ct);
        SetEtag(row.Version);
        return Created($"/api/v1/warehouses/{id}/items/{row.Id}", row);
    });

    [HttpPut("{id:int}/items/{itemId:int}")]
    [Authorize(Roles = "Admin,Management")]
    public Task<IActionResult> UpdateItem(int id, int itemId, [FromBody] ItemWrite write, CancellationToken ct) => Execute(async () =>
    {
        var row = await service.UpdateItemAsync(id, itemId, RequireVersion(), write, ct);
        SetEtag(row.Version);
        return Ok(row);
    });

    [HttpDelete("{id:int}/items/{itemId:int}")]
    [Authorize(Roles = "Admin,Management")]
    public Task<IActionResult> DeleteItem(int id, int itemId, CancellationToken ct) => Execute(async () =>
    {
        await service.DeleteItemAsync(id, itemId, RequireVersion(), ct);
        return NoContent();
    });
}
