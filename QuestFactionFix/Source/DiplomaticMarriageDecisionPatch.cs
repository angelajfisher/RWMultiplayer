using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;
using MP = Multiplayer.Client.Multiplayer;

namespace MultiplayerQuestFactionFix;

// Shared and saved; never derive whether an offer was consumed from local UI state.
public sealed class DiplomaticOfferLedger : GameComponent
{
    private List<int> consumed = new();
    public DiplomaticOfferLedger(Game game) { }
    public bool Contains(int questId) => consumed.Contains(questId);
    public void Consume(int questId) { if (!Contains(questId)) consumed.Add(questId); }
    public override void ExposeData()
    {
        Scribe_Collections.Look(ref consumed, "questFactionFixConsumedMarriageOffers", LookMode.Value);
        if (Scribe.mode == LoadSaveMode.PostLoadInit) consumed ??= new();
    }
}

public static class DiplomaticMarriageOffers
{
    public const string LetterTypeName = "RomanceOnTheRim.DialogLetter_DiplomaticMarriage";
    public static bool Matches(Letter letter, int questId) => letter?.GetType().FullName == LetterTypeName &&
        ActionableLetters.QuestFor(letter)?.id == questId;

    public static Letter Resolve(int questId)
    {
        if (Current.Game.GetComponent<DiplomaticOfferLedger>().Contains(questId)) return null;
        // Faction archives are shared simulation data. The active letter list is a
        // client UI list and may already differ when resuming an older save.
        Letter result = null;
        // Avoid cached delegates whose signatures mention MP's runtime-only types:
        // Prepatcher inspects fields before Multiplayer swaps in those types.
        foreach (var data in MP.game.worldComp.factionData.Values)
            foreach (var archivable in data.history.archive.ArchivablesListForReading)
            {
                if (archivable is not Letter letter || !Matches(letter, questId) ||
                    ActionableLetters.QuestFor(letter).Historical ||
                    (letter is LetterWithTimeout timed && timed.TimeoutPassed)) continue;
                if (result == null || letter.ID < result.ID) result = letter;
            }
        return result;
    }

    public static Letter Prepare(int questId)
    {
        var letter = Resolve(questId);
        if (letter == null) return null;
        var active = Find.LetterStack.LettersListForReading;
        // The original handler must encounter this canonical offer first on every peer.
        active.RemoveAll(candidate => Matches(candidate, questId));
        active.Add(letter);
        return letter;
    }

    public static void Complete(int questId, Letter letter)
    {
        // Invalid partner/stale input leaves the letter active and must remain retryable.
        if (letter != null && !Find.LetterStack.LettersListForReading.Contains(letter))
            Current.Game.GetComponent<DiplomaticOfferLedger>().Consume(questId);
    }
}

// Optional integration: preserve the existing sync registration, eligibility checks,
// relationship changes and original quest signals in MenuMultiplayerFix.Decide.
[HarmonyPatch]
internal static class DiplomaticMarriageDecisionPatch
{
    static bool Prepare() => AccessTools.TypeByName("MenuMultiplayerFix.DiplomaticMarriagePatch") != null;
    static MethodBase TargetMethod() => AccessTools.Method(
        AccessTools.TypeByName("MenuMultiplayerFix.DiplomaticMarriagePatch"), "Decide",
        new[] { typeof(int), typeof(Pawn), typeof(bool) }) ??
        throw new MissingMethodException("MenuMultiplayerFix diplomatic marriage Decide signature changed");

    static bool Prefix(int questId, ref Letter __state)
    {
        if (!QuestOwnership.Enabled || !MP.ExecutingCmds) return true;
        __state = DiplomaticMarriageOffers.Prepare(questId);
        return __state != null;
    }

    static void Postfix(int questId, Letter __state)
    {
        if (__state != null) DiplomaticMarriageOffers.Complete(questId, __state);
    }
}
