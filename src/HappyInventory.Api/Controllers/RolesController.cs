using HappyInventory.Application.Users;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HappyInventory.Api.Controllers;

[ApiController]
[Authorize(Roles = "Admin")]
[Route("api/v1/roles")]
public sealed class RolesController(IUserService service) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct) => Ok(await service.RolesAsync(ct));
}
