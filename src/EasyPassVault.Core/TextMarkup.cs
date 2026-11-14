namespace EasyPassVault.Core;

public enum MarkupKind { Heading, Separator }

/// <summary>A marked line of a text: its start, its length without the line break, and what it is.</summary>
public readonly record struct MarkupLine(int Start, int Length, MarkupKind Kind);

/// <summary>A plain text replacement: remove <see cref="Length"/> characters at <see cref="Start"/>, insert
/// <see cref="Insert"/>, then put the caret at <see cref="Caret"/> (an index in the new text).</summary>
public readonly record struct TextEdit(int Start, int Length, string Insert, int Caret);

/// <summary>
/// The only two display rules for texts: a line that starts with "# " is a heading, a line that is exactly "---"
/// (spaces or tabs around allowed) is a separator. Everything else, "##" and "**" included, is plain text.
/// The text itself is never changed by displaying it.
/// </summary>
public static class TextMarkup
{
    public const string HeadingPrefix = "# ";
    public const string SeparatorText = "---";

    public static bool IsHeading(ReadOnlySpan<char> line) => line.StartsWith(HeadingPrefix, StringComparison.Ordinal);

    public static bool IsSeparator(ReadOnlySpan<char> line) => line.Trim(" \t").SequenceEqual(SeparatorText);

    public static MarkupKind? Classify(ReadOnlySpan<char> line) =>
        IsHeading(line) ? MarkupKind.Heading : IsSeparator(line) ? MarkupKind.Separator : null;

    /// <summary>All headings and separators. Line breaks are "\r\n", "\n" or "\r", as in a WPF text box.</summary>
    public static IReadOnlyList<MarkupLine> Parse(string text)
    {
        var result = new List<MarkupLine>();
        int start = 0;
        while (start <= text.Length)
        {
            int end = LineEnd(text, start);
            if (Classify(text.AsSpan(start, end - start)) is { } kind) result.Add(new(start, end - start, kind));
            if (end >= text.Length) break;
            start = end + (text[end] == '\r' && end + 1 < text.Length && text[end + 1] == '\n' ? 2 : 1);
        }
        return result;
    }

    /// <summary>Start of the line that holds <paramref name="index"/>.</summary>
    public static int LineStart(string text, int index)
    {
        index = Math.Clamp(index, 0, text.Length);
        while (index > 0 && text[index - 1] is not ('\n' or '\r')) index--;
        return index;
    }

    /// <summary>Index of the line break that ends the line starting at <paramref name="start"/>, or the text length.</summary>
    public static int LineEnd(string text, int start)
    {
        int end = text.IndexOfAny(['\r', '\n'], Math.Clamp(start, 0, text.Length));
        return end < 0 ? text.Length : end;
    }

    /// <summary>"Heading" button: adds "# " at the start of the caret's line, or removes it when it is there.</summary>
    public static TextEdit ToggleHeading(string text, int caret)
    {
        caret = Math.Clamp(caret, 0, text.Length);
        int start = LineStart(text, caret);
        if (IsHeading(text.AsSpan(start, LineEnd(text, start) - start)))
            return new(start, HeadingPrefix.Length, "", Math.Max(start, caret - HeadingPrefix.Length));
        return new(start, 0, HeadingPrefix, caret + HeadingPrefix.Length);
    }

    /// <summary>"Separator" button: a "---" line right after the caret's line; the caret goes to the line after it.</summary>
    public static TextEdit InsertSeparator(string text, int caret)
    {
        string newLine = NewLineOf(text);
        int end = LineEnd(text, LineStart(text, Math.Clamp(caret, 0, text.Length)));
        if (end < text.Length)
        {
            // "line|\r\nnext" → "line\r\n---|\r\nnext": the caret lands at the start of the next line.
            string insert = newLine + SeparatorText;
            int breakLength = text[end] == '\r' && end + 1 < text.Length && text[end + 1] == '\n' ? 2 : 1;
            return new(end, 0, insert, end + insert.Length + breakLength);
        }
        string tail = newLine + SeparatorText + newLine;
        return new(end, 0, tail, end + tail.Length);
    }

    /// <summary>The line break already used in the text; a WPF text box types "\r\n".</summary>
    public static string NewLineOf(string text)
    {
        int index = text.IndexOfAny(['\r', '\n']);
        if (index < 0) return "\r\n";
        if (text[index] == '\n') return "\n";
        return index + 1 < text.Length && text[index + 1] == '\n' ? "\r\n" : "\r";
    }

    public static string Apply(string text, TextEdit edit) =>
        text.Remove(edit.Start, edit.Length).Insert(edit.Start, edit.Insert);
}
