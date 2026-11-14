using System.Security.Cryptography;

namespace EasyPassVault.Core;

/// <summary>Generator settings remembered between runs. Never the generated password itself.</summary>
public sealed record PasswordOptions
{
    public const int MinLength = 8, MaxLength = 64, DefaultLength = 20;

    public int Length { get; init; } = DefaultLength;
    public bool Upper { get; init; } = true;
    public bool Lower { get; init; } = true;
    public bool Digits { get; init; } = true;
    public bool Symbols { get; init; } = true;
    public bool ExcludeSimilar { get; init; }

    /// <summary>A hand-edited or damaged length becomes the nearest allowed one.</summary>
    public PasswordOptions Normalize() => this with { Length = Math.Clamp(Length, MinLength, MaxLength) };
}

public enum PasswordStrength { Weak, Medium, Strong }

/// <summary>
/// Random passwords from <see cref="RandomNumberGenerator"/>: every character is chosen uniformly
/// (GetInt32 rejects out-of-range values, so there is no modulo bias) and every chosen set occurs at least once.
/// </summary>
public static class PasswordGenerator
{
    public const string UpperSet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
    public const string LowerSet = "abcdefghijklmnopqrstuvwxyz";
    public const string DigitSet = "0123456789";
    public const string SymbolSet = "!#$%&*+-.:=?@_~";
    /// <summary>Characters that are easy to confuse when read or typed by hand.</summary>
    public const string SimilarChars = "O0l1I";

    /// <summary>The enabled character sets, without similar characters when asked.</summary>
    public static IReadOnlyList<string> Sets(PasswordOptions options)
    {
        var sets = new List<string>(4);
        void Add(bool enabled, string set)
        {
            if (!enabled) return;
            string chars = options.ExcludeSimilar ? new string(set.Where(c => !SimilarChars.Contains(c)).ToArray()) : set;
            if (chars.Length > 0) sets.Add(chars);
        }
        Add(options.Upper, UpperSet);
        Add(options.Lower, LowerSet);
        Add(options.Digits, DigitSet);
        Add(options.Symbols, SymbolSet);
        return sets;
    }

    public static bool CanGenerate(PasswordOptions options) => Sets(options).Count > 0;

    public static int AlphabetSize(PasswordOptions options) => Sets(options).Sum(set => set.Length);

    /// <summary>Length × log2(alphabet size); 0 when nothing is chosen.</summary>
    public static double EntropyBits(PasswordOptions options)
    {
        int size = AlphabetSize(options);
        return size == 0 ? 0 : options.Normalize().Length * Math.Log2(size);
    }

    /// <summary>Below 50 bits weak, 50–80 medium, above 80 strong.</summary>
    public static PasswordStrength Strength(double bits) =>
        bits < 50 ? PasswordStrength.Weak : bits <= 80 ? PasswordStrength.Medium : PasswordStrength.Strong;

    public static string Generate(PasswordOptions options)
    {
        options = options.Normalize();
        var sets = Sets(options);
        if (sets.Count == 0) throw new InvalidOperationException("No character set is chosen.");
        string alphabet = string.Concat(sets);
        var chars = new char[options.Length];
        // One character of every set first (length ≥ 8 > 4 sets), the rest from the whole alphabet, then shuffle.
        for (int i = 0; i < chars.Length; i++)
        {
            string from = i < sets.Count ? sets[i] : alphabet;
            chars[i] = from[RandomNumberGenerator.GetInt32(from.Length)];
        }
        for (int i = chars.Length - 1; i > 0; i--)
        {
            int j = RandomNumberGenerator.GetInt32(i + 1);
            (chars[i], chars[j]) = (chars[j], chars[i]);
        }
        string result = new(chars);
        Array.Clear(chars);
        return result;
    }
}
