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
if (args.Length >= 1 && args[0] == "--benchmark")
{
    // Not part of Test.ps1: times opening and saving of large synthetic vaults made of random bytes.
    // Run: dotnet run --project tests/EasyPassVault.Tests -- --benchmark 100 300
    string bench = Path.GetFullPath(Path.Combine(".artifacts", "benchmark", Guid.NewGuid().ToString("N")));
    Directory.CreateDirectory(bench);
    try
    {
        foreach (int megabytes in args.Skip(1).Select(int.Parse).DefaultIfEmpty(100))
        {
            string file = Path.Combine(bench, $"synthetic-{megabytes}mb.kdbx");
            string id = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(16));
            const int chunk = 20 * 1024 * 1024;
            var attachments = new List<VaultAttachment>();
            for (long left = (long)megabytes * 1024 * 1024, n = 1; left > 0; left -= chunk, n++)
            {
                byte[] data = System.Security.Cryptography.RandomNumberGenerator.GetBytes((int)Math.Min(chunk, left));
                attachments.Add(new($"random-{n}.bin", AttachmentContent.FromBytes(data), DateTime.UtcNow));
            }
            var watch = Stopwatch.StartNew();
            using (var created = VaultSession.Create(file, password))
            {
                long createMs = watch.ElapsedMilliseconds; watch.Restart();
                created.SaveItem(new VaultItem(id, created.Folders[1].Id, ItemKind.Text, "Случайные данные.txt", Text: "вымысел") { Attachments = attachments });
                Console.WriteLine($"{megabytes} MB: create {createMs} ms, first save with attachments {watch.ElapsedMilliseconds} ms, file {new FileInfo(file).Length / 1048576.0:0.0} MB");
            }
            attachments.Clear();
            GC.Collect();
            watch.Restart();
            using var opened = VaultSession.Open(file, password);
            long openMs = watch.ElapsedMilliseconds; watch.Restart();
            var item = opened.GetItem(id);
            opened.SaveItem(item with { Text = "вымысел 2" });
            long saveMs = watch.ElapsedMilliseconds; watch.Restart();
            string version = opened.SaveNewVersion([item with { Text = "вымысел 3" }]);
            long versionMs = watch.ElapsedMilliseconds;
            Console.WriteLine($"{megabytes} MB: open {openMs} ms, save {saveMs} ms, new version file {versionMs} ms, " +
                $"peak working set {Process.GetCurrentProcess().PeakWorkingSet64 / 1048576} MB");
            opened.Dispose();
            File.Delete(file); File.Delete(version);
        }
    }
    finally { Directory.Delete(bench, recursive: true); }
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
    Test("window layout roundtrip keeps sizes and mode and stores nothing else", () =>
    {
        string file = Path.Combine(root, WindowLayout.FileName);
        Check(WindowLayoutStore.Load(file) == new WindowLayout(), "A missing layout file did not give defaults");
        var saved = new WindowLayout
        {
            Left = 40, Top = 30, Width = 1300, Height = 800, CompactWidth = 480, CompactHeight = 700,
            Maximized = true, Compact = true, FoldersWidth = 200, ListWidth = 330,
            FolderSort = FolderSort.NameDescending, ItemSort = ItemSort.ModifiedOldest
        };
        Check(WindowLayoutStore.Save(file, saved), "The layout file was not written");
        Check(WindowLayoutStore.Load(file) == saved, "Layout roundtrip differs");
        var remembered = WindowLayoutStore.Load(file).Normalize(new(0, 0, 1920, 1080));
        Check(remembered.FolderSort == FolderSort.NameDescending && remembered.ItemSort == ItemSort.ModifiedOldest,
            "The chosen sort order was not remembered");
        foreach (var sort in Enum.GetValues<ItemSort>())
        {
            WindowLayoutStore.Save(file, saved with { ItemSort = sort });
            Check(WindowLayoutStore.Load(file).Normalize(new(0, 0, 1920, 1080)).ItemSort == sort, "An item sort order was lost");
        }
        WindowLayoutStore.Save(file, saved);
        string json = File.ReadAllText(file);
        Check(!json.Contains("kdbx", StringComparison.OrdinalIgnoreCase) && !json.Contains(root, StringComparison.Ordinal) &&
            !json.Contains(password, StringComparison.Ordinal), "The layout file contains more than sizes and mode");
        Check(!WindowLayoutStore.Save(Path.Combine(root, "missing-directory", WindowLayout.FileName), saved),
            "An unwritable layout path did not report failure");
    });
    Test("damaged window layout files fall back to defaults", () =>
    {
        string file = Path.Combine(root, "damaged-" + WindowLayout.FileName);
        foreach (string content in new[] { "", "{", "null", "[1,2]", "{\"Width\":\"wide\"}", "{\"Compact\":7}", "\0\0\0", new string('x', 20000) })
        {
            File.WriteAllText(file, content);
            Check(WindowLayoutStore.Load(file) == new WindowLayout(), "A damaged layout file was not replaced by defaults");
        }
        File.WriteAllText(file, "{\"Width\":99999,\"Height\":-5,\"Left\":50,\"Top\":50,\"FoldersWidth\":5000,\"ListWidth\":1,\"Unknown\":true}");
        var odd = WindowLayoutStore.Load(file).Normalize(new(0, 0, 1920, 1080));
        Check(odd.Width is >= WindowLayout.MinWideWidth and <= 1920 && odd.Height == WindowLayout.MinHeight,
            "Out-of-range sizes were not clamped");
        Check(odd.FolderSort == FolderSort.NameAscending && odd.ItemSort == ItemSort.NameAscending,
            "A layout file without a sort order did not give the default order");
        File.WriteAllText(file, "{\"FolderSort\":9,\"ItemSort\":-3}");
        var unknown = WindowLayoutStore.Load(file).Normalize(new(0, 0, 1920, 1080));
        Check(unknown.FolderSort == FolderSort.NameAscending && unknown.ItemSort == ItemSort.NameAscending,
            "An unknown sort order was not replaced by the default");
        Check(odd.FoldersWidth + odd.ListWidth + WindowLayout.MinEditorWidth <= odd.Width &&
            odd.ListWidth >= WindowLayout.MinListWidth && odd.FoldersWidth >= WindowLayout.MinFoldersWidth,
            "Area widths left no room for the editor");
    });
    Test("window position outside the screens is centered instead", () =>
    {
        var screen = new WindowLayout.Area(0, 0, 1920, 1080);
        var inside = new WindowLayout { Left = 100, Top = 80 }.Normalize(screen);
        Check(inside.Left == 100 && inside.Top == 80, "A visible position was discarded");
        foreach (var (left, top) in new (double?, double?)[] { (5000, 80), (-3000, 80), (100, 4000), (100, -500), (null, 80), (double.NaN, 80), (1900, 80) })
        {
            var moved = new WindowLayout { Left = left, Top = top }.Normalize(screen);
            Check(moved.Left is null && moved.Top is null, "An unreachable position was kept");
        }
        var second = new WindowLayout { Left = -1500, Top = 200 }.Normalize(new(-1920, 0, 3840, 1080));
        Check(second.Left == -1500, "A position on a second screen to the left was discarded");
        var small = new WindowLayout { Width = 3000, Height = 2000, CompactWidth = 100 }.Normalize(new(0, 0, 1280, 720));
        Check(small.Width == 1280 && small.Height == 720 && small.CompactWidth == WindowLayout.MinCompactWidth,
            "Sizes were not limited by the screen");
    });
    Test("sorting follows the interface language and ignores case", () =>
    {
        var ru = System.Globalization.CultureInfo.GetCultureInfo("ru");
        VaultItem Item(string title, int? day = null) => new(title, "f", ItemKind.Login, title)
            { Modified = day is { } d ? new DateTime(2026, 1, d, 0, 0, 0, DateTimeKind.Utc) : null };
        string Order(ItemSort sort, params VaultItem[] items) =>
            string.Join(",", VaultSorting.SortItems(items, sort, ru).Select(item => item.Title));

        // Ordinal order would put capitals first and "ё" after "я".
        var names = new[] { Item("яблоко"), Item("Груша"), Item("банан"), Item("ёлка"), Item("Арбуз"), Item("дом"), Item("вишня") };
        Check(Order(ItemSort.NameAscending, names) == "Арбуз,банан,вишня,Груша,дом,ёлка,яблоко", "Cyrillic titles are not sorted A to Z");
        Check(Order(ItemSort.NameDescending, names) == "яблоко,ёлка,дом,Груша,вишня,банан,Арбуз", "Cyrillic titles are not sorted Z to A");
        Check(Order(ItemSort.NameAscending, Item("beta"), Item("Alpha"), Item("gamma"), Item("Delta")) == "Alpha,beta,Delta,gamma",
            "Latin titles are sorted with capitals first");
        Check(Order(ItemSort.NameAscending, Item("почта"), Item("Почта"), Item("ПОЧТА")) ==
            Order(ItemSort.NameAscending, Item("ПОЧТА"), Item("Почта"), Item("почта")), "Titles that differ only by case have no stable order");

        var dated = new[] { Item("b", 2), Item("a", 3), Item("c", 1), Item("new") };
        Check(Order(ItemSort.ModifiedNewest, dated) == "new,a,b,c", "Newest first is wrong or hides an unsaved item");
        Check(Order(ItemSort.ModifiedOldest, dated) == "c,b,a,new", "Oldest first is wrong");
        Check(Order(ItemSort.ModifiedNewest, Item("б", 5), Item("А", 5)) == "А,б", "Equal dates are not ordered by title");
        Check(VaultSorting.SortItems([new("1", "f", ItemKind.Text, " "), Item("Яма")], ItemSort.NameAscending, ru,
            item => string.IsNullOrWhiteSpace(item.Title) ? "Новый файл" : item.Title)[0].Id == "1", "The shown title of an untitled draft is ignored");

        FolderInfo[] folders =
        [
            new("r", "root"), new("1", "яблоки", "r", "яблоки"), new("2", "Банки", "r", "Банки"),
            new("21", "Банки / почта", "2", "почта"), new("22", "Банки / Архив", "2", "Архив"), new("3", "ёлки", "r", "ёлки")
        ];
        string Tree(FolderSort sort) => string.Join(",", VaultSorting.SortFolders(folders, sort, ru).Select(f => f.Id));
        Check(Tree(FolderSort.NameAscending) == "r,2,22,21,3,1", "Folders are not sorted A to Z on each level");
        Check(Tree(FolderSort.NameDescending) == "r,1,3,2,21,22", "Folders are not sorted Z to A on each level");
        Check(VaultSorting.SortFolders(session.Folders, FolderSort.NameDescending, ru).Count == session.Folders.Count &&
            VaultSorting.SortFolders(session.Folders, FolderSort.NameDescending, ru)[0].Id == session.Folders[0].Id,
            "Sorting lost a folder or moved the root");
    });
    Test("modification time comes from the vault and sorting writes nothing", () =>
    {
        string file = Path.Combine(root, "synthetic-dates.kdbx");
        using var dates = VaultSession.Create(file, password);
        string target = dates.Folders[1].Id;
        var before = DateTime.UtcNow.AddSeconds(-2);
        var entry = new VaultItem("000000000000000000000000000000A1", target, ItemKind.Login, "Synthetic dated", Password: "Fictional-secret-112!");
        var blank = new VaultItem("000000000000000000000000000000A2", target, ItemKind.Login, "Synthetic without password");
        dates.SaveItems([entry, blank]);
        using var reopened = VaultSession.Open(file, password);
        var listed = reopened.Items.Single(item => item.Id == entry.Id);
        Check(listed.Modified is { Kind: DateTimeKind.Utc } modified && modified >= before && modified <= DateTime.UtcNow.AddSeconds(2),
            "The modification time was not read from the vault");
        Check(reopened.GetItem(entry.Id).Modified == listed.Modified && reopened.GetItem(entry.Id) == entry,
            "The modification time changed the item's contents or differs between reads");
        Check(listed.Password.Length == 0 && listed.HasPassword && !reopened.Items.Single(item => item.Id == blank.Id).HasPassword &&
            entry.WithoutPassword() is { Password: "", HasPassword: true }, "Lists do not know whether an entry has a password");
        byte[] bytes = File.ReadAllBytes(file);
        foreach (var sort in Enum.GetValues<ItemSort>())
            VaultSorting.SortItems(reopened.Items, sort, System.Globalization.CultureInfo.InvariantCulture);
        VaultSorting.SortFolders(reopened.Folders, FolderSort.NameDescending, System.Globalization.CultureInfo.InvariantCulture);
        Check(bytes.SequenceEqual(File.ReadAllBytes(file)), "Sorting changed the vault file");
    });
    Test("only well-formed http and https addresses may be opened", () =>
    {
        foreach (string good in new[] { "https://example.com", "http://example.com/path?q=1", " HTTPS://Mail.Example.com:8443/ ", "https://пример.example" })
            Check(SiteAddress.TryGetWebUri(good, out var uri) && uri.Scheme is "http" or "https", "A web address was rejected: " + good);
        foreach (string? bad in new[] { null, "", "   ", "example.com", "www.example.com", "ftp://example.com", "file:///C:/Windows/notepad.exe",
            "C:\\Windows\\notepad.exe", "javascript:alert(1)", "http://", "http:example.com", "https://exa mple.com",
            "mailto:demo@example.com", "\\\\server\\share", "cmd /c calc", "https://example.com\ncalc" })
            Check(!SiteAddress.TryGetWebUri(bad, out _), "A non-web address was accepted: " + bad);
    });
    Test("quick search finds titles, logins, sites and text, ignores case and never looks at passwords", () =>
    {
        var ru = System.Globalization.CultureInfo.GetCultureInfo("ru");
        VaultItem[] items =
        [
            new("1", "f", ItemKind.Login, "Почта (пример)", "demo.user@example.com", "Fictional-ПОЧТА-555", "https://mail.example.com"),
            new("2", "f", ItemKind.Login, "Рабочий портал", "i.ivanov@example.org", "Секретный-ёжик-pochta", "https://portal.example.org", "Заметка про ПОЧТУ"),
            new("3", "f", ItemKind.Text, "Настройки.txt", Text: "Сервер почты: mail.example.com"),
            new("4", "f", ItemKind.Login, "Форум", "kaktus_demo", "Fictional-secret-zzz")
        ];
        string Ids(string query) => string.Join(",", VaultSearch.Find(items, query, ru).Select(item => item.Id));
        Check(Ids("ёжик") == "" && Ids("pochta") == "" && Ids("secret-zzz") == "" && Ids("555") == "",
            "Quick search looked at a password");
        Check(Ids("ПОЧТ") == "1,3,2" && Ids("почт") == "1,3,2" && Ids("  Почт ") == "1,3,2",
            "Cyrillic search is case sensitive or does not put title matches first");
        Check(Ids("MAIL.EXAMPLE") == "3,1", "Latin search over the site and the text is case sensitive");
        Check(Ids("portal.example") == "2" && Ids("KAKTUS") == "4" && Ids("сервер") == "3", "Site, login or text was not searched");
        Check(Ids("") == "3,1,2,4" && Ids("нет такого") == "", "An empty query must find everything, a missing word nothing");
        Check(VaultSearch.Find(session.Items, "Fictional-secret-761", ru).Count == 0 &&
            VaultSearch.Find(session.Items, "synthetic title", ru).Count == 1, "Search over a real vault list is wrong");
    });
    Test("recent vault files: at most five, newest first, only paths and times", () =>
    {
        var start = new DateTime(2026, 11, 1, 8, 0, 0, DateTimeKind.Utc);
        string Vault(string name) => Path.Combine(root, name + ".kdbx");
        var recent = RecentVaultList.Empty;
        for (int i = 1; i <= 7; i++) recent = recent.Touch(Vault("vault" + i), start.AddHours(i));
        string Names(RecentVaultList list) => string.Join(",", list.Select(item => Path.GetFileNameWithoutExtension(item.Path)));
        Check(recent.Count == RecentVaultList.Max && Names(recent) == "vault7,vault6,vault5,vault4,vault3", "The list is not the five newest");
        recent = recent.Touch(Vault("VAULT4"), start.AddHours(10));
        Check(Names(recent) == "VAULT4,vault7,vault6,vault5,vault3", "Reopening a file did not move it to the top once");
        Check(recent.Touch("relative.kdbx", start) == recent && recent.Touch(Path.Combine(root, "notes.txt"), start) == recent &&
            recent.Touch("", start) == recent, "A relative path or a non-vault file got into the list");
        Check(Names(recent.Remove(Vault("vault6"))) == "VAULT4,vault7,vault5,vault3", "Removing from the list failed");

        string file = Path.Combine(root, "recent-" + WindowLayout.FileName);
        var layout = new WindowLayout { RecentVaults = recent };
        Check(WindowLayoutStore.Save(file, layout) && WindowLayoutStore.Load(file) == layout, "The recent list did not survive a roundtrip");
        Check(WindowLayoutStore.Load(file).Normalize(new(0, 0, 1920, 1080)).RecentVaults == recent, "Normalizing changed a valid list");
        using (var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(file)))
        {
            var stored = document.RootElement.GetProperty("RecentVaults").EnumerateArray().ToArray();
            Check(stored.Length == 5 && stored.All(entry =>
                entry.EnumerateObject().Select(property => property.Name).SequenceEqual(["Path", "OpenedUtc"]) &&
                entry.GetProperty("Path").GetString()!.EndsWith(".kdbx", StringComparison.OrdinalIgnoreCase)),
                "A recent entry holds more than its path and time");
        }
        Check(!File.ReadAllText(file).Contains(password, StringComparison.Ordinal), "The settings file contains a password");

        // Damaged settings: a wrong entry is skipped, a wrong list gives an empty one, a broken file the defaults.
        string Entry(string path, string time) => "{\"Path\":" + System.Text.Json.JsonSerializer.Serialize(path) + ",\"OpenedUtc\":\"" + time + "\"}";
        File.WriteAllText(file, "{\"Width\":1300,\"RecentVaults\":[{\"Path\":5}, null, 7, " + Entry(Vault("bad-time"), "yesterday") + ", " +
            Entry("relative.kdbx", "2026-01-01T00:00:00Z") + ", " + Entry(Vault("note") + ".txt", "2026-01-01T00:00:00Z") + ", " +
            "{\"Path\":" + System.Text.Json.JsonSerializer.Serialize(Vault("ok")) + ",\"OpenedUtc\":\"2026-01-02T00:00:00Z\",\"Password\":\"x\"}, " +
            Entry(Vault("OK"), "2026-01-01T00:00:00Z") + "]}");
        var damaged = WindowLayoutStore.Load(file).Normalize(new(0, 0, 1920, 1080));
        Check(damaged.Width == 1300 && Names(damaged.RecentVaults) == "ok" && damaged.RecentVaults[0].OpenedUtc == new DateTime(2026, 1, 2),
            "Damaged recent entries were not skipped one by one");
        WindowLayoutStore.Save(file, damaged);
        Check(!File.ReadAllText(file).Contains("Password", StringComparison.Ordinal), "An unknown field of a recent entry was written back");
        foreach (string content in new[] { "{\"RecentVaults\":\"C:\\\\a.kdbx\"}", "{\"RecentVaults\":null}", "{\"RecentVaults\":{}}", "{\"RecentVaults\":[" })
        {
            File.WriteAllText(file, content);
            Check(WindowLayoutStore.Load(file).Normalize(new(0, 0, 1920, 1080)).RecentVaults.Count == 0, "A damaged recent list was not emptied: " + content);
        }
        var many = string.Join(",", Enumerable.Range(1, 20).Select(i => Entry(Vault("many" + i), $"2026-01-{i:00}T00:00:00Z")));
        File.WriteAllText(file, "{\"RecentVaults\":[" + many + "]}");
        Check(Names(WindowLayoutStore.Load(file).Normalize(new(0, 0, 1920, 1080)).RecentVaults) == "many20,many19,many18,many17,many16",
            "A long hand-edited list was not cut to the five newest");
    });
    Test("version list: newest first, current and newest marks, next file", () =>
    {
        string folderPath = Path.Combine(root, "version-list");
        Directory.CreateDirectory(folderPath);
        string Make(string name, int size, int day)
        {
            string file = Path.Combine(folderPath, name);
            File.WriteAllBytes(file, new byte[size]);
            File.SetLastWriteTimeUtc(file, new DateTime(2026, 10, day, 12, 0, 0, DateTimeKind.Utc));
            return file;
        }
        Make("personal_2026-09-30_v9.kdbx", 10, 1);
        Make("personal_2026-10-14_v10.kdbx", 2048, 14);
        string current = Make("personal_2026-10-28_v11.kdbx", 3000, 28);
        string latest = Make("Personal_2026-11-05_v12.kdbx", 5000, 30);
        Make("other_2026-11-05_v20.kdbx", 1, 2);
        Make("personal_2026-11-05_v13.kdbx.bak", 1, 3);
        Make("personal.txt", 1, 4);
        var list = VaultVersions.List(current);
        Check(string.Join(",", list.Select(file => file.Number)) == "12,11,10,9", "The series is not listed newest first or has strangers");
        Check(list.Single(file => file.IsCurrent).Path == current && list.Single(file => file.IsLatest).Path == latest,
            "The open file or the newest file is not marked");
        Check(list.Single(file => file.Number == 10) is { Size: 2048 } ten && ten.ModifiedUtc == new DateTime(2026, 10, 14, 12, 0, 0),
            "Size or time on disk is wrong");
        Check(Path.GetFileName(VaultVersions.NextPath(current, new DateTime(2026, 11, 14))) == "personal_2026-11-14_v13.kdbx",
            "The next file does not continue after the newest number");
        string plain = Make("plain.kdbx", 1, 5);
        Check(VaultVersions.List(plain) is [{ Number: 0, IsCurrent: true, IsLatest: false }], "A file outside a series is not listed alone");
        Check(VaultVersions.List(latest).Single(file => file.IsCurrent).IsLatest, "The newest open file is not marked as newest");
    });
    Test("password generator: length, sets, similar characters, strength", () =>
    {
        const string similar = PasswordGenerator.SimilarChars;
        var all = new PasswordOptions();
        Check(all.Length == 20 && all.Upper && all.Lower && all.Digits && all.Symbols && !all.ExcludeSimilar, "Wrong defaults");
        foreach (int length in new[] { 8, 20, 64 })
            for (int i = 0; i < 200; i++)
            {
                string generated = PasswordGenerator.Generate(all with { Length = length });
                Check(generated.Length == length, "Wrong length");
                Check(generated.Any(char.IsAsciiLetterUpper) && generated.Any(char.IsAsciiLetterLower) && generated.Any(char.IsAsciiDigit) &&
                    generated.Any(c => PasswordGenerator.SymbolSet.Contains(c)), "A chosen set is missing: " + length);
            }
        Check(PasswordGenerator.Generate(all with { Length = 3 }).Length == 8 && PasswordGenerator.Generate(all with { Length = 500 }).Length == 64,
            "The length was not kept within 8–64");
        var noSimilar = all with { ExcludeSimilar = true, Length = 64 };
        for (int i = 0; i < 300; i++)
            Check(!PasswordGenerator.Generate(noSimilar).Any(c => similar.Contains(c)), "A similar character got in");
        foreach (var options in new[]
        {
            new PasswordOptions { Upper = false, Lower = false, Symbols = false },
            new PasswordOptions { Upper = false, Digits = false },
            new PasswordOptions { Lower = false, Digits = false, Symbols = false, ExcludeSimilar = true },
            new PasswordOptions { Upper = false, Lower = false, Digits = false }
        })
        {
            string allowed = string.Concat(PasswordGenerator.Sets(options));
            var seen = new HashSet<char>();
            for (int i = 0; i < 300; i++)
            {
                string generated = PasswordGenerator.Generate(options with { Length = 64 });
                Check(generated.All(c => allowed.Contains(c)), "A character outside the chosen sets");
                seen.UnionWith(generated);
            }
            Check(seen.Count == allowed.Length, "Some allowed characters never appear: the choice is not uniform");
        }
        Check(PasswordGenerator.Sets(new PasswordOptions { Digits = false, Symbols = false, Upper = false }).Single() == PasswordGenerator.LowerSet,
            "Only lowercase was not the only set");
        var none = new PasswordOptions { Upper = false, Lower = false, Digits = false, Symbols = false };
        Check(!PasswordGenerator.CanGenerate(none) && PasswordGenerator.EntropyBits(none) == 0, "No sets must disable the generator");
        try { PasswordGenerator.Generate(none); throw new Exception("Generated without sets"); }
        catch (InvalidOperationException) { }
        Check(PasswordGenerator.AlphabetSize(all) == 26 + 26 + 10 + PasswordGenerator.SymbolSet.Length, "Wrong alphabet size");
        Check(PasswordGenerator.AlphabetSize(all with { ExcludeSimilar = true }) == PasswordGenerator.AlphabetSize(all) - 5, "Similar characters not removed");
        Check(Math.Abs(PasswordGenerator.EntropyBits(new PasswordOptions { Length = 10, Upper = false, Lower = false, Symbols = false }) - 10 * Math.Log2(10)) < 1e-9,
            "Entropy is not length × log2(alphabet)");
        Check(PasswordGenerator.Strength(49.9) == PasswordStrength.Weak && PasswordGenerator.Strength(50) == PasswordStrength.Medium &&
            PasswordGenerator.Strength(80) == PasswordStrength.Medium && PasswordGenerator.Strength(80.1) == PasswordStrength.Strong,
            "Wrong strength thresholds");
        Check(PasswordGenerator.Strength(PasswordGenerator.EntropyBits(new PasswordOptions { Length = 8, Upper = false, Symbols = false, Digits = false })) == PasswordStrength.Weak &&
            PasswordGenerator.Strength(PasswordGenerator.EntropyBits(new PasswordOptions { Length = 12 })) == PasswordStrength.Medium &&
            PasswordGenerator.Strength(PasswordGenerator.EntropyBits(all)) == PasswordStrength.Strong, "Strength of typical settings is wrong");

        string file = Path.Combine(root, "generator-" + WindowLayout.FileName);
        var layout = new WindowLayout { Generator = new PasswordOptions { Length = 33, Symbols = false, ExcludeSimilar = true } };
        Check(WindowLayoutStore.Save(file, layout) && WindowLayoutStore.Load(file) == layout, "Generator settings did not survive a roundtrip");
        File.WriteAllText(file, "{\"Generator\":{\"Length\":1000,\"Upper\":false}}");
        var loaded = WindowLayoutStore.Load(file).Normalize(new(0, 0, 1920, 1080)).Generator;
        Check(loaded.Length == 64 && !loaded.Upper && loaded.Lower, "A hand-edited length was not clamped");
        File.WriteAllText(file, "{\"Generator\":null}");
        Check(WindowLayoutStore.Load(file).Normalize(new(0, 0, 1920, 1080)).Generator == new PasswordOptions(), "A missing generator block broke defaults");
    });
    Test("text lines: only '# ' headings and '---' separators", () =>
    {
        MarkupKind? Kind(string line) => TextMarkup.Classify(line);
        Check(Kind("# Заголовок") == MarkupKind.Heading && Kind("# ") == MarkupKind.Heading, "'# x' is not a heading");
        foreach (string plain in new[] { "#x", "## x", " # x", "#", "**bold**", "- item", "", "--", "----", "--- x", "-- -", "x ---" })
            Check(Kind(plain) is null, "Plain text was marked: " + plain);
        foreach (string separator in new[] { "---", "  ---", "---   ", "\t--- " })
            Check(Kind(separator) == MarkupKind.Separator, "A separator with spaces was not found: " + separator);

        string text = "# Домашний Wi-Fi\r\nСеть: Primer-Home\r\n---\n## не заголовок\r  ---  \r\n# Гостевая\n----";
        var lines = TextMarkup.Parse(text);
        Check(lines.Count == 4 && lines.Select(line => text.Substring(line.Start, line.Length)).SequenceEqual(
            ["# Домашний Wi-Fi", "---", "  ---  ", "# Гостевая"]) &&
            lines.Select(line => line.Kind).SequenceEqual([MarkupKind.Heading, MarkupKind.Separator, MarkupKind.Separator, MarkupKind.Heading]),
            "Parsing found wrong lines");
        Check(TextMarkup.Parse("").Count == 0 && TextMarkup.Parse("---\r\n").Single().Length == 3, "Edge lines are wrong");

        // Heading button: adds "# " to the caret's line, removes it again, the caret stays on its character.
        string note = "first\r\nsecond line\r\nthird";
        var add = TextMarkup.ToggleHeading(note, 10);
        string added = TextMarkup.Apply(note, add);
        Check(added == "first\r\n# second line\r\nthird" && add.Caret == 12, "Heading was not added to the caret line");
        var remove = TextMarkup.ToggleHeading(added, add.Caret);
        Check(TextMarkup.Apply(added, remove) == note && remove.Caret == 10, "Heading was not removed");
        Check(TextMarkup.Apply("## x", TextMarkup.ToggleHeading("## x", 0)) == "# ## x", "'##' was treated as a heading");
        Check(TextMarkup.ToggleHeading("# x", 1).Caret == 0, "A caret inside '# ' went before the line");

        // Separator button: a "---" line after the caret's line, with the text's own line break.
        var middle = TextMarkup.InsertSeparator(note, 2);
        Check(TextMarkup.Apply(note, middle) == "first\r\n---\r\nsecond line\r\nthird" && middle.Caret == 12, "Separator inside the text is wrong");
        var last = TextMarkup.InsertSeparator("a\nb", 3);
        Check(TextMarkup.Apply("a\nb", last) == "a\nb\n---\n" && last.Caret == 8, "Separator at the end is wrong");
        Check(TextMarkup.Apply("", TextMarkup.InsertSeparator("", 0)) == "\r\n---\r\n", "Separator in an empty text is wrong");

        // Stored as typed: markup is only a way of showing the text. KDBX is XML and has always kept every
        // line break as "\n" (before markup too), so the byte-for-byte check uses "\n" texts.
        string stored = "# Заголовок\n---\nтекст ## **x**\n  ---\t\n#без пробела\n----\n# ";
        string markupPath = Path.Combine(root, "markup.kdbx");
        string id = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(16));
        using (var vault = VaultSession.Create(markupPath, password))
            vault.SaveItem(new VaultItem(id, vault.Folders[0].Id, ItemKind.Text, "Вымышленные заметки.txt", Text: stored));
        using (var reopened = VaultSession.Open(markupPath, password))
            Check(reopened.GetItem(id).Text == stored, "The text changed when saved");
    });
    // Attachments: standard KDBX 4 entry binaries. All bytes below are random or synthetic.
    string NewId() => Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(16));
    byte[] marker = Encoding.ASCII.GetBytes("SYNTHETIC-ATTACHMENT-MARKER-5121");
    byte[] Payload(int size)
    {
        byte[] data = System.Security.Cryptography.RandomNumberGenerator.GetBytes(size);
        marker.CopyTo(data, 0);
        return data;
    }
    Test("attachments: bytes, names and dates survive saving and reopening; the file has no plain bytes", () =>
    {
        string attachPath = Path.Combine(root, "attachments.kdbx");
        string id = NewId(), textFileId = NewId();
        byte[] photo = Payload(300_000), archive = Payload(70_000);
        var added = new DateTime(2026, 11, 2, 9, 30, 0, DateTimeKind.Utc);
        var item = new VaultItem(id, "", ItemKind.Login, "Договор аренды (пример)", "arendator@example.com")
        {
            Attachments = [new("Фото счётчика (пример).jpg", AttachmentContent.FromBytes(photo), added),
                new("Сканы (пример).zip", AttachmentContent.FromBytes(archive), added)]
        };
        using (var vault = VaultSession.Create(attachPath, password))
        {
            item = item with { FolderId = vault.Folders[1].Id };
            vault.SaveItems([item, new VaultItem(textFileId, vault.Folders[1].Id, ItemKind.Text, "Заметка.txt", Text: "x")
                { Attachments = [new("only.pdf", AttachmentContent.FromBytes(Payload(5_000)))] }]);
            Check(vault.FileSize == new FileInfo(attachPath).Length, "FileSize differs from the file on disk");
        }
        byte[] file = File.ReadAllBytes(attachPath);
        KdbxGuard.Validate(file);
        Check(file.AsSpan().IndexOf(marker) < 0 && file.AsSpan().IndexOf(photo.AsSpan(1000, 64)) < 0, "Attachment bytes are in plain form in the file");
        using (var reopened = VaultSession.Open(attachPath, password))
        {
            var back = reopened.GetItem(id);
            // KDBX keeps binaries sorted by name; the app shows them in that one order.
            Check(back.Attachments.Select(a => a.Name).SequenceEqual(["Сканы (пример).zip", "Фото счётчика (пример).jpg"]), "Attachment names or order changed");
            Check(back.Attachments[1].Content.ReadBytes().SequenceEqual(photo) && back.Attachments[0].Content.ReadBytes().SequenceEqual(archive),
                "Attachment bytes differ after reopening");
            Check(back == (back with { Attachments = VaultAttachments.Sorted(back.Attachments.Reverse()) }), "Sorting a reopened list made it look edited");
            Check(back.Attachments.All(a => a.Added == added), "Attachment dates were lost");
            Check(reopened.GetItem(textFileId).Attachments.Single().Added is null, "A text file lost its attachment or got a date");
            Check(reopened.Items.First(x => x.Id == id).AttachmentCount == 2, "The list copy does not know the attachment count");
            // Removing one attachment persists as well; the other stays byte-for-byte.
            reopened.SaveItem(back with { Attachments = [back.Attachments[0]] });
        }
        using (var again = VaultSession.Open(attachPath, password))
        {
            var left = again.GetItem(id).Attachments.Single();
            Check(left.Name == "Сканы (пример).zip" && left.Content.ReadBytes().SequenceEqual(archive), "Removing an attachment damaged the other one");
        }
    });
    Test("a vault without attachments opens and saves as before", () =>
    {
        // The vault of the first checks was written without attachments.
        using var plain = VaultSession.Open(path, password);
        Check(plain.Items.All(x => x.Attachments.Count == 0), "A vault without attachments shows some");
        var item = plain.GetItem(loginId);
        plain.SaveItem(item with { Title = "SYNTHETIC TITLE 2" });
        using var reopened = VaultSession.Open(path, password);
        Check(reopened.GetItem(loginId).Title == "SYNTHETIC TITLE 2" && reopened.GetItem(loginId).Attachments.Count == 0,
            "Saving a vault without attachments changed it");
    });
    Test("adding or removing an attachment edits the draft; the same list compares equal", () =>
    {
        var original = new VaultItem(NewId(), "f", ItemKind.Text, "Файл.txt", Text: "x");
        var attachment = new VaultAttachment("a.png", AttachmentContent.FromBytes([1, 2, 3]), DateTime.UtcNow);
        var withAttachment = original with { Attachments = [attachment] };
        Check(withAttachment != original && !withAttachment.Equals(original), "Adding an attachment did not change the item");
        Check(withAttachment == (withAttachment with { Attachments = withAttachment.Attachments.ToList() }), "An equal list of attachments looks edited");
        Check((withAttachment with { Attachments = [] }) == original, "Removing the added attachment did not return to the original");
        Check(withAttachment != (original with { Attachments = [attachment with { Name = "b.png" }] }), "A renamed attachment looks equal");
        Check(new VaultItem(NewId(), "f", ItemKind.Login, " ") { Attachments = [attachment] }.HasContent, "An item with only an attachment is blank");
    });
    Test("attachment names get ' (2)', kinds, badges and size limits", () =>
    {
        Check(VaultAttachments.UniqueName(["Договор.pdf"], "Договор.pdf") == "Договор (2).pdf", "No ' (2)' suffix");
        Check(VaultAttachments.UniqueName(["a.zip", "A (2).zip"], "a.zip") == "a (3).zip", "Suffix ignores taken names or case");
        Check(VaultAttachments.UniqueName([], "fresh.txt") == "fresh.txt" && VaultAttachments.UniqueName(["x"], "x") == "x (2)", "Plain names changed");
        Check(VaultAttachments.KindOf("p.JPEG") == AttachmentKind.Image && VaultAttachments.KindOf("d.pdf") == AttachmentKind.Document &&
            VaultAttachments.KindOf("s.7z") == AttachmentKind.Archive && VaultAttachments.KindOf("bin") == AttachmentKind.Other, "Kinds are wrong");
        Check(VaultAttachments.Badge("p.jpeg") == "JPG" && VaultAttachments.Badge("noext") == "FILE" && VaultAttachments.CanPreview("x.webp") &&
            !VaultAttachments.CanPreview("x.pdf"), "Badges or preview kinds are wrong");
        const long MB = 1024 * 1024;
        Check(VaultAttachments.Check(100 * MB) == SizeCheck.Fine && VaultAttachments.Check(100 * MB + 1) == SizeCheck.Warn &&
            VaultAttachments.Check(512 * MB) == SizeCheck.Warn && VaultAttachments.Check(512 * MB + 1) == SizeCheck.TooLarge, "Size thresholds are not 100 and 512 MB");
        var shared = new VaultAttachment("s.bin", AttachmentContent.FromBytes(new byte[1000]));
        var fresh = new VaultAttachment("n.bin", AttachmentContent.FromBytes(new byte[300]));
        var saved = new VaultItem("1", "f", ItemKind.Text, "t") { Attachments = [shared] };
        Check(VaultAttachments.EstimateSize(5000, [(saved with { Attachments = [fresh] }, saved)]) == 4300 &&
            VaultAttachments.EstimateSize(5000, [(saved with { Attachments = [shared, fresh] }, null)]) == 6300,
            "The size estimate does not add new and subtract removed attachments");
    });
    Test("a save over the size limit is refused and the file stays intact", () =>
    {
        string limitPath = Path.Combine(root, "limit.kdbx");
        string id = NewId();
        using (var first = VaultSession.Create(limitPath, password))
            first.SaveItem(new VaultItem(id, first.Folders[1].Id, ItemKind.Text, "Маленький.txt", Text: "до"));
        byte[] before = File.ReadAllBytes(limitPath);
        // The same check as for 512 MB, with a limit just above the current size so the test stays small.
        using var limited = VaultSession.Open(limitPath, password, new AtomicVaultFile(maxFileBytes: before.Length + 50_000));
        var item = limited.GetItem(id);
        var big = item with { Attachments = [new("большой.bin", AttachmentContent.FromBytes(Payload(200_000)))] };
        try { limited.SaveItem(big); throw new Exception("An oversized vault was written"); }
        catch (VaultException ex) { Check(ex.Message.Contains("512 МБ") && ex.Message.Contains("не изменён"), "The refusal does not explain itself"); }
        Check(before.SequenceEqual(File.ReadAllBytes(limitPath)) && limited.FileSize == before.Length, "A refused save changed the file");
        Check(!Directory.EnumerateFiles(root, "limit.kdbx.*.tmp").Any(), "A refused save left a temporary file");
        limited.SaveItem(item with { Text = "после" });
        using var reopened = VaultSession.Open(limitPath, password);
        Check(reopened.GetItem(id).Text == "после" && reopened.GetItem(id).Attachments.Count == 0, "The vault did not work after a refusal");
    });
    Test("a junk file over 512 MB is rejected before anything is read or decrypted", () =>
    {
        string junk = Path.Combine(root, "junk-over-limit.kdbx");
        using (var stream = new FileStream(junk, FileMode.CreateNew)) stream.SetLength(AtomicVaultFile.MaxFileBytes + 1L);
        try
        {
            var watch = Stopwatch.StartNew();
            long memory = GC.GetAllocatedBytesForCurrentThread();
            try { using var opened = VaultSession.Open(junk, password); throw new Exception("A junk file was opened"); }
            catch (VaultException ex) { Check(ex.Message.Contains("512 МБ"), "The size error does not name the limit"); }
            Check(GC.GetAllocatedBytesForCurrentThread() - memory < 16 * 1024 * 1024 && watch.ElapsedMilliseconds < 2000,
                "The junk file was read into memory before the size check");
            Check(new FileInfo(junk).Length == AtomicVaultFile.MaxFileBytes + 1L, "The junk file was changed");
        }
        finally { File.Delete(junk); }
        // A junk file under the limit is still rejected by the header guard before decryption.
        string small = Path.Combine(root, "junk-small.kdbx");
        File.WriteAllBytes(small, Payload(4096));
        Reject(() => VaultSession.Open(small, password), "Junk under the limit was accepted");
        Reject(() => KdbxGuard.Validate(File.ReadAllBytes(small)), "The header guard accepted junk");
    });
    Test("export lists attachment names and sizes, never their bytes; search finds names, not contents", () =>
    {
        string exportVault = Path.Combine(root, "export-attachments.kdbx");
        string id = NewId();
        byte[] text = Encoding.UTF8.GetBytes("SECRET-ATTACHMENT-CONTENT-Вымысел " + Convert.ToBase64String(Payload(200)));
        using var vault = VaultSession.Create(exportVault, password);
        vault.SaveItem(new VaultItem(id, vault.Folders[1].Id, ItemKind.Text, "Документы.txt", Text: "без вложенного текста")
            { Attachments = [new("Квитанция (пример).txt", AttachmentContent.FromBytes(text), DateTime.UtcNow)] });
        string exportPath = Path.Combine(root, "export-attachments.txt");
        vault.ExportText(exportPath);
        string exported = File.ReadAllText(exportPath);
        Check(exported.Contains("Квитанция (пример).txt") && exported.Contains($"\"Size\": {text.Length}"), "Export does not list the attachment");
        Check(!exported.Contains("SECRET-ATTACHMENT-CONTENT") && !exported.Contains(Convert.ToBase64String(text)[..40]),
            "Export contains the bytes of an attachment");
        var culture = System.Globalization.CultureInfo.GetCultureInfo("ru");
        Check(VaultSearch.Find(vault.Items, "квитанция", culture).Single().Id == id, "Quick search does not look at attachment names");
        Check(VaultSearch.Find(vault.Items, "SECRET-ATTACHMENT", culture).Count == 0, "Quick search looked inside an attachment");
    });
    Test("JSON and text previews: formatted, broken JSON as is with its line, UTF-8 and other encodings", () =>
    {
        Check(TextPreview.KindOf("data.JSON") == PreviewKind.Json && TextPreview.KindOf("a.log") == PreviewKind.Text &&
            TextPreview.KindOf("a.md") == PreviewKind.Text && TextPreview.KindOf("p.png") == PreviewKind.Image &&
            TextPreview.KindOf("d.pdf") == PreviewKind.None && VaultAttachments.CanPreview("x.json") && !VaultAttachments.CanPreview("x.zip"),
            "Preview kinds are wrong");
        byte[] json = Encoding.UTF8.GetBytes("{\"имя\":\"Пример\",\"список\":[1,2],\"вложено\":{\"ok\":true}}");
        byte[] copy = (byte[])json.Clone();
        var formatted = TextPreview.Build(copy, json: true);
        Check(formatted.Formatted && formatted.Warning == TextPreviewWarning.None &&
            formatted.Text.Replace("\r\n", "\n") == "{\n  \"имя\": \"Пример\",\n  \"список\": [\n    1,\n    2\n  ],\n  \"вложено\": {\n    \"ok\": true\n  }\n}",
            "JSON was not indented: " + formatted.Text);
        Check(copy.All(b => b == 0), "The plain copy of the bytes was not cleared");
        byte[] withBom = [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes("[1]")];
        Check(TextPreview.Build(withBom, json: true) is { Formatted: true, Warning: TextPreviewWarning.None }, "A UTF-8 BOM broke the JSON");
        string broken = "{\n  \"a\": 1,\n  \"b\": oops\n}";
        var bad = TextPreview.Build(Encoding.UTF8.GetBytes(broken), json: true);
        Check(bad.Text == broken && !bad.Formatted && bad.Warning == TextPreviewWarning.InvalidJson && bad.ErrorLine == 3,
            $"Broken JSON is not shown as is with its line: {bad.ErrorLine}");
        var plain = TextPreview.Build(Encoding.UTF8.GetBytes("a;b\n1;2"), json: false);
        Check(plain.Text == "a;b\n1;2" && plain.Warning == TextPreviewWarning.None, "Plain text changed");
        var latin = TextPreview.Build([0x63, 0x61, 0x66, 0xE9, 0x0A], json: false);
        Check(latin.Text == "caf�\n" && latin.Warning == TextPreviewWarning.NotUtf8, "Non-UTF-8 text gave no warning or no replacement");
        Check(TextPreview.FromText("{\"x\":[]}", json: true).Text.Replace("\r\n", "\n") == "{\n  \"x\": []\n}", "Text from the vault was not formatted");

        // Viewing an attachment never changes it: the preview works on a copy.
        var attachment = new VaultAttachment("data.json", AttachmentContent.FromBytes(json));
        TextPreview.Build(attachment.Content.ReadBytes(), json: true);
        Check(attachment.Content.ReadBytes().SequenceEqual(json), "Viewing changed the attachment bytes");
    });
    Console.WriteLine($"{passed} integration checks passed. Synthetic encrypted test files: {root}");
}
catch (Exception ex)
{
    Console.Error.WriteLine("FAIL " + ex.GetType().Name + ": " + ex.Message);
    Environment.ExitCode = 1;
}
