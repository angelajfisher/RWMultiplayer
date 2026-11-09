using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Multiplayer.Client.AsyncTime;
using RimWorld;
using Verse;
using Verse.Sound;
using MP = Multiplayer.Client.Multiplayer;

namespace MultiplayerQuestFactionFix;

public static class QuestLetterRouting
{
    public static List<Faction> Recipients(Letter letter)
    {
        var quest = (letter as ChoiceLetter)?.quest;
        var owner = QuestOwnership.Owner(quest);
        if (quest != null)
        {
            if (owner == null) return Colonies();
            var recipients = new List<Faction> { owner };
            // Only informational letters are shared; recruitment choices remain the owner's.
            if (letter is StandardLetter)
                foreach (var faction in Colonies())
                    if (faction != owner && SharedQuestParticipation.IsParticipant(quest, faction)) recipients.Add(faction);
            return recipients;
        }
        if (letter is not StandardLetter) return null;
        var targets = new HashSet<Faction>();
        if (letter.lookTargets != null)
            foreach (var target in letter.lookTargets.targets)
            {
                var faction = target.Map?.ParentFaction ?? target.Thing?.Faction ?? target.WorldObject?.Faction;
                if (QuestOwnership.IsColonyFaction(faction)) targets.Add(faction);
            }
        if (targets.Count > 0) return targets.OrderBy(f => f.loadID).ToList();
        // Untargeted world notifications are public. Untargeted map/UI notices
        // retain their existing destination rather than becoming broadcasts.
        return AsyncWorldTimeComp.tickingWorld || Faction.OfPlayer == MP.game.worldComp.spectatorFaction ? Colonies() : null;
    }

    static List<Faction> Colonies() => Find.FactionManager.AllFactionsListForReading
        .Where(QuestOwnership.IsColonyFaction).OrderBy(f => f.loadID).ToList();
}

// Optional reflection bridge: no extra copy of the sharing mod/API is shipped.
static class SharedQuestParticipation
{
    public static bool IsParticipant(Quest quest, Faction faction)
    {
        var type = AccessTools.TypeByName("RaidAnimalFix.SharedQuestUtil");
        if (type == null) return false;
        var entry = AccessTools.Method(type, "EntryFor")?.Invoke(null, new object[] { quest });
        return entry != null && (bool)AccessTools.Method(entry.GetType(), "IsParticipant").Invoke(entry, new object[] { faction });
    }
}

[HarmonyPatch(typeof(LetterStack), nameof(LetterStack.ReceiveLetter), new[] { typeof(Letter), typeof(string), typeof(int), typeof(bool) })]
static class QuestLetterRoutingPatch
{
    sealed class Delivery
    {
        internal List<Faction> recipients;
        internal QuestFactionScope scope;
    }

    [HarmonyPriority(Priority.First)]
    static void Prefix(Letter let, int delayTicks, ref Delivery __state)
    {
        if (!QuestOwnership.Enabled || let == null || delayTicks > 0 || !let.CanShowInLetterStack) return;
        var recipients = QuestLetterRouting.Recipients(let);
        if (recipients == null || recipients.Count == 0) return;
        // Public actionable letters keep the existing action semantics.
        if (let is not StandardLetter && QuestOwnership.Owner((let as ChoiceLetter)?.quest) == null) return;
        __state = new Delivery { recipients = recipients };
        __state.scope = QuestFactionScope.Enter(recipients[0], null);
    }

    [HarmonyPriority(int.MinValue)]
    static void Postfix(LetterStack __instance, Letter let, bool playSound, Delivery __state)
    {
        if (__state == null) return;
        var visible = __instance.LettersListForReading;
        bool hadOriginal = visible.Remove(let);
        Letter local = null;
        foreach (var faction in __state.recipients)
        {
            var copy = let;
            if (faction != __state.recipients[0])
            {
                // Unique IDs and separate objects prevent duplicate load references
                // when each faction's archive is serialized into the same save.
                copy = Gen.MemberwiseClone(let);
                copy.ID = Find.UniqueIDsManager.GetNextLetterID();
                MP.game.worldComp.factionData[faction.loadID].history.archive.Add(copy);
            }
            if (faction == MP.RealPlayerFaction) local = copy;
        }
        if (local == null) return;
        visible.Add(local);
        if (playSound && !hadOriginal && local.def?.arriveSound != null)
            local.def.arriveSound.PlayOneShotOnCamera();
    }

    static void Finalizer(Delivery __state) => __state?.scope?.Dispose();
}
