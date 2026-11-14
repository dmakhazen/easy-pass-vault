using System.Globalization;

namespace EasyPassVault.Core;

/// <summary>Quick search over all folders and both kinds of items. Display only: nothing is written.</summary>
public static class VaultSearch
{
    /// <summary>
    /// Items whose title, login, site, text or attachment name contains <paramref name="query"/>, ignoring case by the rules of
    /// <paramref name="culture"/>. The password and the contents of attachments never take part.
    /// Title matches come first, then the rest; inside each group the order is by title.
    /// An empty query finds everything.
    /// </summary>
    public static IReadOnlyList<VaultItem> Find(IEnumerable<VaultItem> items, string? query, CultureInfo culture,
        Func<VaultItem, string>? title = null)
    {
        title ??= item => item.DisplayTitle;
        string needle = (query ?? "").Trim();
        var compare = culture.CompareInfo;
        const CompareOptions options = CompareOptions.IgnoreCase | CompareOptions.IgnoreWidth;
        bool Contains(string text) => needle.Length == 0 || compare.IndexOf(text, needle, options) >= 0;

        var names = VaultSorting.NameComparer(culture);
        return items
            .Select(item => (Item: item, InTitle: Contains(item.Title),
                Found: Contains(item.Title) || Contains(item.Login) || Contains(item.Site) || Contains(item.Text) ||
                    item.Attachments.Any(attachment => Contains(attachment.Name))))
            .Where(match => match.Found)
            .OrderByDescending(match => match.InTitle)
            .ThenBy(match => title(match.Item), names)
            .Select(match => match.Item)
            .ToList();
    }
}
