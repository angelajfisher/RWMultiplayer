using System;
using System.Linq;
using HarmonyLib;
using MultiplayerQuestFactionFix;
using RimWorld;
using Verse;
using MP = Multiplayer.Client.Multiplayer;

static partial class Program
{
    static int diplomaticExecutions;
    static bool throwDiplomatic;
    static StandardLetter diplomaticLetter;
    static void TestHistoryAndDiplomacy(Faction colony, Faction other, Faction spectator)
    {
        var archive = new Archive();
        var oldLetter = new StandardLetter { ID = 100000, arrivalTick = 1 };
        archive.Add(oldLetter);
        for (int i = 0; i < 600; i++)
        {
            var message = Empty<Message>(); message.startingTick = i + 2;
            archive.Add(message);
        }
        Check(archive.Contains(oldLetter), "600 short messages cannot evict the older letter");
        Check(archive.ArchivablesListForReading.OfType<Message>().Count() == 200, "message history remains bounded separately");
        archive.Pin(oldLetter);
        for (int i = 0; i < 205; i++) archive.Add(new StandardLetter { ID = 100001 + i, arrivalTick = 1000 + i });
        Check(archive.Contains(oldLetter), "pinned letter survives the separate letter limit");
        Check(archive.ArchivablesListForReading.OfType<Letter>().Count() == 201, "letter history keeps 200 eligible letters plus pinned entries");
        var active = new StandardLetter { ID = 110000, arrivalTick = 0 };
        Find.LetterStack.LettersListForReading.Add(active);
        archive.Add(active);
        Check(archive.Contains(active), "active letters retain vanilla protection from history culling");
        Find.LetterStack.LettersListForReading.Remove(active);

        var workerType = AccessTools.TypeByName("Leo.Events.IncidentWorker_ScandalousInsult");
        var worker = Activator.CreateInstance(workerType);
        var execute = AccessTools.DeclaredMethod(workerType, "TryExecuteWorker");
        var fixture = new Harmony("quest-fix.smoke.diplomacy");
        fixture.Patch(execute, prefix: new HarmonyMethod(AccessTools.Method(typeof(Program), nameof(DiplomaticBody))) { priority = Priority.Last });
        fixture.Patch(AccessTools.Method(typeof(LetterStack), nameof(LetterStack.ReceiveLetter), new[] { typeof(Letter), typeof(string), typeof(int), typeof(bool) }), prefix: new HarmonyMethod(AccessTools.Method(typeof(Program), nameof(ReceiveForFixture))) { priority = Priority.Last });
        var worldField = AccessTools.Field(typeof(Multiplayer.Client.AsyncTime.AsyncWorldTimeComp), "tickingWorld");
        worldField.SetValue(null, true);
        MP.game.myFactionLoading = other;
        using (QuestFactionScope.Enter(colony, null)) execute.Invoke(worker, new object[] { new IncidentParms() });
        Check(diplomaticExecutions == 1, "world storyteller incident executes under its actual colony context");
        Check(!Find.LetterStack.LettersListForReading.Contains(diplomaticLetter), "insult notification is hidden from unaffected local viewer");
        Check(MP.game.worldComp.factionData[colony.loadID].history.archive.Contains(diplomaticLetter), "insult notification enters the affected colony's history");
        Check(diplomaticLetter.Label.ToString().Contains(colony.Name), "insult notification explicitly names the affected colony");
        Check(DiplomaticIncidentDelivery.Recipient == null && Faction.OfPlayer == spectator, "incident recipient and faction context are restored");
        MP.game.myFactionLoading = colony;
        using (QuestFactionScope.Enter(colony, null)) execute.Invoke(worker, new object[] { new IncidentParms() });
        Check(Find.LetterStack.LettersListForReading.Contains(diplomaticLetter), "affected viewer sees its own insult during world ticking");
        int before = diplomaticExecutions;
        execute.Invoke(worker, new object[] { new IncidentParms() });
        Check(diplomaticExecutions == before, "spectator-world invocation cannot affect an arbitrary player's goodwill");
        throwDiplomatic = true;
        bool caught = false;
        try { using (QuestFactionScope.Enter(colony, null)) execute.Invoke(worker, new object[] { new IncidentParms() }); }
        catch (System.Reflection.TargetInvocationException e) when (e.InnerException is InvalidOperationException) { caught = true; }
        throwDiplomatic = false;
        Check(caught && DiplomaticIncidentDelivery.Recipient == null && Faction.OfPlayer == spectator, "exception cleanup cannot leak the private recipient to subsequent world events");
        worldField.SetValue(null, false);
        Check(QuestLetterRouting.Recipients(new StandardLetter()).Count == 2, "unrelated public world notifications still reach all colonies");
        fixture.UnpatchAll(fixture.Id);
    }
    static bool DiplomaticBody()
    {
        diplomaticExecutions++;
        if (throwDiplomatic) throw new InvalidOperationException("fixture");
        diplomaticLetter = new StandardLetter { ID = Find.UniqueIDsManager.GetNextLetterID(), Label = "Scandalous insult: NPC" };
        Find.LetterStack.ReceiveLetter(diplomaticLetter, null, 0, false);
        return false;
    }
}
