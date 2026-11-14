using System.Globalization;

namespace EasyPassVault.Core;

public enum FolderSort { NameAscending, NameDescending }
public enum ItemSort { NameAscending, NameDescending, ModifiedNewest, ModifiedOldest }

/// <summary>Display order only: nothing here changes the vault or is written to it.</summary>
public static class VaultSorting
{
    /// <summary>Compares names by the rules of the given language, ignoring case.</summary>
    public static IComparer<string> NameComparer(CultureInfo culture) =>
        Comparer<string>.Create((a, b) =>
        {
            int result = culture.CompareInfo.Compare(a, b, CompareOptions.IgnoreCase);
            // Names that differ only by case keep a stable, repeatable order.
            return result != 0 ? result : string.CompareOrdinal(a, b);
        });

    /// <summary>
    /// Sorts folders by name on every level of the tree separately. A parent always comes before its
    /// children, so the result can be used to build the tree in one pass.
    /// </summary>
    public static IReadOnlyList<FolderInfo> SortFolders(IReadOnlyList<FolderInfo> folders, FolderSort sort, CultureInfo culture)
    {
        var names = NameComparer(culture);
        var known = folders.Select(folder => folder.Id).ToHashSet();
        var children = folders.ToLookup(folder => folder.ParentId is { } parent && known.Contains(parent) ? parent : "");
        var result = new List<FolderInfo>(folders.Count);
        void Visit(string parentId)
        {
            var level = sort == FolderSort.NameDescending
                ? children[parentId].OrderByDescending(folder => folder.DisplayName, names)
                : children[parentId].OrderBy(folder => folder.DisplayName, names);
            foreach (var folder in level)
            {
                result.Add(folder);
                Visit(folder.Id);
            }
        }
        Visit("");
        return result;
    }

    /// <summary>
    /// Sorts entries and files. <paramref name="title"/> gives the name shown in the list (an untitled draft
    /// has a placeholder there). Items without a modification time were never saved: they count as the newest.
    /// </summary>
    public static IReadOnlyList<VaultItem> SortItems(IEnumerable<VaultItem> items, ItemSort sort, CultureInfo culture,
        Func<VaultItem, string>? title = null)
    {
        title ??= item => item.DisplayTitle;
        var names = NameComparer(culture);
        return (sort switch
        {
            ItemSort.NameDescending => items.OrderByDescending(title, names),
            ItemSort.ModifiedNewest => items.OrderByDescending(item => item.Modified ?? DateTime.MaxValue).ThenBy(title, names),
            ItemSort.ModifiedOldest => items.OrderBy(item => item.Modified ?? DateTime.MaxValue).ThenBy(title, names),
            _ => items.OrderBy(title, names)
        }).ToList();
    }
}

/// <summary>Decides whether the text of the "Site" field may be handed to the default browser.</summary>
public static class SiteAddress
{
    /// <summary>True only for a well-formed absolute http:// or https:// address with a host.</summary>
    public static bool TryGetWebUri(string? text, out Uri uri)
    {
        uri = null!;
        string value = (text ?? "").Trim();
        if (!value.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !value.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return false;
        if (value.Any(char.IsWhiteSpace) || value.Any(char.IsControl)) return false;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var parsed) ||
            (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps) || parsed.Host.Length == 0)
            return false;
        uri = parsed;
        return true;
    }
}
