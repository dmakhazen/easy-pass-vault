using System.Security.Cryptography;

namespace EasyPassVault.Core;

public enum AttachmentKind { Image, Document, Archive, Other }

public enum SizeCheck { Fine, Warn, TooLarge }

/// <summary>
/// Attachments: names, kinds, size limits and the only two places that touch the disk for them — reading a file
/// the user picked and writing a copy where the user asked. Nothing decrypted is ever written anywhere else.
/// </summary>
public static class VaultAttachments
{
    /// <summary>Above this estimated vault size adding asks first: opening and every save get slow.</summary>
    public const long WarnBytes = 100L * 1024 * 1024;
    public const long MaxBytes = AtomicVaultFile.MaxFileBytes;

    // Only formats WPF decodes itself. WEBP needs the Windows codec; without it the preview reports an error.
    private static readonly HashSet<string> Images = new(StringComparer.OrdinalIgnoreCase) { ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp" };
    private static readonly HashSet<string> Documents = new(StringComparer.OrdinalIgnoreCase)
        { ".pdf", ".doc", ".docx", ".odt", ".rtf", ".txt", ".md", ".xls", ".xlsx", ".ods", ".csv", ".ppt", ".pptx", ".odp", ".xml", ".json", ".html", ".htm" };
    private static readonly HashSet<string> Archives = new(StringComparer.OrdinalIgnoreCase)
        { ".zip", ".7z", ".rar", ".tar", ".gz", ".tgz", ".bz2", ".xz", ".zst" };

    public static AttachmentKind KindOf(string name)
    {
        string extension = Path.GetExtension(name);
        return Images.Contains(extension) ? AttachmentKind.Image
            : Documents.Contains(extension) ? AttachmentKind.Document
            : Archives.Contains(extension) ? AttachmentKind.Archive
            : AttachmentKind.Other;
    }

    /// <summary>Images, JSON and plain text (.txt, .log, .csv, .xml, .md) can be viewed from memory.</summary>
    public static bool CanPreview(string name) => TextPreview.KindOf(name) != PreviewKind.None;

    /// <summary>Upper-case extension for the square badge: JPG, PDF, ZIP; "FILE" without one.</summary>
    public static string Badge(string name)
    {
        string extension = Path.GetExtension(name).TrimStart('.');
        if (extension.Length == 0) return "FILE";
        extension = extension.ToUpperInvariant();
        return extension == "JPEG" ? "JPG" : extension.Length > 4 ? extension[..4] : extension;
    }

    /// <summary>"name.ext", then "name (2).ext", "name (3).ext"… — never one of <paramref name="taken"/> (any case).</summary>
    public static string UniqueName(IEnumerable<string> taken, string name)
    {
        var used = new HashSet<string>(taken, StringComparer.OrdinalIgnoreCase);
        if (!used.Contains(name)) return name;
        string stem = Path.GetFileNameWithoutExtension(name), extension = Path.GetExtension(name);
        for (int n = 2; ; n++)
        {
            string candidate = $"{stem} ({n}){extension}";
            if (!used.Contains(candidate)) return candidate;
        }
    }

    /// <summary>
    /// The one order of attachments everywhere: KDBX keeps entry binaries in a dictionary sorted by name, so a draft
    /// is kept in the same order and an unchanged list compares equal after reopening.
    /// </summary>
    public static IReadOnlyList<VaultAttachment> Sorted(IEnumerable<VaultAttachment> attachments) =>
        attachments.OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase).ThenBy(a => a.Name, StringComparer.Ordinal).ToList();

    /// <summary>Total bytes of the attachments of an item.</summary>
    public static long TotalSize(IEnumerable<VaultAttachment> attachments) => attachments.Sum(attachment => attachment.Size);

    /// <summary>
    /// Approximate size of the vault file once the pending drafts are saved: the file on disk plus attachments the
    /// drafts added, minus those they removed. KDBX 4 stores binaries unencoded, so the estimate is close.
    /// </summary>
    public static long EstimateSize(long fileSize, IEnumerable<(VaultItem Item, VaultItem? Original)> pending)
    {
        long size = fileSize;
        foreach (var (item, original) in pending)
        {
            var before = original?.Attachments ?? [];
            size += item.Attachments.Where(a => !before.Any(b => ReferenceEquals(a.Content, b.Content))).Sum(a => a.Size);
            size -= before.Where(b => !item.Attachments.Any(a => ReferenceEquals(a.Content, b.Content))).Sum(b => b.Size);
        }
        return Math.Max(0, size);
    }

    public static SizeCheck Check(long estimatedSize) =>
        estimatedSize > MaxBytes ? SizeCheck.TooLarge : estimatedSize > WarnBytes ? SizeCheck.Warn : SizeCheck.Fine;

    /// <summary>
    /// Reads a file the user picked into protected memory under <paramref name="name"/>. The size is checked
    /// before any buffer is allocated; the plain buffer is cleared afterwards.
    /// </summary>
    public static VaultAttachment Load(string path, string name, DateTime addedUtc)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > MaxBytes)
            throw new VaultException("Файл больше 512 МБ и не поместится в хранилище. Вложение не добавлено.");
        var bytes = new byte[stream.Length];
        try
        {
            stream.ReadExactly(bytes);
            return new VaultAttachment(name, AttachmentContent.FromBytes(bytes), addedUtc);
        }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }

    /// <summary>Writes a decrypted copy only to the path the user chose in the save dialog.</summary>
    public static void SaveAs(VaultAttachment attachment, string path)
    {
        byte[] bytes = attachment.Content.ReadBytes();
        try
        {
            using var output = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
            output.Write(bytes);
        }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }
}
