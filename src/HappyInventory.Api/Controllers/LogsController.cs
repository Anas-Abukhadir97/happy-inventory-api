using HappyInventory.Application.Logs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;

namespace HappyInventory.Api.Controllers;

[ApiController]
[Authorize(Roles = "Admin")]
[Route("api/v1/logs")]
public sealed class LogsController(IEventLogReader reader, IStringLocalizer<SharedResource> text)
    : BusinessControllerBase(text)
{
    [HttpGet]
    public Task<IActionResult> List([FromQuery] int page = 1, [FromQuery] int pageSize = 10,
        [FromQuery] string? level = null, [FromQuery] DateTime? from = null, [FromQuery] DateTime? to = null,
        [FromQuery] string? search = null, CancellationToken ct = default) =>
        Execute(async () => Ok(await reader.ListAsync(page, pageSize, level, from, to, search, ct)));

    [HttpGet("{id}")]
    public Task<IActionResult> Get(string id, CancellationToken ct) => Execute(async () =>
    {
        var row = await reader.GetAsync(id, ct);
        return row is null ? Failure("not_found", "NotFound", 404) : Ok(row);
    });
}
