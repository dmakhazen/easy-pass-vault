using KeePassLib.Security;

namespace EasyPassVault.Core;

public enum ItemKind { Login, Text }
public sealed record FolderInfo(string Id, string Name, string? ParentId = null, string? LeafName = null)
{
    public string DisplayName => LeafName ?? Name;
}

/// <summary>
/// The bytes of one attachment, kept in KeePassLib's memory-protected binary. Opaque to the UI: the bytes are
/// only read for "Save as…" and for an in-memory image preview. Equality is by reference, so an unchanged draft
/// that shares the original's attachments compares equal without reading them.
/// </summary>
public sealed class AttachmentContent
{
    internal ProtectedBinary Binary { get; }
    internal AttachmentContent(ProtectedBinary binary) => Binary = binary;

    /// <summary>Copies the data into protected memory. The caller still owns and should clear <paramref name="data"/>.</summary>
    public static AttachmentContent FromBytes(byte[] data) => new(new ProtectedBinary(true, data));

    public long Length => Binary.Length;

    /// <summary>A fresh plain copy of the bytes. The caller must clear it (CryptographicOperations.ZeroMemory).</summary>
    public byte[] ReadBytes() => Binary.ReadData();
}

/// <summary>A file attached to an entry or a text file, stored as a standard KDBX 4 entry binary.</summary>
public sealed record VaultAttachment(string Name, AttachmentContent Content, DateTime? Added = null)
{
    public long Size => Content.Length;
}

public sealed record VaultItem(string Id, string FolderId, ItemKind Kind, string Title,
    string Login = "", string Password = "", string Site = "", string Text = "")
{
    /// <summary>Last modification time of the entry as stored in the KDBX file (UTC). Null for an item that
    /// was never saved. Read-only metadata: it is not written back and is not part of equality.</summary>
    public DateTime? Modified { get; init; }

    /// <summary>Set on list copies whose password was left out, so the list still knows whether there is one.</summary>
    public bool PasswordSet { get; init; }

    /// <summary>Attached files in their order. Part of the content: adding or removing one edits the item.</summary>
    public IReadOnlyList<VaultAttachment> Attachments { get; init; } = [];

    public int AttachmentCount => Attachments.Count;
    public bool HasPassword => PasswordSet || Password.Length > 0;
    public bool HasContent => Attachments.Count > 0 ||
        new[] { Title, Login, Password, Site, Text }.Any(s => !string.IsNullOrWhiteSpace(s));
    public string DisplayTitle => string.IsNullOrWhiteSpace(Title) ? Kind == ItemKind.Text ? "Новый файл" : "Новая запись" : Title;

    /// <summary>A copy for lists: no password in memory, only the fact that one exists.</summary>
    public VaultItem WithoutPassword() => this with { Password = "", PasswordSet = HasPassword };

    // Equality is the user's content only. Modified and PasswordSet are metadata and must not make
    // an unchanged draft look edited.
    public bool Equals(VaultItem? other) => other is not null && Id == other.Id && FolderId == other.FolderId &&
        Kind == other.Kind && Title == other.Title && Login == other.Login && Password == other.Password &&
        Site == other.Site && Text == other.Text && Attachments.SequenceEqual(other.Attachments);

    public override int GetHashCode() => HashCode.Combine(Id, FolderId, Kind, Title, Login, Password, Site,
        HashCode.Combine(Text, Attachments.Count));
}
public sealed class VaultException(string message, Exception? inner = null) : Exception(message, inner);
