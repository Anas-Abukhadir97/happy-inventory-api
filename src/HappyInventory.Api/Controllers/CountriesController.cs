using HappyInventory.Application.Inventory;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HappyInventory.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/countries")]
public sealed class CountriesController(IInventoryService service) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct) => Ok(await service.CountriesAsync(ct));
}
