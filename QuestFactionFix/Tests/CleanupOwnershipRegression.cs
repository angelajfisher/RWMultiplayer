using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using MultiplayerQuestFactionFix;
using RimWorld;
using RimWorld.Planet;
using Verse;
using MP = Multiplayer.Client.Multiplayer;

static partial class Program
{
    static void TestCleanupOwnership(Faction colony, Faction other, World world, string filtersPath)
    {
        var parent = Empty<Settlement>(); Set(parent, "factionInt", other);
        var quest = new Quest { id = 123456, name = "Paid Climate Adjuster regression" };
        Set(quest, "parts", new List<QuestPart> { new QuestPart_JoinPlayer { mapParent = parent } });
        var patch = new Harmony("quest-fix.smoke.cleanup-owner");
        // Run the real addon lifecycle prefix before a controlled cleanup body
        // removes all target references, as aged/cleaned-up quests can do.
        patch.Patch(AccessTools.Method(typeof(Quest), nameof(Quest.CleanupQuestParts)), prefix: new HarmonyMethod(AccessTools.Method(typeof(Program), nameof(ClearQuestParts))) { priority = Priority.Last });
        quest.CleanupQuestParts();
        Check(quest.PartsListForReading.Count == 0 && QuestOwnership.Owner(quest) == other, "cleanup cannot erase the affected quest's owner");
        MP.game.myFactionLoading = colony;
        Check(!QuestOwnership.ShouldShow(quest, colony), "cleaned-up foreign quest remains private in history");
        Check(QuestLetterRouting.Recipients(new StandardLetter { quest = quest }).SequenceEqual(new[] { other }), "failure after cleanup is routed only to the original owner");
        var delivery = new Harmony("quest-fix.smoke.cleaned-letter");
        delivery.Patch(AccessTools.Method(typeof(LetterStack), nameof(LetterStack.ReceiveLetter), new[] { typeof(Letter), typeof(string), typeof(int), typeof(bool) }), prefix: new HarmonyMethod(AccessTools.Method(typeof(Program), nameof(ReceiveForFixture))) { priority = Priority.Last });
        var letter = new StandardLetter { quest = quest, ID = 120001 };
        Find.LetterStack.ReceiveLetter(letter, null, 0, false);
        Check(!Find.LetterStack.LettersListForReading.Contains(letter), "foreign cleaned-up quest failure does not enter local event list");
        Check(MP.game.worldComp.factionData[other.loadID].history.archive.Contains(letter), "cleaned-up failure is archived by its owner");
        delivery.UnpatchAll(delivery.Id);

        string path = Path.Combine(Path.GetTempPath(), "quest-owner-" + Guid.NewGuid() + ".xml");
        Scribe.saver.InitSaving(path, "owner");
        QuestOwnerMemory.Expose(quest);
        Scribe.saver.FinalizeSaving();
        var loaded = new Quest { id = quest.id };
        Scribe.loader.InitLoading(path);
        QuestOwnerMemory.Expose(loaded);
        // This field is a value; no cross-reference/post-load phase is needed.
        // Avoid full Unity profiling initialization in the headless host.
        Scribe.loader.ForceStop();
        Check(QuestOwnerMemory.Owner(loaded) == other, "owner survives real Scribe save/load with no quest parts");
        File.Delete(path);
        var publicQuest = new Quest { id = 123457 };
        publicQuest.CleanupQuestParts();
        Check(QuestOwnership.Owner(publicQuest) == null && QuestLetterRouting.Recipients(new StandardLetter { quest = publicQuest }).Count == 2, "public quest cleanup does not invent a private owner from execution context");
        Set(quest, "parts", new List<QuestPart> { new QuestPart_JoinPlayer { mapParent = parent }, new QuestPart_JoinPlayer { mapParent = MakeTarget(colony) } });
        Check(QuestOwnership.Owner(quest) == null, "explicit multi-colony targets take precedence over remembered single owner");
        quest.CleanupQuestParts();
        Check(QuestOwnership.Owner(quest) == null, "cleanup of an explicitly multi-colony quest clears stale single-owner memory");
        Set(quest, "parts", new List<QuestPart> { new QuestPart_JoinPlayer { mapParent = parent } });
        QuestOwnerMemory.Remember(quest);
        // Exercise the installed sharing integration against a cleaned-up quest.
        if (File.Exists(filtersPath))
        {
            Set(quest, "parts", new List<QuestPart>());
            var dataType = AccessTools.TypeByName("RaidAnimalFix.RafSharedQuestData");
            var data = world.components.Find(c => c.GetType() == dataType);
            var entry = dataType.GetMethod("GetOrCreate").Invoke(data, new object[] { quest.id, other.loadID });
            ((System.Collections.IList)entry.GetType().GetField("participants").GetValue(entry)).Add(colony.loadID);
            Check(QuestLetterRouting.Recipients(new StandardLetter { quest = quest }).Count == 2, "accepted ally still receives outcomes after quest cleanup");
        }
        patch.UnpatchAll(patch.Id);
    }
    static Settlement MakeTarget(Faction faction) { var target = Empty<Settlement>(); Set(target, "factionInt", faction); return target; }
    static bool ClearQuestParts(Quest __instance) { __instance.PartsListForReading.Clear(); return false; }
}
