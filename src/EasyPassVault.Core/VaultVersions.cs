using System.Globalization;
using System.Text.RegularExpressions;

namespace EasyPassVault.Core;

// Every write creates {name}_{yyyy-MM-dd}_v{N}.kdbx next to the current file.
// N is continuous for the series and never resets by date. Older versions are never modified.
public static partial class VaultVersions
{
    public const string NewerVersionExists =
        "В папке есть более новая версия этого хранилища. Изменения не записаны. Откройте последнюю версию.";
    public const string SeriesExists =
        "В папке уже есть хранилище с таким названием. Выберите другое название.";

    public readonly record struct VersionName(string BaseName, int Number);

    [GeneratedRegex(@"^(?<base>.+)_(?<date>\d{4}-\d{2}-\d{2})_v(?<n>\d{1,9})$", RegexOptions.CultureInvariant)]
    private static partial Regex Versioned();

    // Names from the former "Save dated version" button: {name}-{yyyy-MM-dd}.
    [GeneratedRegex(@"^(?<base>.+)-(?<date>\d{4}-\d{2}-\d{2})$", RegexOptions.CultureInvariant)]
    private static partial Regex LegacyDated();

    public static VersionName Parse(string path)
    {
        string name = Path.GetFileNameWithoutExtension(path);
        var match = Versioned().Match(name);
        if (match.Success && IsDate(match.Groups["date"].Value) &&
            int.TryParse(match.Groups["n"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int number) && number > 0)
            return new(match.Groups["base"].Value, number);
        match = LegacyDated().Match(name);
        if (match.Success && IsDate(match.Groups["date"].Value))
            return new(match.Groups["base"].Value, 0);
        return new(name, 0);
    }

    private static bool IsDate(string value) =>
        DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _);

    public static string PathFor(string directory, string baseName, int number, DateTime date) =>
        Path.Combine(directory, string.Create(CultureInfo.InvariantCulture, $"{baseName}_{date:yyyy-MM-dd}_v{number}.kdbx"));

    public static string LockPath(string vaultPath) =>
        Path.Combine(DirectoryOf(vaultPath), Parse(vaultPath).BaseName + ".epv-lock");

    /// <summary>Path of the highest version in the series, or null when the series has no numbered files.</summary>
    public static string? FindLatest(string vaultPath)
    {
        string baseName = Parse(vaultPath).BaseName;
        return Directory.EnumerateFiles(DirectoryOf(vaultPath), "*.kdbx")
            .Where(file => Path.GetExtension(file).Equals(".kdbx", StringComparison.OrdinalIgnoreCase))
            .Select(file => (File: file, Name: Parse(file)))
            .Where(x => x.Name.Number > 0 && x.Name.BaseName.Equals(baseName, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(x => x.Name.Number)
            .Select(x => x.File)
            .FirstOrDefault();
    }

    /// <summary>Path of a version newer than <paramref name="vaultPath"/>, or null when it is the latest.</summary>
    public static string? FindNewer(string vaultPath)
    {
        string? latest = FindLatest(vaultPath);
        return latest is not null && Parse(latest).Number > Parse(vaultPath).Number ? latest : null;
    }

    public static string NextPath(string vaultPath, DateTime date)
    {
        var current = Parse(vaultPath);
        int latest = FindLatest(vaultPath) is { } file ? Parse(file).Number : 0;
        int next = checked(Math.Max(latest, current.Number) + 1);
        return PathFor(DirectoryOf(vaultPath), current.BaseName, next, date);
    }

    /// <summary>First file of a new vault series chosen by the user, e.g. Personal.kdbx → Personal_2026-11-05_v1.kdbx.</summary>
    public static string FirstPath(string chosenPath, DateTime date)
    {
        string full = Path.GetFullPath(chosenPath);
        if (FindLatest(full) is not null) throw new VaultException(SeriesExists);
        return PathFor(DirectoryOf(full), Parse(full).BaseName, 1, date);
    }

    private static string DirectoryOf(string path) => Path.GetDirectoryName(Path.GetFullPath(path))!;
}
