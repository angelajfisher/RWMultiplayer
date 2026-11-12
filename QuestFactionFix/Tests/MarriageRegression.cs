using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using HarmonyLib;
using Multiplayer.Client;
using MultiplayerQuestFactionFix;
using RimWorld;
using Verse;
using MP = Multiplayer.Client.Multiplayer;

static partial class Program
{
    static Type marriageLetterType, marriageHandler;
    static int marriageQuestEnds;

    static void LoadMarriageAssemblies()
    {
        // Install API doubles before Harmony compiles the actual Decide body:
        // Mono can inline these getters while building its patched wrapper.
        var apiProbe = new Harmony("quest-fix.smoke.marriage-api");
        foreach (string name in new[] { "IsInMultiplayer", "IsExecutingSyncCommand" })
            apiProbe.Patch(AccessTools.PropertyGetter(typeof(Multiplayer.API.MP), name),
                prefix: new HarmonyMethod(AccessTools.Method(typeof(Program), nameof(TrueForMarriageFixture))));
        var directory = AppDomain.CurrentDomain.BaseDirectory;
        var romance = Assembly.LoadFrom(Path.Combine(directory, "RomanceOnTheRim.dll"));
        var menu = Assembly.LoadFrom(Path.Combine(directory, "MenuMultiplayerFix.dll"));
        marriageLetterType = romance.GetType(DiplomaticMarriageOffers.LetterTypeName, true);
        marriageHandler = menu.GetType("MenuMultiplayerFix.DiplomaticMarriagePatch", true);
        AccessTools.Field(marriageHandler, "letterType").SetValue(null, marriageLetterType);
        foreach (string name in new[] { "quest", "suitor", "acceptSignal", "rejectSignal", "isAway" })
        {
            string handlerField = name == "quest" ? "questField" : name == "suitor" ? "suitorField" : name == "isAway" ? "awayField" : name == "acceptSignal" ? "acceptField" : "rejectField";
            AccessTools.Field(marriageHandler, handlerField).SetValue(null, AccessTools.Field(marriageLetterType, name));
        }
    }

    static Letter MarriageLetter(Quest quest, int id)
    {
        var letter = (Letter)FormatterServices.GetUninitializedObject(marriageLetterType);
        Set(letter, "quest", quest);
        Set(letter, "acceptSignal", "Quest" + quest.id + ".Marriage.Accept");
        Set(letter, "rejectSignal", "Quest" + quest.id + ".Marriage.Reject");
        ((LetterWithTimeout)letter).disappearAtTick = 10000;
        letter.ID = id;
        return letter;
    }

    static void TestMarriageOffers(Quest ownerQuest, Faction colony, Faction other, Faction spectator)
    {
        Set(Current.Game, "tickManager", Empty<TickManager>());
        Set(Current.Game, "components", new List<GameComponent> { new DiplomaticOfferLedger(Current.Game) });
        var stack = Find.LetterStack;
        var letter = MarriageLetter(ownerQuest, 9000);
        Check(letter is not ChoiceLetter && ActionableLetters.IsActionable(letter), "actual RotR DialogLetter is recognized despite not inheriting ChoiceLetter");
        Check(ActionableLetters.QuestFor(letter) == ownerQuest, "custom letter resolves its saved quest field");
        var probe = new Harmony("quest-fix.smoke.marriage");
        var receive = AccessTools.Method(typeof(LetterStack), nameof(LetterStack.ReceiveLetter), new[] { typeof(Letter), typeof(string), typeof(int), typeof(bool) });
        probe.Patch(receive, prefix: new HarmonyMethod(AccessTools.Method(typeof(Program), nameof(ReceiveForFixture))) { priority = Priority.Last });
        // Simulate the real MP foreign-faction postfix removing the letter first.
        probe.Patch(receive, postfix: new HarmonyMethod(AccessTools.Method(typeof(Program), nameof(RemoveForeignForFixture))) { priority = Priority.Normal });
        int idBefore = (int)AccessTools.Field(typeof(UniqueIDsManager), "nextLetterID").GetValue(Current.Game.uniqueIDsManager);
        foreach (var viewer in new[] { colony, other, spectator })
        {
            MP.game.myFactionLoading = viewer;
            stack.LettersListForReading.Remove(letter);
            stack.ReceiveLetter(letter, null, 0, false);
            Check(stack.LettersListForReading.Contains(letter), "custom offer remains in shared active list for " + viewer.Name);
            Check(ActionableLetters.VisibleToViewer(letter) == (viewer == colony), "custom offer UI is private for " + viewer.Name);
        }
        Check((int)AccessTools.Field(typeof(UniqueIDsManager), "nextLetterID").GetValue(Current.Game.uniqueIDsManager) == idBefore, "custom actionable offers are never cloned and allocate no extra IDs");

        var api = typeof(Multiplayer.API.MP);
        foreach (string name in new[] { "IsInMultiplayer", "IsExecutingSyncCommand" })
            probe.Patch(AccessTools.PropertyGetter(api, name), prefix: new HarmonyMethod(AccessTools.Method(typeof(Program), nameof(TrueForMarriageFixture))));
        probe.Patch(AccessTools.Method(typeof(Quest), nameof(Quest.End)), prefix: new HarmonyMethod(AccessTools.Method(typeof(Program), nameof(MarriageEndForFixture))) { priority = Priority.Last });
        probe.Patch(AccessTools.Method(typeof(Messages), nameof(Messages.Message), new[] { typeof(string), typeof(MessageTypeDef), typeof(bool) }), prefix: new HarmonyMethod(AccessTools.Method(typeof(Program), nameof(SkipMarriageMessage))));
        var decide = AccessTools.Method(marriageHandler, "Decide");
        var commandField = AccessTools.Field(typeof(TickPatch), "currentExecutingCmdType");
        commandField.SetValue(null, Enum.ToObject(Nullable.GetUnderlyingType(commandField.FieldType), 0));
        try
        {
            foreach (var viewer in new[] { colony, other, spectator })
            {
                MP.game.myFactionLoading = viewer;
                var quest = new Quest { id = 9100 + viewer.loadID };
                Set(quest, "acceptanceTick", 0);
                var offer = MarriageLetter(quest, 9200 + viewer.loadID);
                var end = new QuestPart_QuestEnd { inSignal = "Quest" + quest.id + ".Marriage.Reject", outcome = QuestEndOutcome.Fail };
                Set(end, "quest", quest);
                Set(quest, "parts", new List<QuestPart> { end });
                var signalManager = new SignalManager();
                signalManager.RegisterReceiver(quest);
                Set(Current.Game, "signalManager", signalManager);
                MP.game.worldComp.factionData[colony.loadID].history.archive.Add(offer);
                stack.LettersListForReading.RemoveAll(l => DiplomaticMarriageOffers.Matches(l, quest.id));
                Check(DiplomaticMarriageOffers.Resolve(quest.id) == offer, "canonical saved offer resolves with no UI letter for " + viewer.Name);
                marriageQuestEnds = 0;
                decide.Invoke(null, new object[] { quest.id, null, false });
                Check(marriageQuestEnds == 1, "actual synchronized reject sends quest-ending signal once for " + viewer.Name);
                Check(Current.Game.GetComponent<DiplomaticOfferLedger>().Contains(quest.id), "decision consumption is recorded for " + viewer.Name);
                decide.Invoke(null, new object[] { quest.id, null, false });
                Check(marriageQuestEnds == 1, "duplicate command cannot consume archived offer again for " + viewer.Name);
            }
            var retryQuest = new Quest { id = 9300 };
            var retry = MarriageLetter(retryQuest, 9301);
            MP.game.worldComp.factionData[colony.loadID].history.archive.Add(retry);
            decide.Invoke(null, new object[] { retryQuest.id, null, true });
            Check(!Current.Game.GetComponent<DiplomaticOfferLedger>().Contains(retryQuest.id) && stack.LettersListForReading.Contains(retry), "invalid acceptance remains pending and retryable");
            ((LetterWithTimeout)retry).disappearAtTick = 0;
            Check(DiplomaticMarriageOffers.Resolve(retryQuest.id) == null, "expired archived marriage offers are excluded");
            ((LetterWithTimeout)retry).disappearAtTick = 10000;
            Set(retryQuest, "ended", true);
            Check(DiplomaticMarriageOffers.Resolve(retryQuest.id) == null, "historical archived marriage offers are excluded");
        }
        finally
        {
            commandField.SetValue(null, null);
            MP.game.myFactionLoading = colony;
            probe.UnpatchAll(probe.Id);
        }
    }

    static bool TrueForMarriageFixture(ref bool __result) { __result = true; return false; }
    static bool SkipMarriageMessage() => false;
    static bool MarriageEndForFixture() { marriageQuestEnds++; Find.UniqueIDsManager.GetNextLetterID(); return false; }
    static void RemoveForeignForFixture(LetterStack __instance, Letter let)
    {
        if (MP.RealPlayerFaction != Faction.OfPlayer) __instance.LettersListForReading.Remove(let);
    }
}
