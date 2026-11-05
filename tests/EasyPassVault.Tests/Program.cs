using System.Text;
using System.Diagnostics;
using System.Reflection;
using EasyPassVault.Core;

// Dependency-free integration runner. Every file/password here is explicitly synthetic.
string root = Path.GetFullPath(Path.Combine(".artifacts", "tests", Guid.NewGuid().ToString("N")));
Directory.CreateDirectory(root);
const string password = "Fictional-test-password-482!";
if (args.Length == 2 && args[0] == "--open-in-child")
{
    using var reopened = VaultSession.Open(args[1], password);
    if (reopened.Items.Count != 1 || reopened.Items[0].Title != "SYNTHETIC TITLE") Environment.ExitCode = 1;
    return;
}
int passed = 0;
void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
void Reject(Action action, string message)
{
    try { action(); } catch (Exception ex) when (ex is VaultException or IOException) { return; }
    throw new Exception(message);
}
void Test(string name, Action action)
{
    action(); passed++; Console.WriteLine("PASS " + name);
}
string path = Path.Combine(root, "synthetic.kdbx");
string loginId = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(16));
string textId = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(16));
try
{
    using var session = VaultSession.Create(path, password);
    string folder = session.Folders[1].Id;
    var login = new VaultItem(loginId, folder, ItemKind.Login, "SYNTHETIC TITLE", "demo@example.com", "Fictional-secret-761!", "https://example.com", "Synthetic note");
    var text = new VaultItem(textId, folder, ItemKind.Text, "Тестовый файл.txt", Text: "Вымышленный текст\nВторая строка 🔐");
    string today = DateTime.Today.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
    Test("create, save both item types, reopen and Unicode", () =>
    {
        session.SaveItem(login);
        Check(session.FilePath == Path.GetFullPath(path), "Plain save switched to another file");
        byte[] original = File.ReadAllBytes(path);
        Check(Path.GetFileName(session.NextVersionPath) == $"synthetic_{today}_v1.kdbx", "Next version name is not v1");
        string first = session.SaveNewVersion([text with { Text = "Draft in v1" }]);
        Check(Path.GetFileName(first) == $"synthetic_{today}_v1.kdbx" && session.FilePath == first, "New version did not become v1");
        Check(original.SequenceEqual(File.ReadAllBytes(path)), "Creating a version modified the previous file");
        Check(Path.GetFileName(session.NextVersionPath) == $"synthetic_{today}_v2.kdbx", "Next version name is not v2");
        session.SaveItem(text);
        Check(session.FilePath == first, "Plain save after a new version left the version file");
        using var reopened = VaultSession.Open(first, password);
        Check(reopened.GetItem(loginId) == login, "Login roundtrip differs");
        Check(reopened.GetItem(textId) == text, "Text roundtrip differs");
        using var older = VaultSession.Open(path, password);
        Check(older.Items.Count == 1 && older.GetItem(loginId) == login, "The previous file lost its own contents");
    });
    Test("file is KDBX4 and plaintext is absent", () =>
    {
        byte[] bytes = File.ReadAllBytes(session.FilePath); KdbxGuard.Validate(bytes);
        foreach (string value in new[] { login.Title, login.Password, text.Text, "demo@example.com" })
        {
            Check(!Encoding.UTF8.GetString(bytes).Contains(value, StringComparison.Ordinal), "Plaintext leaked (UTF8)");
            Check(!Encoding.Unicode.GetString(bytes).Contains(value, StringComparison.Ordinal), "Plaintext leaked (UTF16)");
        }
    });
    Test("version numbers are continuous across dates and legacy dated names", () =>
    {
        string series = Path.Combine(root, "series"); Directory.CreateDirectory(series);
        string old = Path.Combine(series, "my_vault_2000-01-01_v7.kdbx");
        File.Copy(session.FilePath, old);
        byte[] before = File.ReadAllBytes(old);
        using var version = VaultSession.Open(old, password);
        Check(version.NewerVersionPath is null, "The only version was reported as outdated");
        var edited = login with { Title = "SYNTHETIC VERSION" };
        version.SaveNewVersion([edited]);
        Check(Path.GetFileName(version.FilePath) == $"my_vault_{today}_v8.kdbx", "Numbering restarted or ignored the base name");
        Check(before.SequenceEqual(File.ReadAllBytes(old)), "Previous version was changed");
        using var reopened = VaultSession.Open(version.FilePath, password);
        Check(reopened.GetItem(loginId) == edited && reopened.LastSavedAt is not null &&
            reopened.LastSavedAt == version.LastSavedAt, "Version changes or saved timestamp did not survive reopening");

        string legacy = Path.Combine(series, "legacy-2000-01-01.kdbx");
        File.Copy(old, legacy);
        using var fromLegacy = VaultSession.Open(legacy, password);
        fromLegacy.SaveNewVersion([edited]);
        Check(Path.GetFileName(fromLegacy.FilePath) == $"legacy_{today}_v1.kdbx", "Legacy dated name was not converted");
    });
    Test("old version is detected and can continue the series without overwriting", () =>
    {
        string series = Path.Combine(root, "series");
        string old = Path.Combine(series, "my_vault_2000-01-01_v7.kdbx");
        string latest = Path.Combine(series, $"my_vault_{today}_v8.kdbx");
        byte[] oldBytes = File.ReadAllBytes(old), latestBytes = File.ReadAllBytes(latest);
        using var stale = VaultSession.Open(old, password);
        Check(stale.NewerVersionPath == latest, "Newer version was not detected");
        Check(stale.NextVersionPath == Path.Combine(series, $"my_vault_{today}_v9.kdbx"), "Next number ignored the newest file");
        stale.SaveNewVersion([]);
        Check(Path.GetFileName(stale.FilePath) == $"my_vault_{today}_v9.kdbx" && stale.NewerVersionPath is null,
            "Restoring an old version did not continue the series");
        Check(oldBytes.SequenceEqual(File.ReadAllBytes(old)) && latestBytes.SequenceEqual(File.ReadAllBytes(latest)),
            "An existing version was overwritten");

        Check(Path.GetFileName(VaultVersions.FirstPath(Path.Combine(series, "Personal.kdbx"), DateTime.Today)) ==
            $"Personal_{today}_v1.kdbx", "New vault name is not v1");
        Reject(() => VaultVersions.FirstPath(Path.Combine(series, "my_vault.kdbx"), DateTime.Today),
            "A new vault joined an existing series");
        Check(VaultVersions.Parse("a_b_2026-02-30_v3.kdbx") == new VaultVersions.VersionName("a_b_2026-02-30_v3", 0) &&
            VaultVersions.Parse("a_b_2026-02-03_v3.kdbx") == new VaultVersions.VersionName("a_b", 3),
            "Version names are parsed incorrectly");
    });
    Test("plain text export includes saved contents and refuses overwrites", () =>
    {
        string export = Path.Combine(root, "synthetic-export.txt");
        session.ExportText(export);
        using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(export));
        var exported = document.RootElement.EnumerateArray().ToArray();
        Check(exported.Any(entry => entry.GetProperty("Password").GetString() == login.Password), "Export omitted the saved password");
        Check(exported.Any(entry => entry.GetProperty("Text").GetString() == text.Text), "Export omitted the saved note");
        Reject(() => session.ExportText(export), "Existing export was overwritten");
    });
    Test("wrong password rejects and leaves file intact", () =>
    {
        byte[] before = File.ReadAllBytes(path);
        Reject(() => VaultSession.Open(path, "Wrong-fictional-password").Dispose(), "Wrong password accepted");
        Check(before.SequenceEqual(File.ReadAllBytes(path)), "Wrong-password open modified file");
    });
    Test("ciphertext tampering rejected", () =>
    {
        byte[] bytes = File.ReadAllBytes(path); bytes[^30] ^= 0x80;
        string bad = Path.Combine(root, "tampered.kdbx"); File.WriteAllBytes(bad, bytes);
        Reject(() => VaultSession.Open(bad, password).Dispose(), "Tampered body accepted");
        Check(bytes.SequenceEqual(File.ReadAllBytes(bad)), "Bad file modified");
    });
    Test("truncated files rejected", () =>
    {
        byte[] bytes = File.ReadAllBytes(path);
        foreach (int length in new[] { 0, 11, 20, bytes.Length / 2, bytes.Length - 1 })
        {
            string bad = Path.Combine(root, "truncated.kdbx"); File.WriteAllBytes(bad, bytes[..length]);
            Reject(() => VaultSession.Open(bad, password).Dispose(), "Truncated file accepted");
        }
    });
    Test("hostile KDF memory rejected before derivation", () =>
    {
        byte[] bytes = File.ReadAllBytes(path);
        // Variant dictionary encodes UTF8 key M, followed by 8-byte value.
        byte[] pattern = [0x05, 1, 0, 0, 0, (byte)'M', 8, 0, 0, 0];
        int offset = bytes.AsSpan().IndexOf(pattern);
        Check(offset >= 0, "KDF memory field not found");
        Array.Fill(bytes, (byte)0xff, offset + pattern.Length, 8);
        Reject(() => KdbxGuard.Validate(bytes), "Excessive KDF memory accepted");
    });
    Test("safe save fault after flush preserves disk and in-memory version", () =>
    {
        string current = session.FilePath;
        byte[] before = File.ReadAllBytes(current);
        int files = Directory.GetFiles(root).Length;
        bool flushed = false;
        using var failing = VaultSession.Open(current, password, new AtomicVaultFile(stage =>
            { if (stage == "after-flush") { flushed = true; throw new IOException("Synthetic IO failure"); } }));
        Reject(() => failing.SaveItem(login with { Password = "Unsaved-fictional-secret" }), "Failure not propagated");
        Check(flushed, "The fault was not reached");
        Check(before.SequenceEqual(File.ReadAllBytes(current)), "Original damaged");
        Check(failing.FilePath == current && Directory.GetFiles(root).Length == files, "Failed save left a new version");
        Check(failing.GetItem(loginId) == login, "Failed transaction changed in-memory database");
        Check(!Directory.EnumerateFiles(root, "*.tmp").Any(), "Temporary file not cleaned");
        flushed = false;
        Reject(() => failing.SaveNewVersion([login with { Password = "Unsaved-fictional-secret" }]), "New-version failure not propagated");
        Check(flushed && failing.FilePath == current && Directory.GetFiles(root).Length == files &&
            !Directory.EnumerateFiles(root, "*.tmp").Any(), "Failed new version left files or switched the active file");
    });
    Test("new file creation does not overwrite existing file", () =>
    {
        byte[] before = File.ReadAllBytes(path);
        Reject(() => VaultSession.Create(path, "Other-fictional-password").Dispose(), "Existing file overwritten");
        Check(before.SequenceEqual(File.ReadAllBytes(path)), "Create overwrote original");
    });
    Test("external modification and stale second session rejected", () =>
    {
        using var stale = VaultSession.Open(session.FilePath, password);
        session.SaveItem(text with { Text = text.Text + "\nSaved update" });
        byte[] before = File.ReadAllBytes(session.FilePath);
        int files = Directory.GetFiles(root).Length;
        Reject(() => stale.SaveItem(login with { Title = "Stale edit" }), "Stale write accepted");
        Check(before.SequenceEqual(File.ReadAllBytes(session.FilePath)) && Directory.GetFiles(root).Length == files,
            "External change overwritten");

        string current = session.FilePath;
        using var modified = VaultSession.Open(current, password);
        byte[] original = File.ReadAllBytes(current);
        File.WriteAllBytes(current, File.ReadAllBytes(path));
        Reject(() => modified.SaveItem(login with { Title = "Edit after external change" }), "Externally modified current file ignored");
        File.WriteAllBytes(current, original);
    });
    Test("fresh encryption changes ciphertext for equal content", () =>
    {
        byte[] before = File.ReadAllBytes(session.FilePath); session.SaveItem(login);
        Check(!before.SequenceEqual(File.ReadAllBytes(session.FilePath)), "Encryption randomness reused");
    });
    Test("folder create, rename, delete and nonempty-folder protection", () =>
    {
        session.AddFolder("Synthetic folder");
        string id = session.Folders.Single(f => f.Name == "Synthetic folder").Id;
        session.RenameFolder(id, "Renamed synthetic folder");
        Check(session.Folders.Any(f => f.Id == id && f.Name == "Renamed synthetic folder"), "Rename failed");
        session.DeleteFolder(id); Check(session.Folders.All(f => f.Id != id), "Folder not deleted");
        Reject(() => session.DeleteFolder(folder), "Nonempty folder deleted");
    });
    Test("deletion persists on reopen", () =>
    {
        session.DeleteItem(textId);
        using var reopened = VaultSession.Open(session.FilePath, password);
        Check(reopened.Items.All(x => x.Id != textId) && reopened.GetItem(loginId) == login, "Deletion corrupted other entry");
    });
    Test("moving a folder preserves its subtree and rejects cycles", () =>
    {
        session.AddFolder("Synthetic destination");
        session.AddFolder("Synthetic child", folder);
        string destination = session.Folders.Single(f => f.DisplayName == "Synthetic destination").Id;
        string childFolder = session.Folders.Single(f => f.DisplayName == "Synthetic child").Id;
        Reject(() => session.MoveFolder(folder, childFolder), "Folder moved inside its own subtree");
        Reject(() => session.MoveFolder(folder, folder), "Folder moved into itself");
        Reject(() => session.MoveFolder(session.Folders[0].Id, destination), "Vault root was moved");
        session.MoveFolder(folder, destination);
        using var reopened = VaultSession.Open(session.FilePath, password);
        Check(reopened.Folders.Single(f => f.Id == folder).ParentId == destination, "Folder did not move");
        Check(reopened.Folders.Single(f => f.Id == childFolder).ParentId == folder, "Nested folder was lost");
        Check(reopened.GetItem(loginId) == login, "Record changed when moving its folder");
    });
    Test("vault reopens in a fresh process with no persisted memory keys", () =>
    {
        var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true };
        if (string.Equals(Path.GetFileNameWithoutExtension(Environment.ProcessPath), "dotnet", StringComparison.OrdinalIgnoreCase))
            start.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
        start.ArgumentList.Add("--open-in-child"); start.ArgumentList.Add(session.FilePath);
        using var child = Process.Start(start)!;
        if (!child.WaitForExit(30000)) { child.Kill(); throw new Exception("Child open timed out"); }
        Check(child.ExitCode == 0, "Fresh process could not read vault");
    });
    Test("blank item rejected", () => Reject(() => session.SaveItem(new("00000000000000000000000000000001", folder, ItemKind.Text, " ", Text: "\n")), "Blank item saved"));
    Console.WriteLine($"{passed} integration checks passed. Synthetic encrypted test files: {root}");
}
catch (Exception ex)
{
    Console.Error.WriteLine("FAIL " + ex.GetType().Name + ": " + ex.Message);
    Environment.ExitCode = 1;
}
