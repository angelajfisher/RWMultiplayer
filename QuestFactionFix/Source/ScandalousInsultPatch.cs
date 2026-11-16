using System;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace MultiplayerQuestFactionFix;

public static class DiplomaticIncidentDelivery
{
    [ThreadStatic] public static Faction Recipient;

    public static Faction Resolve(IncidentParms parms)
    {
        var target = (parms?.target as Map)?.ParentFaction;
        if (QuestOwnership.IsColonyFaction(target)) return target;
        return QuestOwnership.IsColonyFaction(Faction.OfPlayer) ? Faction.OfPlayer : null;
    }
}

// Optional integration: keep the mod's faction choice, RNG and goodwill logic.
// MP repeats world storytellers under each colony's faction context.
[HarmonyPatch]
internal static class ScandalousInsultPatch
{
    private static Type Worker => AccessTools.TypeByName("Leo.Events.IncidentWorker_ScandalousInsult");
    private static bool Prepare() => Worker != null;
    private static MethodBase TargetMethod() => AccessTools.DeclaredMethod(Worker, "TryExecuteWorker");
    private sealed class State
    {
        internal Faction previous;
        internal QuestFactionScope scope;
    }
    private static bool Prefix(IncidentParms parms, ref State __state)
    {
        if (!QuestOwnership.Enabled) return true;
        __state = new State { previous = DiplomaticIncidentDelivery.Recipient };
        var recipient = DiplomaticIncidentDelivery.Resolve(parms);
        // A spectator-world invocation has no colony to insult; never choose
        // a recipient using a client's local viewer or host faction.
        if (recipient == null) return false;
        DiplomaticIncidentDelivery.Recipient = recipient;
        __state.scope = QuestFactionScope.Enter(recipient, parms?.target as Map);
        return true;
    }
    private static void Finalizer(State __state)
    {
        if (__state == null) return;
        try { __state.scope?.Dispose(); }
        finally { DiplomaticIncidentDelivery.Recipient = __state.previous; }
    }
}
