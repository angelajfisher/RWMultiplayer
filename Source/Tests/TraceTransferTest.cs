using System.Reflection;
using Multiplayer.Common;
using Multiplayer.Common.Networking.Packet;

namespace Tests;

[NonParallelizable]
public class TraceTransferTest
{
    [Test]
    public void HostServerJoinerTransferActuallyFragmentsAndReassemblesLargeReports()
    {
        var previous = MultiplayerServer.instance;
        try
        {
            var server = MultiplayerServer.instance = new MultiplayerServer(new ServerSettings { direct = false, lan = false });
            server.hostUsername = "Host";
            var hostSender = new Wire(); var serverInput = new Wire { username = "Host" }; var relay = new Wire(); var joiner = new Wire();
            serverInput.serverPlayer = new ServerPlayer(1, serverInput);
            server.playerManager.Players.Add(serverInput.serverPlayer);
            server.playerManager.Players.Add(new ServerPlayer(2, relay));
            ServerTracesPacket? received = null;
            serverInput.Attach(new Handler(serverInput, Packets.Client_Traces, reader =>
            {
                var packet = new ClientTracesPacket(); packet.Bind(new PacketReader(reader));
                new ServerPlayingState(serverInput).HandleTraces(packet);
            }));
            joiner.Attach(new Handler(joiner, Packets.Server_Traces, reader =>
            {
                var packet = new ServerTracesPacket(); packet.Bind(new PacketReader(reader)); received = packet;
            }));
            hostSender.Target = serverInput; relay.Target = joiner;
            var bytes = new byte[250000]; new Random(88).NextBytes(bytes);
            string text = Convert.ToBase64String(bytes);
            byte[] packed = TracePayload.Compress(text);
            hostSender.SendFragmented(new ClientTracesPacket { playerId = 2, rawTraces = packed, rawJittedMethods = packed }.Serialize());
            Assert.That(hostSender.Sent, Is.GreaterThan(1));
            Assert.That(relay.Sent, Is.GreaterThan(1));
            Assert.That(hostSender.Largest, Is.LessThanOrEqualTo(ConnectionBase.MaxFragmentPacketSize));
            Assert.That(relay.Largest, Is.LessThanOrEqualTo(ConnectionBase.MaxFragmentPacketSize));
            Assert.That(received.HasValue, Is.True);
            Assert.That(TracePayload.Decompress(received!.Value.rawTraces), Is.EqualTo(text));
            Assert.That(TracePayload.Decompress(received.Value.rawJittedMethods), Is.EqualTo(text));
            // A nonhost must not be able to relay trace payloads to another player.
            serverInput.username = "NotHost"; received = null;
            hostSender.SendFragmented(new ClientTracesPacket { playerId = 2, rawTraces = packed, rawJittedMethods = packed }.Serialize());
            Assert.That(received.HasValue, Is.False);
        }
        finally { MultiplayerServer.instance = previous; }
    }

    private class Handler(ConnectionBase conn, Packets packet, Action<ByteReader> receive) : MpConnectionState(conn)
    {
        public override PacketHandlerInfo? GetPacketHandler(Packets id) => id == packet ? new((_, reader) => receive(reader), true) : null;
    }

    private class Wire : ConnectionBase
    {
        public Wire? Target;
        public int Sent, Largest;
        public Wire() => typeof(ConnectionBase).GetProperty(nameof(State))!.SetValue(this, ConnectionStateEnum.ClientPlaying);
        public void Attach(MpConnectionState state) => typeof(ConnectionBase).GetProperty(nameof(StateObj))!.SetValue(this, state);
        protected override void SendRaw(byte[] raw, bool reliable = true)
        {
            Sent++; Largest = Math.Max(Largest, raw.Length);
            Target?.HandleReceiveRaw(new ByteReader(raw), reliable);
        }
        protected override void OnClose(ServerDisconnectPacket? goodbye) { }
    }
}
