using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using ZstdSharp;
using ZstdSharp.Unsafe;

namespace Multiplayer.Common;

// Compression occurs only when preparing a diagnostic transfer, never while ticking.
public static class TracePayload
{
    public const int MaxCompressedBytes = 8 * 1024 * 1024;
    public const int MaxUncompressedBytes = 32 * 1024 * 1024;
    public const int MaxReportChars = 8 * 1024 * 1024 - 1024;
    private const int Magic = 0x3154504D; // MPT1
    private const int HeaderLength = 9;
    private const byte Gzip = 0, Zstandard = 1;
    private static readonly UTF8Encoding Utf8 = new(false, true);

    public static byte[] Compress(string text)
    {
        int length = Utf8.GetByteCount(text);
        if (length > MaxUncompressedBytes) throw new InvalidDataException("Trace text exceeds decompressed size limit.");
        byte[] input = Utf8.GetBytes(text);
        byte codec = Zstandard;
        byte[] compressed;
        try { compressed = CompressZstd(input); }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            // Keep diagnostics available if a particular Mono cannot run the port.
            codec = Gzip;
            using var output = new MemoryStream();
            using (var stream = new GZipStream(output, CompressionLevel.Optimal, true))
                stream.Write(input, 0, input.Length);
            compressed = output.ToArray();
        }
        if (compressed.Length > MaxCompressedBytes - HeaderLength)
            throw new InvalidDataException("Compressed trace exceeds transfer size limit.");
        using var envelope = new MemoryStream();
        using var writer = new BinaryWriter(envelope);
        writer.Write(Magic); writer.Write(codec); writer.Write(length); writer.Write(compressed);
        return envelope.ToArray();
    }

    private static byte[] CompressZstd(byte[] input)
    {
        using var output = new MemoryStream();
        using (var stream = new CompressionStream(output, 9, leaveOpen: true))
        {
            stream.SetParameter(ZSTD_cParameter.ZSTD_c_checksumFlag, 1);
            stream.Write(input, 0, input.Length);
        }
        return output.ToArray();
    }

    public static string Decompress(byte[] payload)
    {
        if (payload == null || payload.Length > MaxCompressedBytes || payload.Length < 2)
            throw new InvalidDataException("Invalid trace payload size.");
        using var input = new MemoryStream(payload, false);
        byte codec;
        int expected;
        if (payload.Length >= HeaderLength && BitConverter.ToInt32(payload, 0) == Magic)
        {
            input.Position = 4; codec = (byte)input.ReadByte();
            using var reader = new BinaryReader(input, Encoding.UTF8, true);
            expected = reader.ReadInt32();
            if (expected < 0 || expected > MaxUncompressedBytes)
                throw new InvalidDataException("Invalid declared trace length.");
        }
        else if (payload[0] == 0x1F && payload[1] == 0x8B) { codec = Gzip; expected = -1; }
        else throw new InvalidDataException("Unknown trace payload format.");
        using Stream stream = codec switch
        {
            Zstandard => ZstdStream(input),
            Gzip => new GZipStream(input, CompressionMode.Decompress, true),
            _ => throw new InvalidDataException("Unsupported trace compression codec.")
        };
        using var output = new MemoryStream();
        var buffer = new byte[8192];
        int count;
        while ((count = stream.Read(buffer, 0, buffer.Length)) != 0)
        {
            if (output.Length + count > MaxUncompressedBytes || (expected >= 0 && output.Length + count > expected))
                throw new InvalidDataException("Expanded trace exceeds declared/maximum size.");
            output.Write(buffer, 0, count);
        }
        if (expected >= 0 && output.Length != expected) throw new InvalidDataException("Truncated trace payload.");
        return Utf8.GetString(output.ToArray());
    }

    private static Stream ZstdStream(Stream input)
    {
        var stream = new DecompressionStream(input, checkEndOfStream: true, leaveOpen: true);
        stream.SetParameter(ZSTD_dParameter.ZSTD_d_windowLogMax, 25);
        return stream;
    }
}
