using System.Collections.Generic;
using System.Linq;
using Multiplayer.Client;
using RimWorld;
using RimWorld.QuestGen;
using Verse;

namespace MultiplayerQuestFactionFix;

public sealed class PendingOfferRecovery : GameComponent
{
	private bool checkedOffers;

	public PendingOfferRecovery(Game game)
	{
	}

	public override void FinalizeInit()
	{
		TryRecover();
	}

	public override void GameComponentTick()
	{
		if (!checkedOffers)
		{
			TryRecover();
		}
	}

	private void TryRecover()
	{
		if (QuestOwnership.Enabled && Find.LetterStack != null && Multiplayer.Client.Multiplayer.game.worldComp?.factionData != null)
		{
			checkedOffers = true;
			Recover();
		}
	}

	public static bool CanRecover(ChoiceLetter_AcceptCreepJoiner offer)
	{
		if (offer?.quest != null && !offer.quest.Historical && offer.pawn != null && offer.pawn.Spawned && !offer.pawn.Dead && !offer.pawn.IsPrisoner)
		{
			Faction faction = offer.pawn.Faction;
			if ((faction == null || faction.def?.isPlayer != true) && !offer.TimeoutPassed)
			{
				if (QuestOwnership.IsColonyFaction(QuestOwnership.Owner(offer.quest)))
				{
					return offer.quest.parts.OfType<QuestPart_CreepJoinerOutcomes>().Any((QuestPart_CreepJoinerOutcomes p) => p.pawn == offer.pawn && p.signalAccept == offer.signalAccept && p.signalReject == offer.signalReject && p.signalCapture == offer.signalCapture);
				}
				return false;
			}
		}
		return false;
	}

	public static void Recover()
	{
		HashSet<ChoiceLetter_AcceptCreepJoiner> hashSet = new HashSet<ChoiceLetter_AcceptCreepJoiner>();
		foreach (FactionWorldData value in Multiplayer.Client.Multiplayer.game.worldComp.factionData.Values)
		{
			foreach (IArchivable archivable in value.history.archive.archivables)
			{
				if (archivable is ChoiceLetter_AcceptCreepJoiner item)
				{
					hashSet.Add(item);
				}
			}
		}
		foreach (ChoiceLetter_AcceptCreepJoiner item2 in hashSet.OrderBy((ChoiceLetter_AcceptCreepJoiner offer) => offer.ID).ToList())
		{
			if (!CanRecover(item2))
			{
				continue;
			}
			Faction faction = QuestOwnership.Owner(item2.quest);
			Archive archive = Multiplayer.Client.Multiplayer.game.worldComp.factionData[faction.loadID].history.archive;
			if (!archive.Contains(item2))
			{
				foreach (FactionWorldData value2 in Multiplayer.Client.Multiplayer.game.worldComp.factionData.Values)
				{
					value2.history.archive.archivables.Remove(item2);
				}
				archive.Add(item2);
			}
			if (!Find.LetterStack.LettersListForReading.Contains(item2))
			{
				Find.LetterStack.LettersListForReading.Add(item2);
			}
			Log.Message($"[MP Quest Faction Fix] Restored pending creep-joiner offer {item2.ID}, quest {item2.quest.id}, pawn {item2.pawn.ThingID}, owner {faction.loadID}.");
		}
	}
}
