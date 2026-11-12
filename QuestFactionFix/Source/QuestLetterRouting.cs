using System.Collections.Generic;
using System.Linq;
using Multiplayer.Client;
using Multiplayer.Client.AsyncTime;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace MultiplayerQuestFactionFix;

public static class QuestLetterRouting
{
	public static List<Faction> Recipients(Letter letter)
	{
		Quest quest = ActionableLetters.QuestFor(letter);
		Faction faction = QuestOwnership.Owner(quest);
		if (quest != null)
		{
			if (faction == null)
			{
				return Colonies();
			}
			List<Faction> list = new List<Faction> { faction };
			if (letter is StandardLetter)
			{
				foreach (Faction item in Colonies())
				{
					if (item != faction && SharedQuestParticipation.IsParticipant(quest, item))
					{
						list.Add(item);
					}
				}
			}
			return list;
		}
		if (!(letter is StandardLetter))
		{
			return null;
		}
		HashSet<Faction> hashSet = new HashSet<Faction>();
		if (letter.lookTargets != null)
		{
			foreach (GlobalTargetInfo target in letter.lookTargets.targets)
			{
				Faction faction2 = target.Map?.ParentFaction ?? target.Thing?.Faction ?? target.WorldObject?.Faction;
				if (QuestOwnership.IsColonyFaction(faction2))
				{
					hashSet.Add(faction2);
				}
			}
		}
		if (hashSet.Count > 0)
		{
			return hashSet.OrderBy((Faction f) => f.loadID).ToList();
		}
		if (!AsyncWorldTimeComp.tickingWorld && Faction.OfPlayer != Multiplayer.Client.Multiplayer.game.worldComp.spectatorFaction)
		{
			return null;
		}
		return Colonies();
	}

	private static List<Faction> Colonies()
	{
		return (from f in Find.FactionManager.AllFactionsListForReading.Where(QuestOwnership.IsColonyFaction)
			orderby f.loadID
			select f).ToList();
	}
}
