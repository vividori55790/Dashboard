namespace TelemetryDashboard.Core.Parsers;

public static class XorChecksum
{
    /// <summary>
    /// Calculates XOR checksum over a char span without heap allocations.
    /// </summary>
    public static byte Calculate(ReadOnlySpan<char> span)
    {
        byte checksum = 0;
        for (int i = 0; i < span.Length; i++)
        {
            checksum ^= (byte)span[i];
        }
        return checksum;
    }

    public static byte Calculate(ReadOnlySpan<byte> span)
    {
        byte checksum = 0;
        for (int i = 0; i < span.Length; i++)
        {
            checksum ^= span[i];
        }
        return checksum;
    }

    public static byte CalculateSpan(ReadOnlySpan<char> span) => Calculate(span);
    public static byte CalculateSpan(ReadOnlySpan<byte> span) => Calculate(span);

    /// <summary>
    /// Validates payload format "$[TAG],...*[XOR_HEX]".
    /// Returns true if valid XOR hex match.
    /// </summary>
    public static bool ValidateSpan(ReadOnlySpan<char> rawLine, out ReadOnlySpan<char> contentSpan)
    {
        contentSpan = ReadOnlySpan<char>.Empty;
        if (rawLine.IsEmpty) return false;

        int starIdx = rawLine.LastIndexOf('*');
        if (starIdx < 0 || starIdx + 2 >= rawLine.Length) return false;

        int startIdx = rawLine.StartsWith("$") ? 1 : 0;
        contentSpan = rawLine.Slice(startIdx, starIdx - startIdx);

        ReadOnlySpan<char> hexSpan = rawLine.Slice(starIdx + 1, 2);
        if (byte.TryParse(hexSpan, System.Globalization.NumberStyles.HexNumber, null, out byte expectedChecksum))
        {
            byte computed = Calculate(contentSpan);
            return computed == expectedChecksum;
        }

        return false;
    }

    public static string AppendChecksum(string payload)
    {
        if (string.IsNullOrEmpty(payload)) return "$*00\r\n";
        ReadOnlySpan<char> span = payload.AsSpan().TrimStart('$');
        byte cs = Calculate(span);
        return $"${span}*{cs:X2}\r\n";
    }
}
