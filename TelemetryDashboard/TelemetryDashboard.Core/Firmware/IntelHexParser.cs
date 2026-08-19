using System;
using System.Collections.Generic;
using System.Globalization;

namespace TelemetryDashboard.Core.Firmware;

/// <summary>A contiguous span of firmware destined for one absolute flash address.</summary>
public sealed class FirmwareSegment
{
    public required uint BaseAddress { get; init; }
    public required byte[] Data { get; init; }
    public int Length => Data.Length;
}

/// <summary>Decoded firmware image: ordered segments plus the total payload size.</summary>
public sealed class FirmwareImage
{
    public required IReadOnlyList<FirmwareSegment> Segments { get; init; }
    public required string Format { get; init; }

    public int TotalBytes
    {
        get
        {
            int total = 0;
            foreach (FirmwareSegment segment in Segments) total += segment.Length;
            return total;
        }
    }

    /// <summary>Lowest address referenced by the image.</summary>
    public uint StartAddress
    {
        get
        {
            uint lowest = uint.MaxValue;
            foreach (FirmwareSegment segment in Segments)
            {
                if (segment.BaseAddress < lowest) lowest = segment.BaseAddress;
            }
            return Segments.Count == 0 ? 0 : lowest;
        }
    }
}

/// <summary>
/// Intel HEX (I8HEX/I16HEX/I32HEX) record decoder.
/// </summary>
/// <remarks>
/// A .hex file is ASCII text describing addressed records; it is not a flash image. The previous
/// flasher streamed the file's raw bytes, so an MCU received the literal characters
/// <c>:10010000...</c> instead of firmware — a transfer that reports success and leaves the device
/// unprogrammed or bricked. This decodes records into absolute-addressed binary segments and
/// verifies each record's checksum.
/// </remarks>
public static class IntelHexParser
{
    private const byte RecordData = 0x00;
    private const byte RecordEndOfFile = 0x01;
    private const byte RecordExtendedSegmentAddress = 0x02;
    private const byte RecordStartSegmentAddress = 0x03;
    private const byte RecordExtendedLinearAddress = 0x04;
    private const byte RecordStartLinearAddress = 0x05;

    /// <summary>True when the text looks like Intel HEX rather than a raw binary blob.</summary>
    public static bool LooksLikeIntelHex(ReadOnlySpan<char> text)
    {
        foreach (char c in text)
        {
            if (char.IsWhiteSpace(c)) continue;
            return c == ':';
        }
        return false;
    }

    /// <summary>
    /// Parses Intel HEX text into address-ordered segments.
    /// </summary>
    /// <exception cref="FormatException">A record is malformed or fails its checksum.</exception>
    public static FirmwareImage Parse(string hexText)
    {
        ArgumentNullException.ThrowIfNull(hexText);

        var blocks = new SortedDictionary<uint, List<byte>>();
        uint upperAddress = 0;
        bool sawEndOfFile = false;
        int lineNumber = 0;

        foreach (string rawLine in hexText.Split('\n'))
        {
            lineNumber++;
            string line = rawLine.Trim();
            if (line.Length == 0) continue;

            if (line[0] != ':')
            {
                throw new FormatException($"Line {lineNumber}: Intel HEX records must begin with ':'.");
            }

            byte[] record = DecodeRecord(line, lineNumber);

            byte byteCount = record[0];
            ushort offset = (ushort)((record[1] << 8) | record[2]);
            byte recordType = record[3];

            switch (recordType)
            {
                case RecordData:
                {
                    uint absolute = upperAddress + offset;
                    var data = new byte[byteCount];
                    Array.Copy(record, 4, data, 0, byteCount);
                    Append(blocks, absolute, data);
                    break;
                }

                case RecordExtendedLinearAddress:
                    // Upper 16 bits of a 32-bit address.
                    upperAddress = (uint)((record[4] << 8) | record[5]) << 16;
                    break;

                case RecordExtendedSegmentAddress:
                    // Segment base, paragraph-aligned (x16).
                    upperAddress = (uint)((record[4] << 8) | record[5]) << 4;
                    break;

                case RecordEndOfFile:
                    sawEndOfFile = true;
                    break;

                case RecordStartSegmentAddress:
                case RecordStartLinearAddress:
                    break; // entry point metadata, not flash content

                default:
                    throw new FormatException($"Line {lineNumber}: unsupported Intel HEX record type 0x{recordType:X2}.");
            }

            if (sawEndOfFile) break;
        }

        if (blocks.Count == 0)
        {
            throw new FormatException("Intel HEX file contains no data records.");
        }

        var segments = new List<FirmwareSegment>(blocks.Count);
        foreach (KeyValuePair<uint, List<byte>> entry in blocks)
        {
            segments.Add(new FirmwareSegment { BaseAddress = entry.Key, Data = entry.Value.ToArray() });
        }

        return new FirmwareImage { Segments = segments, Format = "hex" };
    }

    /// <summary>Decodes one record's hex digits and verifies its trailing checksum byte.</summary>
    private static byte[] DecodeRecord(string line, int lineNumber)
    {
        string body = line[1..].TrimEnd('\r');
        if (body.Length < 10 || body.Length % 2 != 0)
        {
            throw new FormatException($"Line {lineNumber}: record length {body.Length} is not a valid hex payload.");
        }

        var bytes = new byte[body.Length / 2];
        for (int i = 0; i < bytes.Length; i++)
        {
            if (!byte.TryParse(body.AsSpan(i * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out bytes[i]))
            {
                throw new FormatException($"Line {lineNumber}: '{body.Substring(i * 2, 2)}' is not a hex byte.");
            }
        }

        byte declaredCount = bytes[0];
        if (bytes.Length != declaredCount + 5)
        {
            throw new FormatException(
                $"Line {lineNumber}: declared byte count {declaredCount} does not match record length {bytes.Length}.");
        }

        // Checksum is the two's complement of the sum of every preceding byte.
        int sum = 0;
        for (int i = 0; i < bytes.Length - 1; i++) sum += bytes[i];

        byte expected = (byte)((~sum + 1) & 0xFF);
        byte actual = bytes[^1];
        if (expected != actual)
        {
            throw new FormatException(
                $"Line {lineNumber}: checksum mismatch (expected 0x{expected:X2}, found 0x{actual:X2}).");
        }

        return bytes;
    }

    /// <summary>Merges a data run into the block that it continues, or starts a new one.</summary>
    private static void Append(SortedDictionary<uint, List<byte>> blocks, uint address, byte[] data)
    {
        foreach (KeyValuePair<uint, List<byte>> entry in blocks)
        {
            uint end = entry.Key + (uint)entry.Value.Count;
            if (end == address)
            {
                entry.Value.AddRange(data);
                return;
            }
        }

        blocks[address] = new List<byte>(data);
    }
}
