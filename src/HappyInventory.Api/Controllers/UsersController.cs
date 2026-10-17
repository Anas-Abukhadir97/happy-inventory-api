using HappyInventory.Application.Auth;
using HappyInventory.Application.Users;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;

namespace HappyInventory.Api.Controllers;

[ApiController]
[Authorize(Roles = "Admin")]
[Route("api/v1/users")]
public sealed class UsersController(IUserService service, IStringLocalizer<SharedResource> text)
    : BusinessControllerBase(text)
{
    [HttpGet]
    public Task<IActionResult> List([FromQuery] int page = 1, [FromQuery] int pageSize = 10,
        [FromQuery] string? search = null, CancellationToken ct = default) =>
        Execute(async () => Ok(await service.UsersAsync(page, pageSize, search, ct)));

    [HttpGet("{id:int}")]
    public Task<IActionResult> Get(int id, CancellationToken ct) => Execute(async () =>
    {
        var row = await service.UserAsync(id, ct);
        if (row is null) return Failure("not_found", "NotFound", 404);
        SetEtag(row.Version);
        return Ok(row);
    });

    [HttpPost]
    public Task<IActionResult> Create([FromBody] UserCreate write, CancellationToken ct) => Execute(async () =>
    {
        var row = await service.CreateAsync(write, ct);
        SetEtag(row.Version);
        return Created($"/api/v1/users/{row.Id}", row);
    });

    [HttpPut("{id:int}")]
    public Task<IActionResult> Update(int id, [FromBody] UserUpdate write, CancellationToken ct) => Execute(async () =>
    {
        var row = await service.UpdateAsync(id, RequireVersion(), write, ct);
        SetEtag(row.Version);
        return Ok(row);
    });

    [HttpDelete("{id:int}")]
    public Task<IActionResult> Delete(int id, CancellationToken ct) => Execute(async () =>
    {
        var actor = (CurrentUser)HttpContext.Items["CurrentUser"]!;
        await service.DeleteAsync(id, RequireVersion(), actor.Id, ct);
        return NoContent();
    });

    [HttpPut("{id:int}/password")]
    public Task<IActionResult> ResetPassword(int id, [FromBody] PasswordReset write, CancellationToken ct) => Execute(async () =>
    {
        await service.ResetPasswordAsync(id, RequireVersion(), write, ct);
        return NoContent();
    });
}
