using System.IO.Compression;
using System.Text;
using Multiplayer.Common;
using Multiplayer.Common.Networking.Packet;

namespace Tests;

public class TracePayloadTest
{
    [TestCase("")]
    [TestCase("Full trace: pawn 🦊 / Nagi’s colony\nline two\n")]
    public void ZstdRoundTripsUnicodeAndEmpty(string text)
    {
        var bytes = TracePayload.Compress(text);
        Assert.That(bytes[4], Is.EqualTo(1), "managed Zstandard should work, rather than silently using fallback");
        Assert.That(TracePayload.Decompress(bytes), Is.EqualTo(text));
    }

    [Test]
    public void LargeReportsAndMethodsRoundTripAcrossBothPacketFormats()
    {
        var data = new byte[250000]; new Random(123).NextBytes(data);
        string text = Convert.ToBase64String(data);
        byte[] packed = TracePayload.Compress(text);
        Assert.That(packed.Length, Is.GreaterThan(ConnectionBase.MaxSinglePacketSize));
        var host = new ClientTracesPacket { playerId = 4, rawTraces = packed, rawJittedMethods = packed };
        var hostBytes = host.Serialize().data;
        var decodedHost = BinderOf.Identity<ClientTracesPacket>().Deserialize(hostBytes);
        var relay = ServerTracesPacket.Transfer(decodedHost.rawTraces, decodedHost.rawJittedMethods);
        var decodedRelay = BinderOf.Identity<ServerTracesPacket>().Deserialize(relay.Serialize().data);
        Assert.That(TracePayload.Decompress(decodedRelay.rawTraces), Is.EqualTo(text));
        Assert.That(TracePayload.Decompress(decodedRelay.rawJittedMethods), Is.EqualTo(text));
    }

    [Test]
    public void LegacyGzipAndEnvelopeGzipRemainReadable()
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Optimal, true))
        {
            var text = Encoding.UTF8.GetBytes("previous report"); gzip.Write(text);
        }
        var bytes = output.ToArray();
        Assert.That(TracePayload.Decompress(bytes), Is.EqualTo("previous report"));
        using var envelope = new MemoryStream(); using var writer = new BinaryWriter(envelope);
        writer.Write(0x3154504D); writer.Write((byte)0); writer.Write(15); writer.Write(bytes);
        Assert.That(TracePayload.Decompress(envelope.ToArray()), Is.EqualTo("previous report"));
    }

    [Test]
    public void CorruptionTruncationAndUnboundedExpansionAreRejected()
    {
        byte[] packed = TracePayload.Compress(new string('a', 50000));
        byte[] corrupt = (byte[])packed.Clone(); corrupt[^1] ^= 1;
        Assert.Catch(() => TracePayload.Decompress(corrupt));
        Assert.Catch(() => TracePayload.Decompress(packed[..^1]));
        corrupt = (byte[])packed.Clone(); corrupt[4] = 255;
        Assert.Throws<InvalidDataException>(() => TracePayload.Decompress(corrupt));
        corrupt = (byte[])packed.Clone(); BitConverter.GetBytes(1).CopyTo(corrupt, 5);
        Assert.Throws<InvalidDataException>(() => TracePayload.Decompress(corrupt));
        corrupt = (byte[])packed.Clone(); BitConverter.GetBytes(TracePayload.MaxUncompressedBytes + 1).CopyTo(corrupt, 5);
        Assert.Throws<InvalidDataException>(() => TracePayload.Decompress(corrupt));
        Assert.Throws<InvalidDataException>(() => TracePayload.Decompress(new byte[TracePayload.MaxCompressedBytes + 1]));
    }

    [Test]
    public void TraceFieldLimitsAreCheckedBeforeReceiveAllocation()
    {
        var invalid = new ByteWriter(); invalid.WriteInt32(4); invalid.WriteInt32(TracePayload.MaxCompressedBytes + 1);
        Assert.Throws<ReaderException>(() => BinderOf.Identity<ClientTracesPacket>().Deserialize(invalid.ToArray()));
        invalid = new ByteWriter(); invalid.WriteInt32(4); invalid.WriteInt32(-1);
        Assert.Throws<ReaderException>(() => BinderOf.Identity<ClientTracesPacket>().Deserialize(invalid.ToArray()));
        var packet = new ClientTracesPacket { rawTraces = new byte[TracePayload.MaxCompressedBytes + 1], rawJittedMethods = [] };
        Assert.Throws<WriterException>(() => packet.Serialize());
    }
}
