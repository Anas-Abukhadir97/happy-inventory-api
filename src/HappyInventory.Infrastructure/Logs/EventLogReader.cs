using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HappyInventory.Application.Inventory;
using HappyInventory.Application.Logs;

namespace HappyInventory.Infrastructure.Logs;

public sealed class EventLogReader : IEventLogReader
{
    private readonly string _directory;

    public EventLogReader(string directory) => _directory = directory;

    public async Task<PagedResult<LogSummary>> ListAsync(int page, int pageSize, string? level, DateTime? from,
        DateTime? to, string? search, CancellationToken ct)
    {
        if (page < 1 || pageSize is < 1 or > 10 || search?.Length > 200 ||
            level?.Length > 30 || (from is not null && to is not null && from > to))
            throw new BusinessFailure("invalid_request", "InvalidRequest", 400);
        var records = await ReadAllAsync(ct);
        var filtered = records.Where(x =>
            (string.IsNullOrEmpty(level) || string.Equals(x.Level, level, StringComparison.OrdinalIgnoreCase)) &&
            (from is null || x.TimestampUtc >= from.Value.ToUniversalTime()) &&
            (to is null || x.TimestampUtc <= to.Value.ToUniversalTime()) &&
            (string.IsNullOrEmpty(search) || x.Message.Contains(search, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(x => x.TimestampUtc).ThenByDescending(x => x.Id, StringComparer.Ordinal).ToList();
        var count = filtered.Count;
        var rows = filtered.Skip((int)Math.Min((long)(page - 1) * pageSize, int.MaxValue)).Take(pageSize)
            .Select(x => new LogSummary(x.Id, x.TimestampUtc, x.Level, x.Message, x.CorrelationId, x.EventName)).ToList();
        return new PagedResult<LogSummary>(rows, page, pageSize, count.ToString(CultureInfo.InvariantCulture),
            ((count + pageSize - 1) / pageSize).ToString(CultureInfo.InvariantCulture));
    }

    public async Task<LogDetail?> GetAsync(string id, CancellationToken ct)
    {
        if (id.Length is < 1 or > 20 || !id.All(char.IsAsciiDigit)) return null;
        return (await ReadAllAsync(ct)).FirstOrDefault(x => x.Id == id);
    }

    private async Task<List<LogDetail>> ReadAllAsync(CancellationToken ct)
    {
        var records = new List<LogDetail>();
        if (!Directory.Exists(_directory)) return records;
        foreach (var file in Directory.EnumerateFiles(_directory, "inventory-*.json").OrderBy(x => x))
        {
            await using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream);
            while (await reader.ReadLineAsync(ct) is { } line)
            {
                try
                {
                    using var json = JsonDocument.Parse(line);
                    var root = json.RootElement;
                    if (!root.TryGetProperty("Timestamp", out var timestamp) ||
                        !DateTime.TryParse(timestamp.GetString(), CultureInfo.InvariantCulture,
                            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var at)) continue;
                    var level = ReadString(root, "Level") ?? "Information";
                    var message = ReadString(root, "RenderedMessage") ?? ReadString(root, "MessageTemplate") ?? "";
                    var properties = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
                    if (root.TryGetProperty("Properties", out var data) && data.ValueKind == JsonValueKind.Object)
                    {
                        foreach (var property in data.EnumerateObject())
                        {
                            if (Sensitive(property.Name)) continue;
                            properties[property.Name] = property.Value.Clone();
                        }
                    }
                    var hash = SHA256.HashData(Encoding.UTF8.GetBytes(file + line));
                    var id = BitConverter.ToUInt64(hash, 0).ToString(CultureInfo.InvariantCulture);
                    records.Add(new LogDetail(id, at, level, message,
                        ReadProperty(properties, "CorrelationId"), ReadProperty(properties, "EventName"),
                        ReadString(root, "Exception"), properties));
                }
                catch (JsonException) { /* A concurrently written final line may be incomplete. */ }
            }
        }
        return records;
    }

    private static string? ReadString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    private static string? ReadProperty(IReadOnlyDictionary<string, JsonElement> data, string key) =>
        data.TryGetValue(key, out var value) ? value.ToString() : null;
    private static bool Sensitive(string key) =>
        key.Contains("password", StringComparison.OrdinalIgnoreCase) ||
        key.Contains("token", StringComparison.OrdinalIgnoreCase) ||
        key.Contains("secret", StringComparison.OrdinalIgnoreCase) ||
        key.Contains("cookie", StringComparison.OrdinalIgnoreCase) ||
        key.Contains("authorization", StringComparison.OrdinalIgnoreCase);
}
