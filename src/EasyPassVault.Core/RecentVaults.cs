using System.Collections;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EasyPassVault.Core;

/// <summary>A vault file opened earlier: its full path and when it was last opened. Nothing from inside the vault.</summary>
public sealed record RecentVault(string Path, DateTime OpenedUtc);

/// <summary>
/// Up to <see cref="Max"/> recently opened vault files, the most recent first. Immutable and compared by
/// contents, so <see cref="WindowLayout"/> keeps value equality. Stored in the layout file as a JSON array
/// of {Path, OpenedUtc}; anything else in that array is ignored when reading.
/// </summary>
[JsonConverter(typeof(RecentVaultListConverter))]
public sealed class RecentVaultList : IReadOnlyList<RecentVault>, IEquatable<RecentVaultList>
{
    public const int Max = 5;
    private const int MaxPathLength = 1024;
    public static readonly RecentVaultList Empty = new([]);
    private readonly RecentVault[] items;

    private RecentVaultList(RecentVault[] items) => this.items = items;

    public static RecentVaultList From(IEnumerable<RecentVault> items) => new RecentVaultList(items.ToArray()).Normalize();

    public RecentVault this[int index] => items[index];
    public int Count => items.Length;
    public IEnumerator<RecentVault> GetEnumerator() => ((IEnumerable<RecentVault>)items).GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>Puts <paramref name="path"/> first with the given time; the oldest entry beyond the limit is dropped.</summary>
    public RecentVaultList Touch(string path, DateTime nowUtc)
    {
        if (Clean(path) is not { } full) return this;
        var entry = new RecentVault(full, DateTime.SpecifyKind(nowUtc.ToUniversalTime(), DateTimeKind.Utc));
        return new(items.Where(item => !Same(item.Path, full)).Prepend(entry).Take(Max).ToArray());
    }

    public RecentVaultList Remove(string path) =>
        new(items.Where(item => !Same(item.Path, path)).ToArray());

    public bool Contains(string path) => items.Any(item => Same(item.Path, path));

    /// <summary>
    /// Keeps only well-formed full paths to .kdbx files, one entry per file (the latest time wins),
    /// newest first, at most <see cref="Max"/>.
    /// </summary>
    public RecentVaultList Normalize() => new(items
        .Select(item => Clean(item?.Path) is { } full ? new RecentVault(full, AsUtc(item!.OpenedUtc)) : null)
        .OfType<RecentVault>()
        .GroupBy(item => item.Path, StringComparer.OrdinalIgnoreCase)
        .Select(group => group.MaxBy(item => item.OpenedUtc)!)
        .OrderByDescending(item => item.OpenedUtc)
        .Take(Max)
        .ToArray());

    public static bool Same(string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private static DateTime AsUtc(DateTime time) =>
        time.Kind == DateTimeKind.Local ? time.ToUniversalTime() : DateTime.SpecifyKind(time, DateTimeKind.Utc);

    private static string? Clean(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Length > MaxPathLength || path.Any(char.IsControl) ||
            !System.IO.Path.IsPathFullyQualified(path)) return null;
        try
        {
            string full = System.IO.Path.GetFullPath(path);
            return System.IO.Path.GetExtension(full).Equals(".kdbx", StringComparison.OrdinalIgnoreCase) ? full : null;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { return null; }
    }

    public bool Equals(RecentVaultList? other) => other is not null && items.SequenceEqual(other.items);
    public override bool Equals(object? obj) => Equals(obj as RecentVaultList);
    public override int GetHashCode() => items.Aggregate(0, (hash, item) => HashCode.Combine(hash, item));
    public static bool operator ==(RecentVaultList? a, RecentVaultList? b) => a?.Equals(b) ?? b is null;
    public static bool operator !=(RecentVaultList? a, RecentVaultList? b) => !(a == b);
}

internal sealed class RecentVaultListConverter : JsonConverter<RecentVaultList>
{
    public override RecentVaultList Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        if (document.RootElement.ValueKind != JsonValueKind.Array) return RecentVaultList.Empty;
        var result = new List<RecentVault>();
        foreach (var element in document.RootElement.EnumerateArray().Take(50))
        {
            if (element.ValueKind != JsonValueKind.Object ||
                !element.TryGetProperty(nameof(RecentVault.Path), out var path) || path.ValueKind != JsonValueKind.String ||
                !element.TryGetProperty(nameof(RecentVault.OpenedUtc), out var opened) || opened.ValueKind != JsonValueKind.String ||
                !opened.TryGetDateTime(out var time)) continue;
            result.Add(new(path.GetString()!, time));
        }
        return RecentVaultList.From(result);
    }

    public override void Write(Utf8JsonWriter writer, RecentVaultList value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        foreach (var item in value)
        {
            writer.WriteStartObject();
            writer.WriteString(nameof(RecentVault.Path), item.Path);
            writer.WriteString(nameof(RecentVault.OpenedUtc), item.OpenedUtc);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
    }
}
