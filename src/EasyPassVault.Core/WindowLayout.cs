using System.Text.Json;

namespace EasyPassVault.Core;

/// <summary>
/// Window geometry, view mode, list order and the recently opened vault files remembered between runs.
/// Never vault contents or secrets: of a vault only its full path and the time it was opened are kept.
/// Units are WPF device-independent pixels.
/// </summary>
public sealed record WindowLayout
{
    public const string FileName = "easy-pass-vault.window.json";
    public const double MinWideWidth = 950, MinCompactWidth = 420, MinHeight = 520;
    public const double MinFoldersWidth = 160, MinListWidth = 240, MinEditorWidth = 380;

    public double? Left { get; init; }
    public double? Top { get; init; }
    public double Width { get; init; } = 1180;
    public double Height { get; init; } = 760;
    public double CompactWidth { get; init; } = 460;
    public double CompactHeight { get; init; } = 760;
    public bool Maximized { get; init; }
    public bool Compact { get; init; }
    public double FoldersWidth { get; init; } = 220;
    public double ListWidth { get; init; } = 310;
    public FolderSort FolderSort { get; init; }
    public ItemSort ItemSort { get; init; }
    public RecentVaultList RecentVaults { get; init; } = RecentVaultList.Empty;
    /// <summary>Password generator length and character sets; the generated password is never stored.</summary>
    public PasswordOptions Generator { get; init; } = new();

    public readonly record struct Area(double Left, double Top, double Width, double Height);

    /// <summary>
    /// Returns a layout that is safe to apply: finite sizes within the screen and a position whose title bar
    /// is reachable. An unreachable or missing position becomes null, which means "center the window".
    /// </summary>
    public WindowLayout Normalize(Area screen)
    {
        double wide = Clamp(Width, MinWideWidth, screen.Width, 1180);
        double height = Clamp(Height, MinHeight, screen.Height, 760);
        double compact = Clamp(CompactWidth, MinCompactWidth, screen.Width, 460);
        double compactHeight = Clamp(CompactHeight, MinHeight, screen.Height, 760);
        var (folders, list) = FitColumns(wide, FoldersWidth, ListWidth);

        double? left = Left, top = Top;
        double current = Compact ? compact : wide;
        // At least 120 px of the title bar must stay inside the screens.
        bool reachable = left is { } x && top is { } y && double.IsFinite(x) && double.IsFinite(y) &&
            Math.Min(x + current, screen.Left + screen.Width) - Math.Max(x, screen.Left) >= 120 &&
            y >= screen.Top - 8 && y <= screen.Top + screen.Height - 48;
        if (!reachable) left = top = null;

        return this with
        {
            Left = left, Top = top, Width = wide, Height = height, CompactWidth = compact,
            CompactHeight = compactHeight, FoldersWidth = folders, ListWidth = list,
            // A number outside the enum (a hand-edited or newer file) means the default order.
            FolderSort = Enum.IsDefined(FolderSort) ? FolderSort : default,
            ItemSort = Enum.IsDefined(ItemSort) ? ItemSort : default,
            RecentVaults = (RecentVaults ?? RecentVaultList.Empty).Normalize(),
            Generator = (Generator ?? new()).Normalize()
        };
    }

    /// <summary>Shrinks the folder and list areas so the editor keeps its minimum width.</summary>
    public static (double Folders, double List) FitColumns(double total, double folders, double list)
    {
        folders = Clamp(folders, MinFoldersWidth, double.MaxValue, 220);
        list = Clamp(list, MinListWidth, double.MaxValue, 310);
        double excess = folders + list + MinEditorWidth - total;
        if (!(excess > 0)) return (folders, list);
        double fromList = Math.Min(excess, list - MinListWidth);
        list -= fromList;
        folders = Math.Max(MinFoldersWidth, folders - (excess - fromList));
        return (folders, list);
    }

    private static double Clamp(double value, double min, double max, double fallback) =>
        !double.IsFinite(value) ? Math.Max(min, Math.Min(fallback, Math.Max(min, max)))
        : Math.Max(min, Math.Min(value, Math.Max(min, max)));
}

/// <summary>Reads and writes <see cref="WindowLayout"/> as a small JSON file. Never throws: a missing,
/// damaged or unwritable file only means the default layout.</summary>
public static class WindowLayoutStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    private const int MaxFileBytes = 16 * 1024;

    public static WindowLayout Load(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length > MaxFileBytes) return new();
            return JsonSerializer.Deserialize<WindowLayout>(File.ReadAllText(path), Options) ?? new();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException
            or NotSupportedException or ArgumentException or System.Security.SecurityException)
        {
            return new();
        }
    }

    public static bool Save(string path, WindowLayout layout)
    {
        try
        {
            File.WriteAllText(path, JsonSerializer.Serialize(layout, Options));
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException
            or ArgumentException or System.Security.SecurityException)
        {
            return false;
        }
    }
}
