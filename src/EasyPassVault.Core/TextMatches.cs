namespace EasyPassVault.Core;

/// <summary>Where a search query occurs in a text: for the yellow marks and the "next match" button. Display only.</summary>
public static class TextMatches
{
    /// <summary>
    /// Start offsets of <paramref name="query"/> in <paramref name="text"/>, ignoring case, left to right and without
    /// overlaps; every match is exactly as long as the query. An empty query or text has no matches.
    /// </summary>
    public static IReadOnlyList<int> Find(string? text, string? query, int limit = int.MaxValue)
    {
        var found = new List<int>();
        if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(query)) return found;
        for (int index = 0; found.Count < limit; index += query.Length)
        {
            index = text.IndexOf(query, index, StringComparison.OrdinalIgnoreCase);
            if (index < 0) break;
            found.Add(index);
        }
        return found;
    }
}
