using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Serialization;
using HarmonyLib;
using Multiplayer.Client;
using Multiplayer.Client.AsyncTime;
using Multiplayer.Client.Comp;
using Multiplayer.Common;
using Multiplayer.Common.Networking.Packet;
using ThreatPauseVotes;
using RimWorld;
using RimWorld.Planet;
using Verse;
using MP = Multiplayer.Client.Multiplayer;

static class Program
{
    static int checks;
    static bool throwLetter;
    static MethodInfo pauseMethod;
    static T Empty<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
    static void Check(bool ok, string label) { if (!ok) throw new Exception(label); checks++; Console.WriteLine("PASS: " + label); }
    static void Set(object obj, string field, object value) => AccessTools.Field(obj.GetType(), field).SetValue(obj, value);
    static Faction Faction(int id) { var f = Empty<Faction>(); f.def = new FactionDef { isPlayer = true }; f.loadID = id; return f; }
    static void Seed(MultiplayerGameComp game)
    {
        foreach (var id in new[] { -1, 1, 2 })
            foreach (int player in new[] { 1, 2 }) game.playerData[player].SetTimeVote(id, TimeVote.Superfast);
    }
    static void Apply(ClientCommandPacket packet, int playerId)
    {
        Check(packet.type == CommandType.TimeSpeedVote && packet.mapId == ScheduledCommand.Global, "normal synchronized vote command is emitted");
        var cmd = new ScheduledCommand(packet.type, 0, 20, packet.mapId, playerId, packet.data);
        AccessTools.Method(typeof(AsyncWorldTimeComp), "HandleTimeVote").Invoke(MP.AsyncWorldTime, new object[] { cmd, new ByteReader(packet.data) });
    }
    static int Main()
    {
        Current.Game = Empty<Game>();
        var maps = new List<Map>(); Set(Current.Game, "maps", maps);
        var owner = Faction(20); var other = Faction(22); var spectator = Faction(21);
        var world = Empty<World>(); var fm = Empty<FactionManager>();
        Set(fm, "ofPlayer", owner); Set(fm, "allFactions", new List<Faction> { owner, other, spectator });
        Set(world, "factionManager", fm); Set(Current.Game, "worldInt", world);
        MP.game = Empty<MultiplayerGame>();
        MP.game.worldComp = Empty<MultiplayerWorldComp>(); MP.game.worldComp.spectatorFaction = spectator;
        MP.game.myFactionLoading = owner;
        var game = MP.game.gameComp = new MultiplayerGameComp { asyncTime = true, timeControl = TimeControl.LowestWins };
        MP.game.asyncWorldTimeComp = Empty<AsyncWorldTimeComp>(); MP.game.asyncTimeComps = new List<AsyncTimeComp>();
        foreach (int id in new[] { 1, 2 })
        {
            var map = Empty<Map>(); map.uniqueID = id; maps.Add(map);
            var time = Empty<AsyncTimeComp>(); Set(time, "map", map); time.DesiredTimeSpeed = TimeSpeed.Superfast;
            MP.game.asyncTimeComps.Add(time);
        }
        MP.session = Empty<MultiplayerSession>(); var connection = new DummyConnection(); MP.session.client = connection; MP.session.playerId = 1;
        game.playerData = new Dictionary<int, PlayerData> { [1] = new(), [2] = new(), [3] = new() }; Seed(game);
        AsyncTimeComp.executingCmdMap = maps[0];
        var pause = AccessTools.Method(AccessTools.TypeByName("Multiplayer.Client.AsyncTime.ReceiveLetterPause"), "PauseOnLetter");
        pauseMethod = pause;
        ThreatPauseVotesMod.ApplyPatches();
        pause.Invoke(null, new object[] { null });
        Check(connection.packets.Count == 1, "recipient client requests exactly one map pause vote");
        Check(game.playerData[1].GetTimeVoteOrNull(1) == TimeVote.Superfast && game.playerData[2].GetTimeVoteOrNull(1) == TimeVote.Superfast, "client-specific detection never mutates shared votes directly");
        Apply(connection.packets[0], 1);
        Check(game.playerData[1].GetTimeVoteOrNull(1) == TimeVote.Paused, "scheduled command pauses the recipient's vote");
        Check(game.playerData[2].GetTimeVoteOrNull(1) == TimeVote.Superfast, "other player's selected speed is preserved");
        Check(game.GetLowestTimeVote(1) == TimeSpeed.Paused, "recipient's paused vote protects the shared clock");
        Check(game.playerData[1].GetTimeVoteOrNull(2) == TimeVote.Superfast && game.playerData[1].GetTimeVoteOrNull(-1) == TimeVote.Superfast, "async scope leaves other map/world votes unchanged");
        game.playerData[1].SetTimeVote(1, TimeVote.Normal);
        Check(game.GetLowestTimeVote(1) == TimeSpeed.Normal, "only recipient must choose a speed to release this pause");
        connection.packets.Clear(); MP.game.myFactionLoading = other; MP.session.playerId = 2;
        pause.Invoke(null, new object[] { null });
        Check(connection.packets.Count == 0, "other colony does not send an automatic pause vote");
        Check(game.playerData[2].GetTimeVoteOrNull(1) == TimeVote.Superfast, "nonrecipient vote remains unchanged on its own client");
        MP.game.myFactionLoading = spectator; MP.session.playerId = 3; pause.Invoke(null, new object[] { null });
        Check(connection.packets.Count == 0 && game.playerData[3].GetTimeVoteOrNull(1) == null, "spectator/offline entries never gain pause votes");
        MP.game.myFactionLoading = owner; MP.session.playerId = 1;
        game.playerData[1].SetTimeVote(1, TimeVote.PlayerResetTickable); pause.Invoke(null, new object[] { null });
        Check(connection.packets.Count == 1, "online recipient with a null vote can still protect their threat");
        connection.packets.Clear(); game.asyncTime = false; Seed(game); pause.Invoke(null, new object[] { null });
        Check(connection.packets.Count == 3, "synchronous pause covers world and both map controls");
        foreach (var packet in connection.packets) Apply(packet, 1);
        foreach (int id in new[] { -1, 1, 2 })
            Check(game.playerData[1].GetTimeVoteOrNull(id) == TimeVote.Paused && game.playerData[2].GetTimeVoteOrNull(id) == TimeVote.Superfast, "sync scope preserves nonrecipient speed for tickable " + id);
        game.playerData[1].SetTimeVote(-1, TimeVote.PlayerResetGlobal);
        Check(game.GetLowestTimeVote(-1) == TimeSpeed.Superfast, "disconnect/desync resets still release recipient pause");
        Seed(game); game.ResetAllTimeVotes(1);
        Check(game.playerData[1].GetTimeVoteOrNull(1) == null && game.playerData[2].GetTimeVoteOrNull(1) == null, "ordinary manual resets remain unchanged");
        connection.packets.Clear(); MP.session.replay = true; pause.Invoke(null, new object[] { null }); MP.session.replay = false;
        Check(connection.packets.Count == 0, "replays never emit new vote commands");
        TickPatch.simulating = new SimulatingData { target = 100 }; pause.Invoke(null, new object[] { null }); TickPatch.simulating = null;
        Check(connection.packets.Count == 0, "catch-up simulation never emits historical vote commands");
        MP.session.desynced = true; pause.Invoke(null, new object[] { null }); MP.session.desynced = false;
        Check(connection.packets.Count == 0, "desynced clients never emit pause requests");
        Check(PauseVotes.Receives(null, owner, spectator) && PauseVotes.Receives(null, other, spectator), "untargeted world events pause all receiving colonies");
        // Optional quest routing uses the real installed addon without patching it.
        Assembly.LoadFrom(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "MultiplayerQuestFactionFix.dll"));
        var quest = new Quest(); var letter = new StandardLetter { quest = quest };
        Check(PauseVotes.Receives(letter, owner, owner) && PauseVotes.Receives(letter, other, owner), "public quest routing overrides a single ambient faction");
        var pawn = Empty<Pawn>(); Set(pawn, "factionInt", owner); Set(quest, "accepterPawn", pawn);
        Check(PauseVotes.Receives(letter, owner, other) && !PauseVotes.Receives(letter, other, owner), "quest owner routing takes precedence over ambient context");
        var integration = new Harmony("pause-votes.letter-context-fixture");
        integration.Patch(AccessTools.Method(typeof(LetterStack), nameof(LetterStack.ReceiveLetter), new[] { typeof(Letter), typeof(string), typeof(int), typeof(bool) }),
            prefix: new HarmonyMethod(AccessTools.Method(typeof(Program), nameof(ReceiveForFixture))) { priority = int.MinValue });
        game.asyncTime = true; MP.game.myFactionLoading = owner; MP.session.playerId = 1;
        Set(fm, "ofPlayer", other); connection.packets.Clear();
        new LetterStack().ReceiveLetter(letter, null, 0, false);
        Check(connection.packets.Count == 1, "actual letter scope routes pause by quest owner despite another ambient faction");
        connection.packets.Clear(); throwLetter = true; bool caught = false;
        try { new LetterStack().ReceiveLetter(letter, null, 0, false); } catch (InvalidOperationException) { caught = true; }
        throwLetter = false;
        Check(caught, "letter-processing exception is exercised");
        pause.Invoke(null, new object[] { null });
        Check(connection.packets.Count == 0, "letter context is cleared after exceptions and cannot leak recipient ownership");
        integration.UnpatchAll(integration.Id);
        Console.WriteLine("Threat Pause Votes: " + checks + " checks passed"); return 0;
    }
    static bool ReceiveForFixture()
    {
        if (throwLetter) throw new InvalidOperationException("fixture");
        pauseMethod.Invoke(null, new object[] { null });
        return false;
    }
    sealed class DummyConnection : ConnectionBase
    {
        public List<ClientCommandPacket> packets = new();
        protected override void Send(Packets id, byte[] message, bool reliable = true)
        {
            var packet = new ClientCommandPacket(); packet.Bind(new PacketReader(new ByteReader(message))); packets.Add(packet);
        }
        protected override void SendRaw(byte[] data, bool reliable = true) { }
        protected override void OnClose(ServerDisconnectPacket? packet) { }
    }
}
