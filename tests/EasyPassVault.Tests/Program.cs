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
    Test("create, save both item types, reopen and Unicode", () =>
    {
        session.SaveItem(login); session.SaveItem(text);
        using var reopened = VaultSession.Open(path, password);
        Check(reopened.GetItem(loginId) == login, "Login roundtrip differs");
        Check(reopened.GetItem(textId) == text, "Text roundtrip differs");
    });
    Test("file is KDBX4 and plaintext is absent", () =>
    {
        byte[] bytes = File.ReadAllBytes(path); KdbxGuard.Validate(bytes);
        foreach (string value in new[] { login.Title, login.Password, text.Text, "demo@example.com" })
        {
            Check(!Encoding.UTF8.GetString(bytes).Contains(value, StringComparison.Ordinal), "Plaintext leaked (UTF8)");
            Check(!Encoding.Unicode.GetString(bytes).Contains(value, StringComparison.Ordinal), "Plaintext leaked (UTF16)");
        }
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
        byte[] before = File.ReadAllBytes(path);
        using var failing = VaultSession.Open(path, password, new AtomicVaultFile(stage => { if (stage == "after-flush") throw new IOException("Synthetic IO failure"); }));
        Reject(() => failing.SaveItem(login with { Password = "Unsaved-fictional-secret" }), "Failure not propagated");
        Check(before.SequenceEqual(File.ReadAllBytes(path)), "Original damaged");
        Check(failing.GetItem(loginId) == login, "Failed transaction changed in-memory database");
        Check(!Directory.EnumerateFiles(root, "*.tmp").Any(), "Temporary file not cleaned");
    });
    Test("new file creation does not overwrite existing file", () =>
    {
        byte[] before = File.ReadAllBytes(path);
        Reject(() => VaultSession.Create(path, "Other-fictional-password").Dispose(), "Existing file overwritten");
        Check(before.SequenceEqual(File.ReadAllBytes(path)), "Create overwrote original");
    });
    Test("external modification and stale second session rejected", () =>
    {
        using var stale = VaultSession.Open(path, password);
        session.SaveItem(text with { Text = text.Text + "\nSaved update" });
        byte[] before = File.ReadAllBytes(path);
        Reject(() => stale.SaveItem(login with { Title = "Stale edit" }), "Stale write accepted");
        Check(before.SequenceEqual(File.ReadAllBytes(path)), "External change overwritten");
    });
    Test("fresh encryption changes ciphertext for equal content", () =>
    {
        byte[] before = File.ReadAllBytes(path); session.SaveItem(login);
        Check(!before.SequenceEqual(File.ReadAllBytes(path)), "Encryption randomness reused");
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
        using var reopened = VaultSession.Open(path, password);
        Check(reopened.Items.All(x => x.Id != textId) && reopened.GetItem(loginId) == login, "Deletion corrupted other entry");
    });
    Test("vault reopens in a fresh process with no persisted memory keys", () =>
    {
        var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true };
        if (string.Equals(Path.GetFileNameWithoutExtension(Environment.ProcessPath), "dotnet", StringComparison.OrdinalIgnoreCase))
            start.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
        start.ArgumentList.Add("--open-in-child"); start.ArgumentList.Add(path);
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
