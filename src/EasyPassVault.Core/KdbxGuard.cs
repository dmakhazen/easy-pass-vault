using System.Text;

namespace EasyPassVault.Core;

// This parses only public framing/limits, never decrypts or implements cryptography.
// Resource checks must precede a library's allocation of attacker-controlled KDF memory.
public static class KdbxGuard
{
    private static readonly byte[] Argon2id = Convert.FromHexString("9E298B1956DB4773B23DFC3EC6F0A1E6");
    public static void Validate(byte[] bytes)
    {
        try
        {
            using var input = new BinaryReader(new MemoryStream(bytes), Encoding.UTF8);
            if (input.ReadUInt32() != 0x9AA2D903 || input.ReadUInt32() != 0xB54BFB67)
                throw new VaultException("Это не файл хранилища KDBX.");
            uint version = input.ReadUInt32();
            if (version is < 0x00040000 or > 0x00040001)
                throw new VaultException("Поддерживаются только KDBX 4.0 и 4.1.");
            bool kdfSeen = false;
            var seen = new HashSet<byte>();
            while (true)
            {
                byte field = input.ReadByte();
                int length = input.ReadInt32();
                if (length < 0 || length > 65536 || input.BaseStream.Position + length > Math.Min(bytes.Length, 65536))
                    throw new VaultException("Повреждён заголовок хранилища.");
                byte[] value = input.ReadBytes(length);
                if (!seen.Add(field)) throw new VaultException("Повторяющееся поле заголовка.");
                if (field == 11) { ValidateKdf(value); kdfSeen = true; }
                if (field == 3 && (value.Length != 4 || BitConverter.ToUInt32(value) != 0))
                    throw new VaultException("Первая версия открывает KDBX без сжатия. Для совместимости отключите сжатие в исходном клиенте.");
                if (field == 0) break;
            }
            if (!kdfSeen) throw new VaultException("Отсутствуют параметры получения ключа.");
        }
        catch (EndOfStreamException ex) { throw new VaultException("Файл хранилища обрезан или повреждён.", ex); }
    }

    private static void ValidateKdf(byte[] bytes)
    {
        using var reader = new BinaryReader(new MemoryStream(bytes), Encoding.UTF8);
        if (reader.ReadUInt16() != 0x0100) throw new VaultException("Неизвестная версия параметров KDF.");
        var values = new Dictionary<string, (byte Type, byte[] Data)>();
        while (true)
        {
            byte type = reader.ReadByte();
            if (type == 0) break;
            int keySize = reader.ReadInt32();
            if (keySize is < 1 or > 128 || keySize > reader.BaseStream.Length - reader.BaseStream.Position)
                throw new VaultException("Повреждены параметры KDF.");
            string key = Encoding.UTF8.GetString(reader.ReadBytes(keySize));
            int size = reader.ReadInt32();
            if (size < 0 || size > reader.BaseStream.Length - reader.BaseStream.Position)
                throw new VaultException("Повреждены параметры KDF.");
            if (!values.TryAdd(key, (type, reader.ReadBytes(size)))) throw new VaultException("Повторяющийся параметр KDF.");
        }
        if (reader.BaseStream.Position != reader.BaseStream.Length) throw new VaultException("Лишние данные в параметрах KDF.");
        byte[] Get(string key, byte type, int size)
        {
            if (!values.TryGetValue(key, out var value) || value.Type != type || value.Data.Length != size)
                throw new VaultException("Отсутствует или повреждён параметр KDF: " + key);
            return value.Data;
        }
        if (!Get("$UUID", 0x42, 16).SequenceEqual(Argon2id))
            throw new VaultException("В первой версии поддерживается только Argon2id. Другие KDF не изменены и не конвертированы.");
        ulong memory = BitConverter.ToUInt64(Get("M", 0x05, 8));
        ulong rounds = BitConverter.ToUInt64(Get("I", 0x05, 8));
        uint parallel = BitConverter.ToUInt32(Get("P", 0x04, 4));
        uint version = BitConverter.ToUInt32(Get("V", 0x04, 4));
        if (memory < 8 * 1024 || memory > 256UL * 1024 * 1024 || rounds is < 1 or > 10 || parallel is < 1 or > 8 || version != 0x13)
            throw new VaultException("Параметры Argon2id выходят за поддерживаемые пределы (до 256 МБ, 10 проходов, 8 потоков).");
        if (memory / 1024 < 8 * parallel) throw new VaultException("Недопустимые параметры памяти Argon2id.");
        if (!values.TryGetValue("S", out var salt) || salt.Type != 0x42 || salt.Data.Length is < 8 or > 64)
            throw new VaultException("Недопустимая соль KDF.");
        if (values.Keys.Any(k => k is not ("$UUID" or "M" or "I" or "P" or "V" or "S")))
            throw new VaultException("Неподдерживаемые дополнительные параметры KDF.");
    }
}
