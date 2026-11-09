using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Multiplayer.Client;
using RimWorld;
using RimWorld.Planet;
using Verse;
using MP = Multiplayer.Client.Multiplayer;

namespace MultiplayerQuestFactionFix;

public static class QuestOwnership
{
    private static readonly Dictionary<System.Type, FieldInfo> mapParentFields = new();
    public static bool Enabled => MP.Client != null && MP.game?.gameComp?.multifaction == true;

    public static bool IsColonyFaction(Faction faction) => faction?.def?.isPlayer == true &&
        faction != MP.game?.worldComp?.spectatorFaction;

    // Saved quest targets are authoritative. Never infer ownership from the local
    // viewer or the temporary Faction.OfPlayer, which may currently be Spectator.
    public static IEnumerable<MapParent> Targets(Quest quest)
    {
        if (quest?.parts == null) yield break;
        var seen = new HashSet<MapParent>();
        foreach (var part in quest.parts)
            if (part is QuestPart_JoinPlayer join && IsColonyFaction(join.mapParent?.Faction) && seen.Add(join.mapParent))
                yield return join.mapParent;

        foreach (var part in quest.parts)
        {
            if (part == null) continue;
            var type = part.GetType();
            if (!mapParentFields.TryGetValue(type, out var field))
                mapParentFields[type] = field = AccessTools.Field(type, "mapParent");
            if (field?.GetValue(part) is MapParent parent && IsColonyFaction(parent.Faction) && seen.Add(parent))
                yield return parent;
        }

        foreach (var part in quest.parts)
        {
            if (part == null) continue;
            foreach (var target in part.QuestLookTargets)
                if (target.HasWorldObject && target.WorldObject is MapParent parent &&
                    IsColonyFaction(parent.Faction) && seen.Add(parent))
                    yield return parent;
        }
    }

    public static Faction Owner(Quest quest)
    {
        Faction owner = null;
        foreach (var target in Targets(quest))
        {
            if (owner != null && owner != target.Faction) return null;
            owner = target.Faction;
        }
        if (owner != null) return owner;
        var accepterFaction = quest?.AccepterPawn?.Faction;
        if (IsColonyFaction(accepterFaction)) return accepterFaction;
        return quest?.parent != null ? Owner(quest.parent) : null;
    }

    public static Map TargetMap(Quest quest)
    {
        foreach (var target in Targets(quest)) return target.Map;
        return null;
    }

    public static MapParent JoinOfferTarget(QuestPart_PawnJoinOffer offer)
    {
        if (offer.quest?.parts != null)
            foreach (var part in offer.quest.parts)
                if (part is QuestPart_JoinPlayer join && join.inSignal == offer.outSignalPawnAccepted &&
                    IsColonyFaction(join.mapParent?.Faction))
                    return join.mapParent;
        foreach (var target in Targets(offer.quest)) return target;
        return null;
    }

    public static bool ShouldShow(Quest quest, Faction viewer)
    {
        var owner = Owner(quest);
        return owner == null || owner == viewer;
    }

    public static AsyncTimeComp FindHomeTime(Quest quest)
    {
        // A quest spanning colonies must remain on the world clock.
        if (Owner(quest) == null) return null;
        foreach (var target in Targets(quest))
        {
            var map = target.Map;
            if (map == null) continue;
            // IsPlayerHome compares the map owner to Faction.OfPlayer. Check it
            // under the actual owner even when save loading uses spectator context.
            FactionContext.Push(target.Faction);
            try
            {
                if (map.IsPlayerHome && map.AsyncTime() is { } time) return time;
            }
            finally { FactionContext.Pop(); }
        }
        return null;
    }
}
