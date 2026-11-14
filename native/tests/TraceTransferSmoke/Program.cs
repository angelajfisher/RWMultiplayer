using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using Multiplayer.Client;
using Multiplayer.Common;
using Multiplayer.Common.Networking.Packet;
using MP = Multiplayer.Client.Multiplayer;

static class Program
{
    static int checks;
    static T Empty<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
    static void Check(bool value, string label) { if (!value) throw new Exception(label); checks++; Console.WriteLine("PASS: " + label); }
    static int Main(string[] args)
    {
        foreach (var name in new[] { "ZstdSharp", "System.Memory", "System.Buffers", "System.Runtime.CompilerServices.Unsafe", "System.Threading.Tasks.Extensions", "System.Numerics.Vectors" })
        {
            var assembly = Assembly.LoadFrom(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, name + ".dll"));
            foreach (var type in assembly.GetTypes())
                foreach (var field in type.GetFields(BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                { var fieldType = field.FieldType; }
            Check(true, "startup type/field scan: " + name);
        }
        MP.settings = Empty<MpSettings>(); MP.settings.desyncTracesRadius = 1;
        MP.game = Empty<MultiplayerGame>(); MP.game.sync = new SyncCoordinator();
        var opinion = new ClientSyncOpinion(77);
        var random = new Random(9);
        for (int i = 0; i < 3000; i++)
        {
            var data = new byte[40]; random.NextBytes(data);
            opinion.AddDesyncTrace(new StackTraceLogItemObj { tick = 100, traceEvent = i + 1, hash = i, info1 = Convert.ToBase64String(data) });
        }
        MP.game.sync.knownClientOpinions.Add(opinion);
        string report = opinion.GetFormattedStackTracesForRange(1);
        Check(report.Contains("\n0 Tick:") && report.Contains("\n2999 Tick:"), "default report exports beginning and recent tail despite radius=1");
        Check(report.Contains("captures 256..951 not retained"), "export identifies the evicted middle gap");
        Check(opinion.desyncStackTraces.Count == 2304, "retained memory stays bounded at 256+2048 stacks");
        var compressed = TracePayload.Compress(report);
        Check(compressed[4] == 1, "real game Mono uses Zstandard rather than gzip fallback");
        Check(compressed.Length > ConnectionBase.MaxSinglePacketSize, "report exercises the previous 64KiB failure boundary");
        Check(TracePayload.Decompress(compressed) == report, "complete report survives codec round trip");
        var server = MultiplayerServer.instance = new MultiplayerServer(new ServerSettings { direct = false, lan = false });
        server.hostUsername = "Host";
        var host = new Wire(); var input = new Wire { username = "Host" }; var relay = new Wire(); var joiner = new Wire();
        input.serverPlayer = new ServerPlayer(1, input);
        server.playerManager.Players.Add(input.serverPlayer); server.playerManager.Players.Add(new ServerPlayer(2, relay));
        string received = null;
        input.Attach(new Handler(input, Packets.Client_Traces, reader =>
        {
            var packet = new ClientTracesPacket(); packet.Bind(new PacketReader(reader));
            new ServerPlayingState(input).HandleTraces(packet);
        }));
        joiner.Attach(new Handler(joiner, Packets.Server_Traces, reader =>
        {
            var packet = new ServerTracesPacket(); packet.Bind(new PacketReader(reader));
            received = TracePayload.Decompress(packet.rawTraces);
            Check(!string.IsNullOrEmpty(TracePayload.Decompress(packet.rawJittedMethods)), "method-list payload decodes at joiner");
        }));
        host.Target = input; relay.Target = joiner;
        // Invoke the production host request handler, production server relay,
        // and actual fragment reassembly on both legs, all on the game's Mono.
        new ClientPlayingState(host).HandleTraces(ServerTracesPacket.Request(77, 1, 2));
        Check(host.Sent > 1 && relay.Sent > 1, "both production trace transfer legs fragment successfully");
        Check(received == report, "joiner receives the complete head/tail report unchanged");
        foreach (var path in Directory.GetFiles(args[0], "419-*traces.txt"))
        {
            string text = File.ReadAllText(path); var payload = TracePayload.Compress(text);
            Check(payload[4] == 1 && TracePayload.Decompress(payload) == text, "real report round trip: " + Path.GetFileName(path));
            Console.WriteLine("SIZE " + Path.GetFileName(path) + " text=" + System.Text.Encoding.UTF8.GetByteCount(text) + " zstd=" + payload.Length);
        }
        Console.WriteLine("Trace transfer smoke: " + checks + " checks passed"); return 0;
    }
    class Handler(ConnectionBase conn, Packets packet, Action<ByteReader> receive) : MpConnectionState(conn)
    {
        public override PacketHandlerInfo GetPacketHandler(Packets id) => id == packet ? new PacketHandlerInfo((_, reader) => receive(reader), true) : null;
    }
    class Wire : ConnectionBase
    {
        public Wire Target; public int Sent;
        public Wire() => typeof(ConnectionBase).GetProperty(nameof(State)).SetValue(this, ConnectionStateEnum.ClientPlaying);
        public void Attach(MpConnectionState state) => typeof(ConnectionBase).GetProperty(nameof(StateObj)).SetValue(this, state);
        protected override void SendRaw(byte[] raw, bool reliable = true) { Sent++; Target?.HandleReceiveRaw(new ByteReader(raw), reliable); }
        protected override void OnClose(ServerDisconnectPacket? packet) { }
    }
}
