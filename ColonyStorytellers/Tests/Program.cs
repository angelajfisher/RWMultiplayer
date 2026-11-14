using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Serialization;
using System.Threading.Tasks;
using HarmonyLib;
using Multiplayer.API;
using Multiplayer.Client;
using Multiplayer.Client.Comp;
using Multiplayer.Common;
using Multiplayer.Common.Networking.Packet;
using ColonyStorytellers;
using RimWorld;
using RimWorld.Planet;
using Verse;
using MP = Multiplayer.Client.Multiplayer;
using SyncSerialization = Multiplayer.Client.SyncSerialization;

static class Program
{
    static int checks;
    static object valueToRead;
    static bool readTarget;
    static T Empty<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
    static void Set(object obj, string field, object value) => AccessTools.Field(obj.GetType(), field).SetValue(obj, value);
    static void Check(bool ok, string text) { if (!ok) throw new Exception(text); checks++; Console.WriteLine("PASS: " + text); }
    static Faction Faction(int id) { var f = Empty<Faction>(); f.def = new FactionDef { isPlayer = true }; f.loadID = id; return f; }
    static Storyteller MakeStory(Difficulty difficulty)
    {
        var def = Empty<StorytellerDef>(); def.comps = new List<StorytellerCompProperties>();
        return new Storyteller(def, Empty<DifficultyDef>(), difficulty);
    }
    static MultiplayerServer MakeServer(IEnumerable<int> hostOnly)
    {
        var server = MultiplayerServer.instance = new MultiplayerServer(new ServerSettings { direct = false, lan = false });
        server.hostUsername = "Host";
        server.StartInitData().SetResult(new ServerInitData(new byte[0], "1.6", new HashSet<int>(), new HashSet<int>(hostOnly), (RoundModeEnum.ToNearest, RoundModeEnum.ToNearest), new Dictionary<string, DefInfo>()));
        return server;
    }
    static int Main()
    {
        MpReflection.allAssembliesHook = () => AppDomain.CurrentDomain.GetAssemblies();
        var owner = Faction(20); var other = Faction(22); var spectator = Faction(21);
        Current.Game = Empty<Game>();
        var world = Empty<World>(); var factions = Empty<FactionManager>(); Set(factions, "ofPlayer", spectator);
        Set(world, "factionManager", factions); Set(Current.Game, "worldInt", world);
        var difficulty = new Difficulty { threatScale = 1.7f, cropYieldFactor = 0.8f, minThreatPointsRangeCeiling = 90f };
        var a = MakeStory(difficulty); var b = MakeStory(difficulty); var s = MakeStory(new Difficulty { minThreatPointsRangeCeiling = 70 });
        MP.game = Empty<MultiplayerGame>(); MP.game.gameComp = new MultiplayerGameComp { multifaction = true };
        MP.game.worldComp = Empty<MultiplayerWorldComp>(); MP.game.worldComp.spectatorFaction = spectator;
        MP.game.worldComp.factionData = new SortedDictionary<int, FactionWorldData> {
            [20] = new FactionWorldData { storyteller = a }, [22] = new FactionWorldData { storyteller = b }, [21] = new FactionWorldData { storyteller = s }
        };
        Set(Current.Game, "storyteller", s); MP.session = Empty<MultiplayerSession>(); MP.session.client = new DummyConnection("Host");
        var defField = new SyncField(typeof(Storyteller), nameof(Storyteller.def)); defField.hostOnly = true; defField.PostApply((_, __) => Find.Storyteller.Notify_DefChanged());
        var presetField = new SyncField(typeof(Storyteller), nameof(Storyteller.difficultyDef)); presetField.hostOnly = true;
        var difficultyField = new SyncField(typeof(Storyteller), nameof(Storyteller.difficulty)); difficultyField.hostOnly = true;
        var copy = new SyncMethod(typeof(Difficulty), null, AccessTools.Method(typeof(Difficulty), nameof(Difficulty.CopyFrom)), null); copy.hostOnly = true;
        var unrelated = new SyncField(typeof(Difficulty), nameof(Difficulty.threatScale)); unrelated.hostOnly = true;
        SyncFields.SyncStorytellerDef = defField; SyncFields.SyncStorytellerDifficultyDef = presetField; SyncFields.SyncStorytellerDifficulty = difficultyField;
        Sync.handlers = new List<SyncHandler> { defField, presetField, difficultyField, copy, unrelated };
        for (int i = 0; i < Sync.handlers.Count; i++) Sync.handlers[i].syncId = i;
        var before = MakeServer(new[] { 0, 1, 2, 3, 4 });
        var player = new ServerPlayer(2, new DummyConnection("Joiner"));
        before.commands.Send(CommandType.Sync, 22, -1, ByteWriter.GetBytes(0), player);
        Check(before.commands.SentCmds == 0, "baseline server rejects nonhost storyteller setting");
        ColonyStorytellersMod.ApplyPatches();
        Check(StorytellerAccess.Enable() == 4, "exactly four existing storyteller controls are enabled");
        Check(!defField.hostOnly && !presetField.hostOnly && !difficultyField.hostOnly && !copy.hostOnly, "selection, preset, custom difficulty and CopyFrom lose host-only restriction");
        Check(unrelated.hostOnly, "unrelated host-only controls remain restricted");
        Check(StorytellerAccess.Enable() == 4, "permission update is idempotent");
        var allowed = new List<int>(); foreach (var handler in Sync.handlers) if (handler.hostOnly) allowed.Add(handler.syncId);
        var after = MakeServer(allowed);
        for (int id = 0; id < 4; id++) after.commands.Send(CommandType.Sync, 22, -1, ByteWriter.GetBytes(id), player);
        Check(after.commands.SentCmds == 4, "actual server accepts all four nonhost control commands");
        after.commands.Send(CommandType.Sync, 22, -1, ByteWriter.GetBytes(4), player);
        Check(after.commands.SentCmds == 4, "actual server still rejects unrelated host-only command");
        Check(DifficultyIsolation.Repair() == 1, "existing shared difficulty is isolated once");
        Check(!ReferenceEquals(a.difficulty, b.difficulty), "colonies no longer share mutable difficulty");
        Check(b.difficulty.threatScale == 1.7f && b.difficulty.cropYieldFactor == 0.8f, "isolation preserves current custom settings");
        var curve = AccessTools.Field(typeof(Difficulty), "minThreatPointsRangeCeilingCurveCached");
        var clone = DifficultyIsolation.Clone(b.difficulty);
        Check(!ReferenceEquals(curve.GetValue(clone), curve.GetValue(b.difficulty)), "difficulty curve caches are independent");
        Check(DifficultyIsolation.Repair() == 0, "existing-data repair is idempotent");
        var probe = new Harmony("storyteller.smoke.serialization");
        probe.Patch(AccessTools.Method(typeof(SyncSerialization), nameof(SyncSerialization.ReadSyncObject)), prefix: new HarmonyMethod(AccessTools.Method(typeof(Program), nameof(ReadForFixture))));
        foreach (var faction in new[] { owner, other })
        {
            FactionContext.Push(faction); MP.game.worldComp.SetFaction(faction);
            try
            {
                var own = Find.Storyteller; var foreign = faction == owner ? b : a;
                var oldForeignDef = foreign.def; var newDef = Empty<StorytellerDef>(); newDef.comps = new List<StorytellerCompProperties>();
                valueToRead = newDef; defField.Handle(new ByteReader(new byte[0]));
                Check(own.def == newDef && foreign.def == oldForeignDef, "actual storyteller field handler changes only faction " + faction.loadID);
                valueToRead = Empty<DifficultyDef>(); presetField.Handle(new ByteReader(new byte[0]));
                Check(ReferenceEquals(own.difficultyDef, valueToRead), "preset handler updates own faction " + faction.loadID);
                var foreignDifficulty = foreign.difficulty;
                valueToRead = new Difficulty { threatScale = faction.loadID, minThreatPointsRangeCeiling = 70 };
                difficultyField.Handle(new ByteReader(new byte[0]));
                Check(ReferenceEquals(own.difficulty, valueToRead) && ReferenceEquals(foreign.difficulty, foreignDifficulty), "custom difficulty handler is isolated for faction " + faction.loadID);
                var preset = Empty<DifficultyDef>(); preset.threatScale = 0.75f; preset.minThreatPointsRangeCeiling = 70;
                valueToRead = preset; copy.Handle(new ByteReader(new byte[0]));
                Check(own.difficulty.threatScale == 0.75f && ReferenceEquals(foreign.difficulty, foreignDifficulty), "standard-playstyle CopyFrom targets own faction " + faction.loadID);
            }
            finally { FactionContext.Pop(); MP.game.worldComp.SetFaction(spectator); }
        }
        var oldSpec = s.def; valueToRead = Empty<StorytellerDef>(); defField.Handle(new ByteReader(new byte[0]));
        Check(s.def == oldSpec, "spectator storyteller field mutation is rejected");
        float oldScale = s.difficulty.threatScale; valueToRead = Empty<DifficultyDef>(); copy.Handle(new ByteReader(new byte[0]));
        Check(s.difficulty.threatScale == oldScale, "spectator preset CopyFrom is rejected");
        MP.session.client = null;
        Check(StorytellerAccess.MayApply(), "single-player field behavior is not restricted");
        Check(DifficultyIsolation.Repair() == 0, "single-player data is not repaired");
        probe.UnpatchAll(probe.Id);
        Console.WriteLine("Colony Storytellers: " + checks + " checks passed"); return 0;
    }
    static bool ReadForFixture(SyncType syncType, ref object __result)
    {
        if (syncType.type == typeof(Storyteller)) { __result = Find.Storyteller; readTarget = true; }
        else if (syncType.type == typeof(Difficulty) && !readTarget) { __result = Find.Storyteller.difficulty; readTarget = true; }
        else { __result = valueToRead; readTarget = false; }
        return false;
    }
    class DummyConnection : ConnectionBase
    {
        public DummyConnection(string name) { username = name; }
        protected override void SendRaw(byte[] raw, bool reliable = true) { }
        protected override void OnClose(ServerDisconnectPacket? packet) { }
    }
}
