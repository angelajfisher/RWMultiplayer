using System;
using System.Collections.Generic;
using HarmonyLib;
using Multiplayer.Client;
using RimWorld;
using Verse;
using Verse.Sound;

namespace MultiplayerQuestFactionFix;

[HarmonyPatch(typeof(LetterStack), "ReceiveLetter", new Type[]
{
	typeof(Letter),
	typeof(string),
	typeof(int),
	typeof(bool)
})]
internal static class QuestLetterRoutingPatch
{
	private sealed class Delivery
	{
		internal List<Faction> recipients;

		internal QuestFactionScope scope;
	}

	[HarmonyPriority(800)]
	private static void Prefix(Letter let, int delayTicks, ref Delivery __state)
	{
		if (QuestOwnership.Enabled && let != null && delayTicks <= 0 && let.CanShowInLetterStack)
		{
            if (DiplomaticIncidentDelivery.Recipient is Faction affected && let is StandardLetter)
            {
                string suffix = " (" + affected.Name + ")";
                if (!let.Label.ToString().EndsWith(suffix, StringComparison.Ordinal)) let.Label = let.Label + suffix;
            }
			List<Faction> list = QuestLetterRouting.Recipients(let);
			if (list != null && list.Count != 0 && (let is StandardLetter || ActionableLetters.IsActionable(let)))
			{
				__state = new Delivery
				{
					recipients = list
				};
				__state.scope = QuestFactionScope.Enter(QuestOwnership.Owner(ActionableLetters.QuestFor(let)) ?? (ActionableLetters.IsActionable(let) ? null : list[0]), null);
			}
		}
	}

	[HarmonyPriority(int.MinValue)]
	private static void Postfix(LetterStack __instance, Letter let, int delayTicks, bool playSound, Delivery __state)
	{
        // Preserve shared decision inputs even when the owner is unresolved/public.
        // Rendering filters visibility; simulation state must be the same on all peers.
        if (QuestOwnership.Enabled && delayTicks <= 0 && let.CanShowInLetterStack && ActionableLetters.IsActionable(let))
        {
            if (!__instance.LettersListForReading.Contains(let))
                __instance.LettersListForReading.Add(let);
            return;
        }
        if (__state == null) return;
		List<Letter> lettersListForReading = __instance.LettersListForReading;
		bool flag = lettersListForReading.Remove(let);
		Letter letter = null;
		foreach (Faction recipient in __state.recipients)
		{
			Letter letter2 = let;
			if (recipient != __state.recipients[0])
			{
				letter2 = Gen.MemberwiseClone(let);
				letter2.ID = Find.UniqueIDsManager.GetNextLetterID();
				Multiplayer.Client.Multiplayer.game.worldComp.factionData[recipient.loadID].history.archive.Add(letter2);
			}
			if (recipient == Multiplayer.Client.Multiplayer.RealPlayerFaction)
			{
				letter = letter2;
			}
		}
		if (letter != null)
		{
			lettersListForReading.Add(letter);
			if (playSound && !flag && letter.def?.arriveSound != null)
			{
				letter.def.arriveSound.PlayOneShotOnCamera();
			}
		}
	}

	private static void Finalizer(Delivery __state)
	{
		__state?.scope?.Dispose();
	}
}
