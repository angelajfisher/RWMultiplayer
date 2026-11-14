namespace Multiplayer.Common.Networking.Packet;

[PacketDefinition(Packets.Client_Traces, allowFragmented: true)]
public record struct ClientTracesPacket : IPacket
{
    public int playerId;
    public byte[] rawTraces;
    public byte[] rawJittedMethods;

    public void Bind(PacketBuffer buf)
    {
        buf.Bind(ref playerId);
        TracePacketLimits.Bind(buf, ref rawTraces);
        TracePacketLimits.Bind(buf, ref rawJittedMethods);
    }
}

[PacketDefinition(Packets.Server_Traces, allowFragmented: true)]
public record struct ServerTracesPacket : IPacket
{
    public enum Mode : byte
    {
        Request, Transfer
    }

    public Mode mode;

    public int tick;
    public int diffAt;
    public int playerId;

    // Used in transfer only
    public byte[] rawTraces;
    public byte[] rawJittedMethods;

    public static ServerTracesPacket Request(int tick, int diffAt, int playerId) => new()
    {
        mode = Mode.Request,
        tick = tick,
        diffAt = diffAt,
        playerId = playerId
    };

    public static ServerTracesPacket Transfer(byte[] rawTraces, byte[] rawJittedMethods) =>
        new() { mode = Mode.Transfer, rawTraces = rawTraces, rawJittedMethods = rawJittedMethods };

    public void Bind(PacketBuffer buf)
    {
        buf.BindEnum(ref mode);
        if (mode == Mode.Request)
        {
            buf.Bind(ref tick);
            buf.Bind(ref diffAt);
            buf.Bind(ref playerId);
        }
        else
        {
            TracePacketLimits.Bind(buf, ref rawTraces);
            TracePacketLimits.Bind(buf, ref rawJittedMethods);
        }
    }
}

// BindBytes historically ignores its maxLength argument. Enforce limits here,
// before allocation on receive, without changing unrelated packet formats.
public static class TracePacketLimits
{
    public static void Bind(PacketBuffer buffer, ref byte[] bytes)
    {
        if (buffer.isWriting)
        {
            if (bytes == null || bytes.Length > TracePayload.MaxCompressedBytes)
                throw new WriterException("Trace field exceeds transfer limit.");
            buffer.Writer.WriteInt32(bytes.Length); buffer.Writer.WriteRaw(bytes);
        }
        else
        {
            int length = buffer.Reader.ReadInt32();
            if (length < 0 || length > TracePayload.MaxCompressedBytes || length > buffer.Reader.Left)
                throw new ReaderException("Invalid trace field length.");
            bytes = buffer.Reader.ReadRaw(length);
        }
    }
}
