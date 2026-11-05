using System.Security.Cryptography;

namespace EasyPassVault.Core;

// The injectable stage hook is for fault-injection tests, never for handling plaintext.
public sealed class AtomicVaultFile(Action<string>? stage = null)
{
    public const int MaxFileBytes = 32 * 1024 * 1024;

    public static byte[] Read(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length is < 12 or > MaxFileBytes) throw new VaultException("Недопустимый размер хранилища (максимум 32 МБ).");
        var bytes = new byte[(int)stream.Length];
        stream.ReadExactly(bytes);
        return bytes;
    }

    public void Write(string path, byte[] ciphertext, byte[]? expectedHash)
    {
        path = Path.GetFullPath(path);
        WriteCore(path + ".epv-lock", () => { VerifyUnchanged(path, expectedHash); return path; }, ciphertext,
            replace: expectedHash is not null);
    }

    /// <summary>
    /// Writes a brand-new file and never touches existing ones. <paramref name="resolve"/> runs under
    /// <paramref name="lockPath"/>, checks preconditions and returns the destination; it runs again just
    /// before the final move and must return the same destination.
    /// </summary>
    public string WriteNew(string lockPath, Func<string> resolve, byte[] ciphertext)
    {
        string? destination = null;
        WriteCore(Path.GetFullPath(lockPath), () =>
        {
            string resolved = Path.GetFullPath(resolve());
            if (destination is not null && !string.Equals(destination, resolved, StringComparison.OrdinalIgnoreCase))
                throw new VaultException(VaultVersions.NewerVersionExists);
            if (File.Exists(resolved))
                throw new VaultException("Файл уже существует. Выберите другое имя; существующее хранилище не перезаписано.");
            return destination = resolved;
        }, ciphertext, replace: false);
        return destination!;
    }

    private void WriteCore(string lockPath, Func<string> check, byte[] ciphertext, bool replace)
    {
        if (ciphertext.Length > MaxFileBytes) throw new VaultException("Хранилище превышает ограничение 32 МБ.");
        string? temp = null;
        FileStream? gate = null;
        try
        {
            // A cooperating second instance cannot pass the commit check concurrently.
            gate = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None,
                1, FileOptions.DeleteOnClose);
            string path = check();
            temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            using (var output = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                       64 * 1024, FileOptions.WriteThrough))
            {
                output.Write(ciphertext);
                output.Flush(flushToDisk: true);
            }
            stage?.Invoke("after-flush");
            check();
            stage?.Invoke("before-replace");
            if (replace) File.Replace(temp, path, null, ignoreMetadataErrors: false);
            else File.Move(temp, path, overwrite: false);
        }
        finally
        {
            // Only remove the temporary file owned by this operation. Never delete the original.
            if (temp is not null && File.Exists(temp)) File.Delete(temp);
            gate?.Dispose();
        }
    }

    public static void VerifyUnchanged(string path, byte[]? expected)
    {
        if (expected is null)
        {
            if (File.Exists(path)) throw new VaultException("Файл уже существует. Выберите другое имя; существующее хранилище не перезаписано.");
            return;
        }
        if (!File.Exists(path) || !CryptographicOperations.FixedTimeEquals(SHA256.HashData(Read(path)), expected))
            throw new VaultException("Файл изменён или перемещён другой программой. Изменения не записаны. Сначала откройте актуальную копию.");
    }
}
