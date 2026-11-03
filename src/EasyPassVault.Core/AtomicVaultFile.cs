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
        if (ciphertext.Length > MaxFileBytes) throw new VaultException("Хранилище превышает ограничение 32 МБ.");
        path = Path.GetFullPath(path);
        string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        // A cooperating second instance cannot pass the commit check concurrently.
        string lockPath = path + ".epv-lock";
        FileStream? gate = null;
        try
        {
            gate = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None,
                1, FileOptions.DeleteOnClose);
            VerifyCurrent(path, expectedHash);
            using (var output = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                       64 * 1024, FileOptions.WriteThrough))
            {
                output.Write(ciphertext);
                output.Flush(flushToDisk: true);
            }
            stage?.Invoke("after-flush");
            VerifyCurrent(path, expectedHash);
            stage?.Invoke("before-replace");
            if (expectedHash is null) File.Move(temp, path, overwrite: false);
            else File.Replace(temp, path, null, ignoreMetadataErrors: false);
        }
        finally
        {
            // Only remove the temporary file owned by this operation. Never delete the original.
            if (File.Exists(temp)) File.Delete(temp);
            gate?.Dispose();
        }
    }

    private static void VerifyCurrent(string path, byte[]? expected)
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
