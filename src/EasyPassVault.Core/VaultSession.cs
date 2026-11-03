using System.Security.Cryptography;
using KeePassLib;
using KeePassLib.Cryptography.KeyDerivation;
using KeePassLib.Keys;
using KeePassLib.Security;
using KeePassLib.Serialization;

namespace EasyPassVault.Core;

public sealed class VaultSession : IDisposable
{
    private const string KindField = "easy-pass-vault:kind";
    private PwDatabase database;
    private byte[] snapshot;
    private byte[] fingerprint;
    private readonly AtomicVaultFile writer;
    private bool disposed;
    public string FilePath { get; }

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
                result.Add(new(group.Uuid.ToHexString(), name));
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
            return database.RootGroup.GetEntries(true).Select(e => new VaultItem(e.Uuid.ToHexString(),
                e.ParentGroup.Uuid.ToHexString(), e.Strings.ReadSafe(KindField) == "text" ? ItemKind.Text : ItemKind.Login,
                e.Strings.ReadSafe(PwDefs.TitleField), e.Strings.ReadSafe(PwDefs.UserNameField),
                "", e.Strings.ReadSafe(PwDefs.UrlField), e.Strings.ReadSafe(PwDefs.NotesField))).ToList();
        }
    }

    public VaultItem GetItem(string id)
    {
        ThrowIfDisposed();
        var item = Items.First(x => x.Id == id);
        var entry = database.RootGroup.GetEntries(true).First(e => e.Uuid.ToHexString() == id);
        return item with { Password = entry.Strings.ReadSafe(PwDefs.PasswordField) };
    }

    public void SaveItem(VaultItem item)
    {
        if (!item.HasContent) throw new VaultException("Заполните хотя бы одно поле.");
        Commit(db =>
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
        });
    }

    public void DeleteItem(string id) => Commit(db =>
    {
        var entry = db.RootGroup.GetEntries(true).First(e => e.Uuid.ToHexString() == id);
        entry.ParentGroup.Entries.Remove(entry);
        db.DeletedObjects.Add(new PwDeletedObject(entry.Uuid, DateTime.UtcNow));
    });

    public void AddFolder(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new VaultException("Введите название папки.");
        Commit(db => db.RootGroup.AddGroup(new PwGroup(true, true, name.Trim(), PwIcon.Folder), true));
    }

    public void RenameFolder(string id, string name)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new VaultException("Введите название папки.");
        Commit(db => { var group = FindGroup(db, id); group.Name = name.Trim(); group.Touch(true); });
    }

    public void DeleteFolder(string id) => Commit(db =>
    {
        var group = FindGroup(db, id);
        if (group == db.RootGroup) throw new VaultException("Корневую папку удалить нельзя.");
        if (group.Entries.UCount != 0 || group.Groups.UCount != 0) throw new VaultException("Сначала удалите или переместите содержимое папки.");
        group.ParentGroup.Groups.Remove(group);
        db.DeletedObjects.Add(new PwDeletedObject(group.Uuid, DateTime.UtcNow));
    });

    private static PwGroup FindGroup(PwDatabase db, string id) =>
        db.RootGroup.Uuid.ToHexString() == id ? db.RootGroup :
        db.RootGroup.GetGroups(true).FirstOrDefault(g => g.Uuid.ToHexString() == id) ?? throw new VaultException("Папка не найдена.");

    private void Commit(Action<PwDatabase> change)
    {
        ThrowIfDisposed();
        // Work on a separate decrypted database. IO failure never commits UI edits in memory.
        PwDatabase candidate = Decode(snapshot, database.MasterKey);
        try
        {
            change(candidate);
            byte[] bytes = Encode(candidate);
            KdbxGuard.Validate(bytes);
            writer.Write(FilePath, bytes, fingerprint);
            database.Close(); database = candidate;
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
