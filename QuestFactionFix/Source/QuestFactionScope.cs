using System;
using Multiplayer.Client;
using Multiplayer.Client.Factions;
using RimWorld;
using Verse;

namespace MultiplayerQuestFactionFix;

public sealed class QuestFactionScope : IDisposable
{
	private readonly Map map;

	private bool disposed;

	private QuestFactionScope(Map map)
	{
		this.map = map;
	}

	public static QuestFactionScope Enter(Faction owner, Map map)
	{
		if (!QuestOwnership.Enabled || !QuestOwnership.IsColonyFaction(owner) || owner == Faction.OfPlayer)
		{
			return null;
		}
		if (map?.MpComp() == null)
		{
			map = null;
		}
		int count = FactionContext.stack.Count;
		QuestFactionScope questFactionScope = new QuestFactionScope(map);
		try
		{
			map.PushFaction(owner);
			return questFactionScope;
		}
		catch
		{
			if (FactionContext.stack.Count > count)
			{
				questFactionScope.Dispose();
			}
			throw;
		}
	}

	public void Dispose()
	{
		if (!disposed)
		{
			disposed = true;
			map.PopFaction();
		}
	}
}
