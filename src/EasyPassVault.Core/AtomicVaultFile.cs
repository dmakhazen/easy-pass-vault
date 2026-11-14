using System.Security.Cryptography;

namespace EasyPassVault.Core;

// The injectable stage hook is for fault-injection tests, never for handling plaintext.
// The size limit guards against junk files and runaway memory use; it is not a limit of the KDBX format.
// Tests may pass a smaller limit to check the refusal without building a huge file.
public sealed class AtomicVaultFile(Action<string>? stage = null, long maxFileBytes = AtomicVaultFile.MaxFileBytes)
{
    public const int MaxFileBytes = 512 * 1024 * 1024;
    public const string TooLargeMessage = "Хранилище стало бы больше 512 МБ. Изменения не записаны, файл на диске не изменён. Удалите крупные вложения и сохраните снова.";

    /// <summary>Reads a whole vault file. The size is checked before any buffer is allocated.</summary>
    public static byte[] Read(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length is < 12 or > MaxFileBytes)
            throw new VaultException("Недопустимый размер файла хранилища: он пустой или больше 512 МБ. Файл не расшифровывался и не изменён.");
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
        if (ciphertext.Length > maxFileBytes) throw new VaultException(TooLargeMessage);
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
        if (!File.Exists(path) || !CryptographicOperations.FixedTimeEquals(HashFile(path), expected))
            throw new VaultException("Файл изменён или перемещён другой программой. Изменения не записаны. Сначала откройте актуальную копию.");
    }

    // Streams the file: a large vault is hashed without a second copy of it in memory.
    private static byte[] HashFile(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024);
        return SHA256.HashData(stream);
    }
}
