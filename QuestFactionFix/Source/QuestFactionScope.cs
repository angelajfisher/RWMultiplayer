using System;
using Multiplayer.Client;
using Multiplayer.Client.Factions;
using RimWorld;
using Verse;
using MP = Multiplayer.Client.Multiplayer;

namespace MultiplayerQuestFactionFix;

public sealed class QuestFactionScope : IDisposable
{
    private readonly Map map;
    private bool disposed;

    private QuestFactionScope(Map map) { this.map = map; }

    public static QuestFactionScope Enter(Faction owner, Map map)
    {
        if (!QuestOwnership.Enabled || !QuestOwnership.IsColonyFaction(owner) || owner == Faction.OfPlayer)
            return null;
        // During early load a target map may exist before its MP components do.
        // World context still supplies the correct faction/archive in that case.
        if (map?.MpComp() == null) map = null;
        int previousDepth = FactionContext.stack.Count;
        var scope = new QuestFactionScope(map);
        try
        {
            map.PushFaction(owner);
            return scope;
        }
        catch
        {
            if (FactionContext.stack.Count > previousDepth) scope.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        map.PopFaction();
    }
}
