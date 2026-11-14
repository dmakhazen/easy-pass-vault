using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace EasyPassVault.Core;

public enum PreviewKind { None, Image, Json, Text }

public enum TextPreviewWarning { None, InvalidJson, NotUtf8 }

/// <summary>What the text preview shows: the text, and why it may differ from what the user expects.</summary>
public sealed record TextPreviewResult(string Text, bool Formatted, TextPreviewWarning Warning, long? ErrorLine = null);

/// <summary>
/// Text and JSON attachments shown read-only from memory. Nothing is written anywhere; the attachment itself is
/// never changed — the caller passes a copy of the bytes, which is cleared here.
/// </summary>
public static class TextPreview
{
    /// <summary>Above this size the app asks before building a preview: it may be slow.</summary>
    public const long AskAboveBytes = 20L * 1024 * 1024;

    private static readonly HashSet<string> PlainText = new(StringComparer.OrdinalIgnoreCase) { ".txt", ".log", ".csv", ".xml", ".md" };
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
    private static readonly UTF8Encoding LenientUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: false);

    public static PreviewKind KindOf(string name)
    {
        string extension = Path.GetExtension(name);
        return VaultAttachments.KindOf(name) == AttachmentKind.Image ? PreviewKind.Image
            : extension.Equals(".json", StringComparison.OrdinalIgnoreCase) ? PreviewKind.Json
            : PlainText.Contains(extension) ? PreviewKind.Text
            : PreviewKind.None;
    }

    /// <summary>
    /// Decodes UTF-8 (with or without BOM). Bytes that are not UTF-8 become U+FFFD with a warning.
    /// JSON is re-indented; when it does not parse, the text is shown as is with the error line.
    /// </summary>
    public static TextPreviewResult Build(byte[] bytes, bool json)
    {
        try
        {
            ReadOnlySpan<byte> data = bytes;
            if (data.StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF])) data = data[3..];
            string text;
            bool utf8 = true;
            try { text = StrictUtf8.GetString(data); }
            catch (DecoderFallbackException) { text = LenientUtf8.GetString(data); utf8 = false; }
            var result = FromText(text, json);
            return utf8 || result.Warning != TextPreviewWarning.None ? result : result with { Warning = TextPreviewWarning.NotUtf8 };
        }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }

    /// <summary>The same view of a text already in memory (a ".json" text file of the vault). The text is not changed.</summary>
    public static TextPreviewResult FromText(string text, bool json)
    {
        if (!json) return new(text, false, TextPreviewWarning.None);
        try
        {
            using var document = JsonDocument.Parse(text);
            using var output = new MemoryStream();
            using (var writer = new Utf8JsonWriter(output, new JsonWriterOptions
                   { Indented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
                document.WriteTo(writer);
            string formatted = Encoding.UTF8.GetString(output.GetBuffer(), 0, (int)output.Length);
            CryptographicOperations.ZeroMemory(output.GetBuffer());
            return new(formatted, true, TextPreviewWarning.None);
        }
        catch (JsonException ex)
        {
            return new(text, false, TextPreviewWarning.InvalidJson, ex.LineNumber is { } line ? line + 1 : null);
        }
    }
}
