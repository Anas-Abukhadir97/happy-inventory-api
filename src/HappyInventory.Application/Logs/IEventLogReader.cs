using System.Text.Json;
using System.Text.Json.Serialization;
using HappyInventory.Application.Inventory;

namespace HappyInventory.Application.Logs;

public sealed record LogSummary(string Id, DateTime TimestampUtc, string Level, string Message,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? CorrelationId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? EventName);
public sealed record LogDetail(string Id, DateTime TimestampUtc, string Level, string Message,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? CorrelationId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? EventName,
    string? Exception, IReadOnlyDictionary<string, JsonElement> Properties);

public interface IEventLogReader
{
    Task<PagedResult<LogSummary>> ListAsync(int page, int pageSize, string? level, DateTime? from,
        DateTime? to, string? search, CancellationToken cancellationToken);
    Task<LogDetail?> GetAsync(string id, CancellationToken cancellationToken);
}
