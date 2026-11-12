using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Multiplayer.Client;
using RimWorld;
using RimWorld.Planet;
using RimWorld.QuestGen;
using Verse;

namespace MultiplayerQuestFactionFix;

public static class QuestOwnership
{
	private static readonly Dictionary<Type, FieldInfo> mapParentFields = new Dictionary<Type, FieldInfo>();

	public static bool Enabled
	{
		get
		{
			if (Multiplayer.Client.Multiplayer.Client != null)
			{
				MultiplayerGame game = Multiplayer.Client.Multiplayer.game;
				if (game == null)
				{
					return false;
				}
				return game.gameComp?.multifaction == true;
			}
			return false;
		}
	}

	public static bool IsColonyFaction(Faction faction)
	{
		if (faction != null && faction.def?.isPlayer == true)
		{
			return faction != Multiplayer.Client.Multiplayer.game?.worldComp?.spectatorFaction;
		}
		return false;
	}

	public static IEnumerable<MapParent> Targets(Quest quest)
	{
		if (quest?.parts == null)
		{
			yield break;
		}
		HashSet<MapParent> seen = new HashSet<MapParent>();
		foreach (QuestPart part in quest.parts)
		{
			if (part is QuestPart_JoinPlayer questPart_JoinPlayer && IsColonyFaction(questPart_JoinPlayer.mapParent?.Faction) && seen.Add(questPart_JoinPlayer.mapParent))
			{
				yield return questPart_JoinPlayer.mapParent;
			}
		}
		foreach (QuestPart part2 in quest.parts)
		{
			if (part2 != null)
			{
				Type type = part2.GetType();
				if (!mapParentFields.TryGetValue(type, out var value))
				{
					value = (mapParentFields[type] = AccessTools.Field(type, "mapParent"));
				}
				if (value?.GetValue(part2) is MapParent mapParent && IsColonyFaction(mapParent.Faction) && seen.Add(mapParent))
				{
					yield return mapParent;
				}
			}
		}
		foreach (QuestPart part3 in quest.parts)
		{
			if (part3 == null)
			{
				continue;
			}
			foreach (GlobalTargetInfo questLookTarget in part3.QuestLookTargets)
			{
				if (questLookTarget.HasWorldObject && questLookTarget.WorldObject is MapParent mapParent2 && IsColonyFaction(mapParent2.Faction) && seen.Add(mapParent2))
				{
					yield return mapParent2;
				}
			}
		}
		if (seen.Count != 0 || quest?.parts == null)
		{
			yield break;
		}
		foreach (QuestPart part4 in quest.parts)
		{
			if (part4 is QuestPart_CreepJoinerOutcomes questPart_CreepJoinerOutcomes)
			{
				MapParent mapParent3 = questPart_CreepJoinerOutcomes.pawn?.MapHeld?.Parent;
				if (mapParent3 != null && IsColonyFaction(mapParent3.Faction) && seen.Add(mapParent3))
				{
					yield return mapParent3;
				}
			}
		}
	}

	public static Faction Owner(Quest quest)
	{
		Faction faction = null;
		foreach (MapParent item in Targets(quest))
		{
			if (faction != null && faction != item.Faction)
			{
				return null;
			}
			faction = item.Faction;
		}
		if (faction != null)
		{
			return faction;
		}
		Faction faction2 = quest?.AccepterPawn?.Faction;
		if (IsColonyFaction(faction2))
		{
			return faction2;
		}
		if (quest?.parent == null)
		{
			return null;
		}
		return Owner(quest.parent);
	}

	public static Map TargetMap(Quest quest)
	{
		using (IEnumerator<MapParent> enumerator = Targets(quest).GetEnumerator())
		{
			if (enumerator.MoveNext())
			{
				return enumerator.Current.Map;
			}
		}
		return null;
	}

	public static MapParent JoinOfferTarget(QuestPart_PawnJoinOffer offer)
	{
		if (offer.quest?.parts != null)
		{
			foreach (QuestPart part in offer.quest.parts)
			{
				if (part is QuestPart_JoinPlayer questPart_JoinPlayer && questPart_JoinPlayer.inSignal == offer.outSignalPawnAccepted && IsColonyFaction(questPart_JoinPlayer.mapParent?.Faction))
				{
					return questPart_JoinPlayer.mapParent;
				}
			}
		}
		using (IEnumerator<MapParent> enumerator2 = Targets(offer.quest).GetEnumerator())
		{
			if (enumerator2.MoveNext())
			{
				return enumerator2.Current;
			}
		}
		return null;
	}

	public static bool ShouldShow(Quest quest, Faction viewer)
	{
		Faction faction = Owner(quest);
		if (faction != null)
		{
			return faction == viewer;
		}
		return true;
	}

	public static AsyncTimeComp FindHomeTime(Quest quest)
	{
		if (Owner(quest) == null)
		{
			return null;
		}
		foreach (MapParent item in Targets(quest))
		{
			Map map = item.Map;
			if (map == null)
			{
				continue;
			}
			FactionContext.Push(item.Faction);
			try
			{
				if (map.IsPlayerHome)
				{
					AsyncTimeComp asyncTimeComp = map.AsyncTime();
					if (asyncTimeComp != null)
					{
						return asyncTimeComp;
					}
				}
			}
			finally
			{
				FactionContext.Pop();
			}
		}
		return null;
	}
}
