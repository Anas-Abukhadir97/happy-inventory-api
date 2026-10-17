using System.Text;

namespace HappyInventory.Domain.ValueObjects;

public static class NormalizedText
{
    public static string Key(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var normalized = value.Trim().Normalize(NormalizationForm.FormKC).ToUpperInvariant();
        return normalized.Length == 0 ? throw new ArgumentException("Value cannot be blank.", nameof(value)) : normalized;
    }
}
