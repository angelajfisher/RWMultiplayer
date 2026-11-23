using HarmonyLib;
using Multiplayer.Client;
using RimWorld;
using Verse;
using MP = Multiplayer.Client.Multiplayer;

namespace IdeologyFactionFix;

public sealed class IdeologyFactionFixMod : Mod
{
    public IdeologyFactionFixMod(ModContentPack content) : base(content)
    {
        ApplyPatches();
        Log.Message("[MP Ideology Faction Fix] v0.1.0: faction follower counts, role holders and map/faction tree history.");
    }
    public static void ApplyPatches() => new Harmony("angelajfisher.ideologyfactionfix").PatchAll(typeof(IdeologyFactionFixMod).Assembly);
}

public static class ColonyScope
{
    public static bool Enabled => MP.Client != null && MP.game?.gameComp?.multifaction == true;
    public static bool IsColony(Faction f) => f?.def?.isPlayer == true && f != MP.game?.worldComp?.spectatorFaction;
    public static Faction ForPawn(Pawn pawn) => IsColony(pawn?.Faction) ? pawn.Faction : null;
    public static int Count(Ideo ideo, Faction faction)
    {
        if (!IsColony(faction)) return 0;
        int count = 0;
        foreach (var p in PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive)
            if (p.Faction == faction && p.RaceProps.Humanlike && p.HostFaction == null && !p.InCryptosleep && !p.IsSlave && !p.IsQuestLodger() && p.Ideo == ideo)
                count++;
        return count;
    }
}

[HarmonyPatch(typeof(Ideo), nameof(Ideo.ColonistBelieverCountCached), MethodType.Getter)]
static class FactionFollowerCount
{
    static bool Prefix(Ideo __instance, ref int __result)
    {
        if (!ColonyScope.Enabled) return true;
        __result = ColonyScope.Count(__instance, Faction.OfPlayer); return false;
    }
}

[HarmonyPatch(typeof(Ideo), nameof(Ideo.ObligationsActive), MethodType.Getter)]
static class FactionObligations
{
    static bool Prefix(Ideo __instance, ref bool __result)
    {
        if (!ColonyScope.Enabled) return true;
        var faction = Faction.OfPlayer;
        __result = ColonyScope.IsColony(faction) && (ColonyScope.Count(__instance, faction) >= Ideo.MinBelieversToEnableObligations || faction.ideos.IsPrimary(__instance));
        return false;
    }
}

// The old shared cached count must never drive activity or eligibility. Roles
// refresh all factions deterministically through their own RecacheActivity hook.
[HarmonyPatch(typeof(Ideo), nameof(Ideo.RecacheColonistBelieverCount))]
static class RefreshFollowerCount
{
    static bool Prefix(Ideo __instance, ref int __result)
    {
        if (!ColonyScope.Enabled) return true;
        __result = ColonyScope.Count(__instance, Faction.OfPlayer);
        foreach (var role in __instance.RolesListForReading) role.RecacheActivity();
        return false;
    }
}
