using System;
using System.Collections.Generic;
using System.Collections;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.Serialization;
using HarmonyLib;
using Multiplayer.Client;
using Multiplayer.Client.Comp;
using Multiplayer.Common;
using Multiplayer.Common.Networking.Packet;
using MultiplayerQuestFactionFix;
using RimWorld;
using RimWorld.Planet;
using Verse;
using MP = Multiplayer.Client.Multiplayer;

static partial class Program
{
    static Faction observed;
    static Archive observedArchive;
    static int checks;
    static bool throwDuringRecruitment;

    static T Empty<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
    static void Set(object obj, string field, object value) => AccessTools.Field(obj.GetType(), field).SetValue(obj, value);
    static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        checks++;
        Console.WriteLine("PASS: " + message);
    }

    public static int Main(string[] args)
    {
        LoadMarriageAssemblies();
        var spectator = MakeFaction(21, "Spectator");
        var colony = MakeFaction(20, "Jelli's faction");
        var other = MakeFaction(22, "Other colony");
        var game = Empty<Game>();
        var world = Empty<World>();
        var manager = Empty<FactionManager>();
        Set(manager, "ofPlayer", spectator);
        Set(manager, "allFactions", new List<Faction> { spectator, colony, other });
        Set(world, "factionManager", manager);
        Set(game, "worldInt", world);
        Set(game, "maps", new List<Map>());
        Current.Game = game;

        MP.game = Empty<MultiplayerGame>();
        MP.game.worldComp = Empty<MultiplayerWorldComp>();
        MP.game.worldComp.spectatorFaction = spectator;
        MP.game.worldComp.factionData = new SortedDictionary<int, FactionWorldData>();
        var colonyHistory = Empty<History>();
        var spectatorHistory = Empty<History>();
        var otherHistory = Empty<History>();
        Set(otherHistory, "archive", new Archive());
        var colonyArchive = new Archive();
        var spectatorArchive = new Archive();
        Set(colonyHistory, "archive", colonyArchive);
        Set(spectatorHistory, "archive", spectatorArchive);
        Set(game, "history", spectatorHistory);
        MP.game.worldComp.factionData[colony.loadID] = new FactionWorldData { history = colonyHistory };
        MP.game.worldComp.factionData[other.loadID] = new FactionWorldData { history = otherHistory };
        MP.game.worldComp.factionData[spectator.loadID] = new FactionWorldData { history = spectatorHistory };
        MP.game.gameComp = new MultiplayerGameComp { multifaction = true, asyncTime = true };
        MP.game.mapComps = new List<MultiplayerMapComp>();
        MP.game.asyncTimeComps = new List<AsyncTimeComp>();
        MP.game.myFactionLoading = colony;
        MP.session = Empty<MultiplayerSession>();
        MP.session.client = new DummyConnection();

        var parent = Empty<Settlement>();
        Set(parent, "factionInt", colony);
        parent.def = new WorldObjectDef { canBePlayerHome = true };
        var map = Empty<Map>();
        map.info = new MapInfo { parent = parent };
        Current.Game.Maps.Add(map);
        var time = Empty<AsyncTimeComp>();
        Set(time, "map", map);
        MP.game.asyncTimeComps.Add(time);

        var quest = new Quest { id = 233, name = "Olga's Dusty Nomads" };
        var join = new QuestPart_JoinPlayer { mapParent = parent, inSignal = "Quest233.Accepted3" };
        Set(join, "quest", quest);
        Set(quest, "parts", new List<QuestPart> { join });
        var offer = new QuestPart_PawnJoinOffer { outSignalPawnAccepted = join.inSignal };
        Set(offer, "quest", quest);

        Check(QuestOwnership.Owner(quest) == colony, "saved colony target wins over spectator context");
        Check(QuestOwnership.ShouldShow(quest, colony), "own quest remains visible");
        Check(!QuestOwnership.ShouldShow(quest, other), "other colony's quest is hidden");
        Check(!QuestOwnership.ShouldShow(quest, spectator), "spectator cannot see colony-owned quests in the list");
        Check(QuestOwnership.ShouldShow(new Quest(), colony), "public quest remains visible");
        var otherParent = Empty<Settlement>();
        Set(otherParent, "factionInt", other);
        var globalQuest = new Quest { id = 300 };
        Set(globalQuest, "parts", new List<QuestPart> {
            new QuestPart_JoinPlayer { mapParent = parent },
            new QuestPart_JoinPlayer { mapParent = otherParent }
        });
        Check(QuestOwnership.Owner(globalQuest) == null, "multiple colony targets do not acquire an arbitrary first owner");
        Check(QuestOwnership.ShouldShow(globalQuest, colony) && QuestOwnership.ShouldShow(globalQuest, other), "multi-colony quest is visible to both colonies");
        Check(QuestOwnership.FindHomeTime(globalQuest) == null, "multi-colony quest keeps world timing");
        var failure = new StandardLetter { quest = quest };
        Check(QuestLetterRouting.Recipients(failure).Count == 1 && QuestLetterRouting.Recipients(failure)[0] == colony, "private failure notification excludes other colonies and spectator");
        Check(QuestLetterRouting.Recipients(new StandardLetter { quest = globalQuest }).Count == 2, "public quest notification reaches both colonies");
        Check(QuestOwnership.JoinOfferTarget(offer) == parent, "join offer resolves its exact recruitment signal target");

        var logs = new Harmony("quest-fix.smoke.logs");
        logs.Patch(AccessTools.Method(typeof(Log), nameof(Log.Message), new[] { typeof(string) }),
            prefix: new HarmonyMethod(typeof(Program).GetMethod(nameof(PrintLog), BindingFlags.Static | BindingFlags.NonPublic)));
        QuestFactionFixMod.ApplyPatches();
        var window = Empty<MainTabWindow_Quests>();
        var listQuest = AccessTools.Method(typeof(MainTabWindow_Quests), "ShouldListNow");
        Check((bool)listQuest.Invoke(window, new object[] { quest }), "patched quest list shows own available quest");
        MP.game.myFactionLoading = other;
        Check(!(bool)listQuest.Invoke(window, new object[] { quest }), "patched quest list hides another colony's quest");
        MP.game.myFactionLoading = colony;
        Check((bool)listQuest.Invoke(window, new object[] { new Quest() }), "patched quest list keeps public quests");
        quest.hidden = true;
        Check(!(bool)listQuest.Invoke(window, new object[] { quest }), "vanilla hidden-quest rules still apply");
        quest.hidden = false;
        var lookup = AccessTools.Method(typeof(MultiplayerAsyncQuest), "TryGetQuestMap");
        Check(ReferenceEquals(lookup.Invoke(null, new object[] { quest }), time), "patched quest lookup finds home map while spectator is current");
        Check(Faction.OfPlayer == spectator && FactionContext.stack.Count == 0, "map lookup restores spectator context");

        var probe = new Harmony("quest-fix.smoke.probe");
        probe.Patch(AccessTools.Method(typeof(QuestPart_JoinPlayer), nameof(QuestPart_JoinPlayer.Notify_QuestSignalReceived)),
            prefix: new HarmonyMethod(typeof(Program).GetMethod(nameof(ObserveFaction), BindingFlags.Static | BindingFlags.NonPublic)) { priority = Priority.Last },
            postfix: new HarmonyMethod(typeof(Program).GetMethod(nameof(MaybeThrow), BindingFlags.Static | BindingFlags.NonPublic)));
        join.Notify_QuestSignalReceived(new Signal(join.inSignal));
        Check(observed == colony, "recruitment runs as target colony even when invoked as spectator");
        Check(observedArchive == colonyArchive, "quest actions use colony archive instead of spectator archive");
        Check(Faction.OfPlayer == spectator && FactionContext.stack.Count == 0, "recruitment finalizer restores context");
        Check(Find.Archive == spectatorArchive, "recruitment restores the previous archive context");
        var contextProbe = new Harmony("quest-fix.smoke.external-signals");
        contextProbe.Patch(AccessTools.Method(typeof(Quest), nameof(Quest.End)), prefix: new HarmonyMethod(AccessTools.Method(typeof(Program), nameof(ObserveAndSkip))) { priority = Priority.Last });
        contextProbe.Patch(AccessTools.Method(typeof(Quest), nameof(Quest.Notify_SignalReceived)), prefix: new HarmonyMethod(AccessTools.Method(typeof(Program), nameof(ObserveAndSkip))) { priority = Priority.Last });
        quest.End(QuestEndOutcome.Fail);
        Check(observed == colony && observedArchive == colonyArchive && Faction.OfPlayer == spectator, "quest ending outside QuestTick uses owner archive and restores context");
        quest.Notify_SignalReceived(new Signal("External.Test"));
        Check(observed == colony && Faction.OfPlayer == spectator, "externally delivered quest signal runs as the owner");
        contextProbe.UnpatchAll(contextProbe.Id);
        throwDuringRecruitment = true;
        bool caught = false;
        try { join.Notify_QuestSignalReceived(new Signal(join.inSignal)); }
        catch (InvalidOperationException) { caught = true; }
        throwDuringRecruitment = false;
        Check(caught && Faction.OfPlayer == spectator && FactionContext.stack.Count == 0, "Harmony finalizer restores context when recruitment throws");
        MP.game.gameComp.asyncTime = false;
        join.Notify_QuestSignalReceived(new Signal(join.inSignal));
        Check(observed == colony && Faction.OfPlayer == spectator, "recruitment context also works with async time disabled");

        try
        {
            using (QuestFactionScope.Enter(colony, null))
            {
                Check(Faction.OfPlayer == colony, "scope selects colony faction");
                throw new InvalidOperationException("test exception");
            }
        }
        catch (InvalidOperationException) { }
        Check(Faction.OfPlayer == spectator && FactionContext.stack.Count == 0, "scope restores context after exceptions");

        TestStructReturns();
        TestDryads();
        TestLetters(quest, globalQuest, colony, other, spectator);
        TestMarriageOffers(quest, colony, other, spectator);
        if (args.Length > 0 && File.Exists(args[0])) TestSharedVisibility(args[0], quest, other, world);
        MP.session.client = null;
        Check(!QuestOwnership.Enabled, "singleplayer is unaffected");
        Console.WriteLine($"Quest faction addon: {checks} checks passed");
        return 0;
    }

    static Faction MakeFaction(int id, string name)
    {
        var faction = Empty<Faction>();
        faction.def = new FactionDef { isPlayer = true };
        Set(faction, "loadID", id);
        Set(faction, "name", name);
        return faction;
    }

    static void TestStructReturns()
    {
        var method = new DynamicMethod("DefaultAcceptanceReport", typeof(AcceptanceReport), Type.EmptyTypes, typeof(Program).Module, true);
        var il = method.GetILGenerator();
        var codes = (IEnumerable<CodeInstruction>)AccessTools.Method(typeof(SyncTemplates), "CreateDefaultCodes")
            .Invoke(null, new object[] { il, typeof(AcceptanceReport) });
        foreach (var code in codes)
        {
            if (code.operand is LocalBuilder local) il.Emit(code.opcode, local);
            else if (code.operand is Type type) il.Emit(code.opcode, type);
            else il.Emit(code.opcode);
        }
        il.Emit(OpCodes.Ret);
        var result = (Func<AcceptanceReport>)method.CreateDelegate(typeof(Func<AcceptanceReport>));
        for (int i = 0; i < 10000; i++)
        {
            var report = result();
            if (report.Accepted || report.Reason != null) throw new Exception("Corrupt AcceptanceReport return");
        }
        Check(true, "generated struct return safely returns default AcceptanceReport 10,000 times on game Mono");
        var sync = new Harmony("quest-fix.smoke.sync-template");
        sync.Patch(AccessTools.Method(typeof(SyncTemplates), "General"), prefix: new HarmonyMethod(AccessTools.Method(typeof(Program), nameof(CancelSyncOriginal))));
        var original = AccessTools.Method(typeof(Program), nameof(OutReportProbe));
        var ids = (IDictionary)AccessTools.Field(typeof(Sync), "methodBaseToInternalId").GetValue(null);
        ids[original] = 99999;
        sync.Patch(original, transpiler: SyncTemplates.CreateTranspiler());
        var args = new object[] { null };
        var returned = (AcceptanceReport)original.Invoke(null, args);
        Check(!returned.Accepted && returned.Reason == null && !((AcceptanceReport)args[0]).Accepted && ((AcceptanceReport)args[0]).Reason == null,
            "actual sync transpiler returns and initializes out structs safely");
        sync.UnpatchAll(sync.Id);
    }

    static bool CancelSyncOriginal(ref bool __result) { __result = false; return false; }
    static AcceptanceReport OutReportProbe(out AcceptanceReport report) { report = true; return true; }

    static void TestDryads()
    {
        var pawn = Empty<Pawn>();
        pawn.def = Empty<ThingDef>();
        pawn.def.race = new RaceProperties();
        pawn.def.race.animalType = AnimalType.Dryad;
        var state = Empty<Verse.AI.Pawn_MindState>();
        Set(state, "pawn", pawn);
        DryadHealingFix.HealingPreference.Initialize(state);
        Check(state.returnToHealingPod, "new dryad defaults to healing pods enabled");
        state.thinkData = new Dictionary<int, int>();
        state.priorityWork = Empty<PriorityWork>();
        DryadHealingFix.DryadHealingFixMod.ApplyPatches();
        state.Reset(false, false, false);
        Check(state.returnToHealingPod, "real mind-state reset preserves enabled dryad preference");
        state.returnToHealingPod = false;
        state.Reset(false, false, false);
        Check(!state.returnToHealingPod, "real mind-state reset preserves deliberately disabled preference");
        Scribe.mode = LoadSaveMode.PostLoadInit;
        DryadHealingFix.HealingPreference.Expose(state);
        Check(state.returnToHealingPod, "existing dryad is enabled on first load");
        state.returnToHealingPod = false;
        DryadHealingFix.HealingPreference.Expose(state);
        Check(!state.returnToHealingPod, "migration does not override later manual disable");
        Scribe.mode = LoadSaveMode.Inactive;
    }

    static void TestLetters(Quest quest, Quest publicQuest, Faction colony, Faction other, Faction spectator)
    {
        Set(Current.Game, "uniqueIDsManager", new UniqueIDsManager());
        var stack = new LetterStack();
        Set(Current.Game, "letterStack", stack);
        var deliveryProbe = new Harmony("quest-fix.smoke.letter-body");
        deliveryProbe.Patch(AccessTools.Method(typeof(LetterStack), nameof(LetterStack.ReceiveLetter), new[] { typeof(Letter), typeof(string), typeof(int), typeof(bool) }),
            prefix: new HarmonyMethod(AccessTools.Method(typeof(Program), nameof(ReceiveForFixture))) { priority = Priority.Last });
        MP.game.myFactionLoading = other;
        var privateLetter = new StandardLetter { quest = quest, ID = 8000 };
        stack.ReceiveLetter(privateLetter, null, 0, false);
        Check(!stack.LettersListForReading.Contains(privateLetter), "private failure is removed from nonowner UI after other postfixes");
        Check(MP.game.worldComp.factionData[colony.loadID].history.archive.Contains(privateLetter), "private failure is archived for its owner");
        Check(!MP.game.worldComp.factionData[spectator.loadID].history.archive.Contains(privateLetter), "private failure never enters spectator archive");
        var publicLetter = new StandardLetter { quest = publicQuest, ID = 8001 };
        stack.ReceiveLetter(publicLetter, null, 0, false);
        var clone = stack.LettersListForReading.Find(l => (l as ChoiceLetter)?.quest == publicQuest);
        Check(clone != null && clone != publicLetter && clone.ID != publicLetter.ID, "public notification reaches second colony with a unique saved letter ID");
        Check(MP.game.worldComp.factionData[other.loadID].history.archive.Contains(clone), "second colony's public notification is archived and actionable by ID");
        Check(Faction.OfPlayer == spectator && FactionContext.stack.Count == 0, "letter delivery restores faction context");
        MP.game.myFactionLoading = colony;
        deliveryProbe.UnpatchAll(deliveryProbe.Id);
    }

    static bool ReceiveForFixture(LetterStack __instance, Letter let)
    {
        __instance.LettersListForReading.Add(let);
        Find.Archive.Add(let);
        return false;
    }

    static void TestSharedVisibility(string filtersPath, Quest quest, Faction invitee, World world)
    {
        var filters = Assembly.LoadFrom(filtersPath);
        var core = Assembly.LoadFrom(Path.Combine(Path.GetDirectoryName(filtersPath), "RaidAnimalFix.dll"));
        var helper = core.GetType("RaidAnimalFix.MpHelper", throwOnError: true);
        var patch = new Harmony("quest-fix.smoke.sharing");
        patch.Patch(AccessTools.Method(helper, "IsMultiplayer"), prefix: new HarmonyMethod(
            typeof(Program).GetMethod(nameof(MultiplayerForFixture), BindingFlags.Static | BindingFlags.NonPublic)));
        patch.Patch(AccessTools.Method(helper, "GetCurrentPlayerFaction"), prefix: new HarmonyMethod(
            typeof(Program).GetMethod(nameof(ViewerForFixture), BindingFlags.Static | BindingFlags.NonPublic)));
        var dataType = filters.GetType("RaidAnimalFix.RafSharedQuestData", throwOnError: true);
        var data = (WorldComponent)Activator.CreateInstance(dataType, world);
        Set(world, "components", new List<WorldComponent> { data });
        var entry = dataType.GetMethod("GetOrCreate").Invoke(data, new object[] { quest.id, 20 });
        var invited = (IList)entry.GetType().GetField("invited").GetValue(entry);
        var participants = (IList)entry.GetType().GetField("participants").GetValue(entry);
        var sharingType = filters.GetType("RaidAnimalFix.SharedQuestListVisibility", throwOnError: true);
        var listMethod = AccessTools.Method(typeof(MainTabWindow_Quests), "ShouldListNow");
        patch.Patch(listMethod, postfix: new HarmonyMethod(AccessTools.Method(sharingType, "Postfix")) { priority = Priority.Last });
        var window = Empty<MainTabWindow_Quests>();
        var previous = MP.game.myFactionLoading;
        MP.game.myFactionLoading = invitee;
        invited.Add(invitee.loadID);
        Check((bool)listMethod.Invoke(window, new object[] { quest }), "actual sharing postfix keeps pending invitations visible");
        invited.Clear();
        participants.Add(invitee.loadID);
        Check(QuestLetterRouting.Recipients(new StandardLetter { quest = quest }).Contains(invitee), "accepted ally receives shared quest outcome notifications");
        Set(quest, "acceptanceTick", 0);
        var tab = AccessTools.Field(typeof(MainTabWindow_Quests), "curTab");
        tab.SetValue(window, Enum.ToObject(tab.FieldType, 1));
        Check((bool)listMethod.Invoke(window, new object[] { quest }), "actual sharing postfix keeps joined participants' quests visible");
        quest.hidden = true;
        Check(!(bool)listMethod.Invoke(window, new object[] { quest }), "sharing still respects vanilla hidden-quest rules");
        quest.hidden = false;
        participants.Clear();
        Check(!(bool)listMethod.Invoke(window, new object[] { quest }), "uninvited nonparticipants still cannot see the other colony's quest");
        MP.game.myFactionLoading = previous;
    }

    static bool MultiplayerForFixture(ref bool __result) { __result = true; return false; }
    static bool ViewerForFixture(ref Faction __result) { __result = MP.RealPlayerFaction; return false; }

    static bool PrintLog(string text) { Console.WriteLine(text); return false; }
    static void ObserveFaction() { observed = RimWorld.Faction.OfPlayer; observedArchive = Find.Archive; }
    static bool ObserveAndSkip() { ObserveFaction(); return false; }
    static void MaybeThrow()
    {
        if (throwDuringRecruitment) throw new InvalidOperationException("simulated recruitment failure");
    }

    sealed class DummyConnection : ConnectionBase
    {
        protected override void SendRaw(byte[] raw, bool reliable = true) { }
        protected override void OnClose(ServerDisconnectPacket? goodbye) { }
    }
}
