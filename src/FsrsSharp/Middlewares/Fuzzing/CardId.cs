using System.Globalization;
using System.Text;

namespace FsrsSharp.Middlewares;

/// <summary>Card id helpers (ts-fsrs <c>middlewares/fuzzing/card-id.ts</c>). A card id is a non-empty string or a finite number.</summary>
public static class CardId
{
    /// <summary>Creates a random RFC 4122 version 4 UUID string.</summary>
    public static string Create() => Guid.NewGuid().ToString();

    /// <summary>Formats 16 random bytes as a UUID v4 string (sets the version and variant bits on a copy).</summary>
    public static string FormatUuidV4(ReadOnlySpan<byte> randomBytes)
    {
        if (randomBytes.Length != 16) throw new ArgumentException("Expected 16 bytes", nameof(randomBytes));
        Span<byte> bytes = stackalloc byte[16];
        randomBytes.CopyTo(bytes);
        bytes[6] = (byte)((bytes[6] & 0x0f) | 0x40);
        bytes[8] = (byte)((bytes[8] & 0x3f) | 0x80);
        var hex = new StringBuilder(32);
        foreach (var b in bytes) hex.Append(b.ToString("x2", CultureInfo.InvariantCulture));
        var s = hex.ToString();
        return $"{s[..8]}-{s[8..12]}-{s[12..16]}-{s[16..20]}-{s[20..]}";
    }

    /// <summary>Whether <paramref name="value"/> is a valid card id: a non-empty string or a finite number.</summary>
    public static bool IsValid(object? value) => value switch
    {
        string s => s.Length > 0,
        double d => double.IsFinite(d),
        float f => float.IsFinite(f),
        sbyte or byte or short or ushort or int or uint or long or ulong or decimal => true,
        _ => false,
    };

    /// <summary>JavaScript-style string conversion used when building the fuzz seed (<c>`${cardId}${reps}`</c>).</summary>
    public static string ToSeedString(object cardId) => cardId switch
    {
        string s => s,
        double d => Format(d),
        float f => Format(f),
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => cardId.ToString() ?? string.Empty,
    };

    private static string Format(double d) =>
        d == Math.Floor(d) && Math.Abs(d) < 1e15 ? ((long)d).ToString(CultureInfo.InvariantCulture) : d.ToString("R", CultureInfo.InvariantCulture);
}
