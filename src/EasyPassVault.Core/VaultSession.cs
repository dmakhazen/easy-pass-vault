using System.Security.Cryptography;
using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using KeePassLib;
using KeePassLib.Cryptography.KeyDerivation;
using KeePassLib.Keys;
using KeePassLib.Security;
using KeePassLib.Serialization;

namespace EasyPassVault.Core;

public sealed class VaultSession : IDisposable
{
    private const string KindField = "easy-pass-vault:kind";
    private const string SavedAtField = "easy-pass-vault:saved-at";
    private PwDatabase database;
    private byte[] snapshot;
    private byte[] fingerprint;
    private readonly AtomicVaultFile writer;
    private bool disposed;
    public string FilePath { get; private set; }
    public DateTimeOffset? LastSavedAt =>
        DateTimeOffset.TryParseExact(database.CustomData.Get(SavedAtField), "O",
            CultureInfo.InvariantCulture, DateTimeStyles.None, out var savedAt) ? savedAt : null;

    private VaultSession(string path, PwDatabase db, byte[] data, AtomicVaultFile? fileWriter)
    { FilePath = Path.GetFullPath(path); database = db; snapshot = data; fingerprint = SHA256.HashData(data); writer = fileWriter ?? new(); }

    public static VaultSession Create(string path, string password, AtomicVaultFile? writer = null)
    {
        if (string.IsNullOrWhiteSpace(password)) throw new VaultException("Главный пароль не может быть пустым.");
        var db = new PwDatabase();
        try
        {
            db.New(IOConnectionInfo.FromPath(path), MakeKey(password));
            db.RootGroup.Name = "easy-pass-vault";
            db.Compression = PwCompressionAlgorithm.None; // Avoid decompression bombs and unnecessary complexity.
            db.KdfParameters = new Argon2Kdf(Argon2Type.ID).GetDefaultParameters();
            db.KdfParameters.SetUInt64(Argon2Kdf.ParamMemory, 64UL * 1024 * 1024);
            db.KdfParameters.SetUInt64(Argon2Kdf.ParamIterations, 3);
            db.KdfParameters.SetUInt32(Argon2Kdf.ParamParallelism, 2);
            foreach (string name in new[] { "Личное", "Работа", "Заметки" })
                db.RootGroup.AddGroup(new PwGroup(true, true, name, PwIcon.Folder), true);
            byte[] bytes = Encode(db);
            KdbxGuard.Validate(bytes);
            (writer ?? new()).Write(path, bytes, null);
            return new(path, db, bytes, writer);
        }
        catch { db.Close(); throw; }
    }

    public static VaultSession Open(string path, string password, AtomicVaultFile? writer = null)
    {
        byte[] bytes = AtomicVaultFile.Read(path);
        KdbxGuard.Validate(bytes);
        return new(path, Decode(bytes, MakeKey(password)), bytes, writer);
    }

    private static CompositeKey MakeKey(string password)
    { var key = new CompositeKey(); key.AddUserKey(new KcpPassword(password)); return key; }

    private static PwDatabase Decode(byte[] bytes, CompositeKey key)
    {
        var db = new PwDatabase { MasterKey = key };
        try
        {
            using var stream = new MemoryStream(bytes, writable: false);
            var file = new KdbxFile(db) { RepairMode = false };
            file.Load(stream, KdbxFormat.Default, null);
            return db;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            db.Close();
            throw new VaultException("Не удалось открыть хранилище: неверный главный пароль либо файл повреждён или изменён.", ex);
        }
    }

    private static byte[] Encode(PwDatabase db)
    {
        db.CustomData.Set(SavedAtField, DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        using var stream = new MemoryStream();
        new KdbxFile(db).Save(stream, db.RootGroup, KdbxFormat.Default, null);
        return stream.ToArray();
    }

    public IReadOnlyList<FolderInfo> Folders
    {
        get
        {
            ThrowIfDisposed();
            var result = new List<FolderInfo>();
            void Visit(PwGroup group, string prefix)
            {
                string name = prefix.Length == 0 ? group.Name : prefix + " / " + group.Name;
                result.Add(new(group.Uuid.ToHexString(), name, group.ParentGroup.Uuid.ToHexString(), group.Name));
                foreach (var child in group.Groups) Visit(child, name);
            }
            // Root entries from other clients remain accessible as well.
            result.Add(new(database.RootGroup.Uuid.ToHexString(), "Корень хранилища"));
            foreach (var child in database.RootGroup.Groups) Visit(child, "");
            return result;
        }
    }

    public IReadOnlyList<VaultItem> Items
    {
        get
        {
            ThrowIfDisposed();
            return database.RootGroup.GetEntries(true).Select(e => ReadItem(e, includePassword: false)).ToList();
        }
    }

    public VaultItem GetItem(string id)
    {
        ThrowIfDisposed();
        var entry = database.RootGroup.GetEntries(true).First(e => e.Uuid.ToHexString() == id);
        return ReadItem(entry, includePassword: true);
    }

    private static VaultItem ReadItem(PwEntry entry, bool includePassword) =>
        new(entry.Uuid.ToHexString(), entry.ParentGroup.Uuid.ToHexString(),
            entry.Strings.ReadSafe(KindField) == "text" ? ItemKind.Text : ItemKind.Login,
            entry.Strings.ReadSafe(PwDefs.TitleField), entry.Strings.ReadSafe(PwDefs.UserNameField),
            includePassword ? entry.Strings.ReadSafe(PwDefs.PasswordField) : "",
            entry.Strings.ReadSafe(PwDefs.UrlField), entry.Strings.ReadSafe(PwDefs.NotesField));

    public void ExportText(string path)
    {
        ThrowIfDisposed();
        path = Path.GetFullPath(path);
        if (!Path.GetExtension(path).Equals(".txt", StringComparison.OrdinalIgnoreCase))
            throw new VaultException("Выберите текстовый файл с расширением .txt.");
        var folders = Folders.ToDictionary(folder => folder.Id, folder => folder.Name);
        var items = database.RootGroup.GetEntries(true).Select(entry =>
        {
            var item = ReadItem(entry, includePassword: true);
            return new { Folder = folders[item.FolderId], Kind = item.Kind.ToString(), item.Title, item.Login, item.Password, item.Site, item.Text };
        });
        var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        try
        {
            using (output)
                JsonSerializer.Serialize(output, items, new JsonSerializerOptions
                {
                    WriteIndented = true,
                    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                });
        }
        catch
        {
            File.Delete(path);
            throw;
        }
    }

    public void SaveItem(VaultItem item) => SaveItems([item]);

    /// <summary>Encrypts and writes the changes into the currently open file.</summary>
    public void SaveItems(IReadOnlyList<VaultItem> items) => Commit(ItemsChange(items), newVersion: false);

    /// <summary>
    /// Encrypts the changes into a brand-new file {name}_{date}_v{N}.kdbx next to the current one and makes it active.
    /// The current file is left untouched.
    /// </summary>
    public string SaveNewVersion(IReadOnlyList<VaultItem> items)
    {
        Commit(ItemsChange(items), newVersion: true);
        return FilePath;
    }

    private static Action<PwDatabase> ItemsChange(IReadOnlyList<VaultItem> items)
    {
        if (items.Any(item => !item.HasContent))
            throw new VaultException("Заполните хотя бы одно поле.");
        return db => { foreach (var item in items) WriteItem(db, item); };
    }

    /// <summary>The file the next <see cref="SaveNewVersion"/> would create today.</summary>
    public string NextVersionPath
    {
        get { ThrowIfDisposed(); return VaultVersions.NextPath(FilePath, DateTime.Today); }
    }

    /// <summary>A newer file of the same series, if one exists.</summary>
    public string? NewerVersionPath
    {
        get { ThrowIfDisposed(); return VaultVersions.FindNewer(FilePath); }
    }

    private static void WriteItem(PwDatabase db, VaultItem item)
    {
            var group = FindGroup(db, item.FolderId);
            var entry = db.RootGroup.GetEntries(true).FirstOrDefault(e => e.Uuid.ToHexString() == item.Id);
            if (entry is null)
            {
                entry = new PwEntry(false, true) { Uuid = new PwUuid(Convert.FromHexString(item.Id)) };
                group.AddEntry(entry, true);
            }
            else if (entry.ParentGroup != group)
            { entry.ParentGroup.Entries.Remove(entry); group.AddEntry(entry, true); }
            entry.Strings.Set(PwDefs.TitleField, new ProtectedString(true, item.Title));
            entry.Strings.Set(PwDefs.UserNameField, new ProtectedString(true, item.Login));
            entry.Strings.Set(PwDefs.PasswordField, new ProtectedString(true, item.Password));
            entry.Strings.Set(PwDefs.UrlField, new ProtectedString(true, item.Site));
            entry.Strings.Set(PwDefs.NotesField, new ProtectedString(true, item.Text));
            entry.Strings.Set(KindField, new ProtectedString(false, item.Kind == ItemKind.Text ? "text" : "login"));
            entry.Touch(true);
    }

    public void DeleteItem(string id) => Commit(db =>
    {
        var entry = db.RootGroup.GetEntries(true).First(e => e.Uuid.ToHexString() == id);
        entry.ParentGroup.Entries.Remove(entry);
        db.DeletedObjects.Add(new PwDeletedObject(entry.Uuid, DateTime.UtcNow));
    });

    public void AddFolder(string name, string? parentId = null)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new VaultException("Введите название папки.");
        Commit(db =>
        {
            var parent = parentId is null ? db.RootGroup : FindGroup(db, parentId);
            parent.AddGroup(new PwGroup(true, true, name.Trim(), PwIcon.Folder), true);
        });
    }

    public void RenameFolder(string id, string name)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new VaultException("Введите название папки.");
        Commit(db => { var group = FindGroup(db, id); group.Name = name.Trim(); group.Touch(true); });
    }

    public void MoveFolder(string id, string destinationId) => Commit(db =>
    {
        var folder = FindGroup(db, id);
        var destination = FindGroup(db, destinationId);
        if (folder == db.RootGroup || folder == destination || destination.IsContainedIn(folder))
            throw new VaultException("Нельзя переместить корень, папку в себя или в её подпапку.");

        var previousParent = folder.ParentGroup;
        previousParent.Groups.Remove(folder);
        destination.AddGroup(folder, true, true);
        folder.PreviousParentGroup = previousParent.Uuid;
    });

    public bool IsFolderEmpty(string id)
    {
        ThrowIfDisposed();
        return IsFolderEmpty(FindGroup(database, id));
    }

    private static bool IsFolderEmpty(PwGroup group) => group.Entries.UCount == 0 && group.Groups.UCount == 0;

    public void DeleteFolder(string id) => Commit(db =>
    {
        var group = FindGroup(db, id);
        if (group == db.RootGroup) throw new VaultException("Корневую папку удалить нельзя.");
        if (!IsFolderEmpty(group)) throw new VaultException("Сначала удалите или переместите содержимое папки.");
        group.ParentGroup.Groups.Remove(group);
        db.DeletedObjects.Add(new PwDeletedObject(group.Uuid, DateTime.UtcNow));
    });

    private static PwGroup FindGroup(PwDatabase db, string id) =>
        db.RootGroup.Uuid.ToHexString() == id ? db.RootGroup :
        db.RootGroup.GetGroups(true).FirstOrDefault(g => g.Uuid.ToHexString() == id) ?? throw new VaultException("Папка не найдена.");

    private void Commit(Action<PwDatabase> change, bool newVersion = false)
    {
        ThrowIfDisposed();
        // Work on a separate decrypted database. IO failure never commits UI edits in memory.
        PwDatabase candidate = Decode(snapshot, database.MasterKey);
        try
        {
            change(candidate);
            byte[] bytes = Encode(candidate);
            KdbxGuard.Validate(bytes);
            string destination = FilePath;
            if (newVersion)
            {
                // A new file {name}_{date}_v{N}: nothing existing is overwritten, so an older
                // version may also be saved this way to continue the series from it.
                DateTime date = DateTime.Today;
                destination = writer.WriteNew(VaultVersions.LockPath(FilePath),
                    () => VaultVersions.NextPath(FilePath, date), bytes);
            }
            else writer.Write(FilePath, bytes, fingerprint);
            database.Close(); database = candidate;
            FilePath = destination;
            CryptographicOperations.ZeroMemory(snapshot);
            snapshot = bytes; fingerprint = SHA256.HashData(bytes);
        }
        catch { candidate.Close(); throw; }
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(disposed, this);
    public void Dispose()
    {
        if (disposed) return;
        disposed = true; database.Close(); CryptographicOperations.ZeroMemory(snapshot);
        CryptographicOperations.ZeroMemory(fingerprint);
    }
}
