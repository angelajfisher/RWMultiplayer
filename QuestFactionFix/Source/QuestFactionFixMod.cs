using HarmonyLib;
using Multiplayer.Client;
using Multiplayer.Client.Comp;
using Multiplayer.Client.Factions;
using RimWorld;
using Verse;
using MP = Multiplayer.Client.Multiplayer;

namespace MultiplayerQuestFactionFix;

public sealed class QuestFactionFixMod : Mod
{
    public const string HarmonyId = "angelajfisher.multiplayerquestfactionfix";
    public QuestFactionFixMod(ModContentPack content) : base(content)
    {
        ApplyPatches();
        Log.Message("[MP Quest Faction Fix] v0.2.0 loaded: quest context, shared/global visibility and notification routing.");
    }

    public static void ApplyPatches() => new Harmony(HarmonyId).PatchAll(typeof(QuestFactionFixMod).Assembly);
}

// Signals can arrive during an unrelated map tick, not just QuestTick.
[HarmonyPatch(typeof(Quest), nameof(Quest.Notify_SignalReceived))]
static class QuestSignalFactionPatch
{
    static void Prefix(Quest __instance, ref QuestFactionScope __state)
    {
        if (QuestOwnership.Enabled)
            __state = QuestFactionScope.Enter(QuestOwnership.Owner(__instance), QuestOwnership.TargetMap(__instance));
    }
    static void Finalizer(QuestFactionScope __state) => __state?.Dispose();
}

[HarmonyPatch(typeof(Quest), nameof(Quest.End))]
static class QuestEndFactionPatch
{
    static void Prefix(Quest __instance, ref QuestFactionScope __state)
    {
        if (QuestOwnership.Enabled)
            __state = QuestFactionScope.Enter(QuestOwnership.Owner(__instance), QuestOwnership.TargetMap(__instance));
    }
    static void Finalizer(QuestFactionScope __state) => __state?.Dispose();
}

[HarmonyPatch(typeof(MultiplayerAsyncQuest), "TryGetQuestMap")]
static class QuestMapLookupPatch
{
    static bool Prefix(Quest quest, ref AsyncTimeComp __result)
    {
        if (!QuestOwnership.Enabled) return true;
        __result = QuestOwnership.FindHomeTime(quest);
        return false;
    }
}

[HarmonyPatch(typeof(Quest), nameof(Quest.QuestTick))]
static class QuestTickFactionPatch
{
    static void Prefix(Quest __instance, ref QuestFactionScope __state)
    {
        if (!QuestOwnership.Enabled) return;
        __state = QuestFactionScope.Enter(QuestOwnership.Owner(__instance), QuestOwnership.TargetMap(__instance));
    }
    static void Finalizer(QuestFactionScope __state) => __state?.Dispose();
}

[HarmonyPatch(typeof(QuestPart_PawnJoinOffer), "SendLetter")]
static class JoinOfferFactionPatch
{
    static void Prefix(QuestPart_PawnJoinOffer __instance, ref QuestFactionScope __state)
    {
        if (!QuestOwnership.Enabled) return;
        var target = QuestOwnership.JoinOfferTarget(__instance);
        var map = target?.Map ?? __instance.pawn?.MapHeld;
        var owner = target?.Faction ?? QuestOwnership.Owner(__instance.quest) ?? map?.ParentFaction;
        __state = QuestFactionScope.Enter(owner, map);
        if (__state != null)
            Log.Message($"[MP Quest Faction Fix] Join offer for {__instance.pawn?.ThingID}: quest {__instance.quest?.id} routed to {owner.Name} ({owner.loadID}).");
    }
    static void Finalizer(QuestFactionScope __state) => __state?.Dispose();
}

[HarmonyPatch(typeof(QuestPart_JoinPlayer), nameof(QuestPart_JoinPlayer.Notify_QuestSignalReceived))]
static class RecruitToTargetFactionPatch
{
    static void Prefix(QuestPart_JoinPlayer __instance, Signal signal, ref QuestFactionScope __state)
    {
        if (!QuestOwnership.Enabled || signal.tag != __instance.inSignal) return;
        var owner = __instance.mapParent?.Faction;
        if (!QuestOwnership.IsColonyFaction(owner)) owner = QuestOwnership.Owner(__instance.quest);
        __state = QuestFactionScope.Enter(owner, __instance.mapParent?.Map);
        if (__state != null)
            Log.Message($"[MP Quest Faction Fix] Recruitment signal {signal.tag}: using colony {owner.Name} ({owner.loadID}).");
    }
    static void Finalizer(QuestFactionScope __state) => __state?.Dispose();
}

[HarmonyPatch(typeof(FactionExtensions), nameof(FactionExtensions.TryGetPlayerFaction))]
static class QuestOwnerLookupPatch
{
    static bool Prefix(Quest quest, ref Faction faction, ref bool __result)
    {
        if (!QuestOwnership.Enabled) return true;
        faction = QuestOwnership.Owner(quest);
        __result = faction != null;
        return false;
    }
}

[HarmonyPatch(typeof(MainTabWindow_Quests), nameof(MainTabWindow_Quests.ShouldListNow))]
static class HideOtherColonyQuestsPatch
{
    [HarmonyPriority(Priority.First)]
    static bool Prefix(Quest quest, ref bool __result)
    {
        if (!QuestOwnership.Enabled || QuestOwnership.ShouldShow(quest, MP.RealPlayerFaction)) return true;
        __result = false;
        return false;
    }
}
