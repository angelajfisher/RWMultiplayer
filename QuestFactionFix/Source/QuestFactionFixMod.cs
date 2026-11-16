using HarmonyLib;
using Verse;

namespace MultiplayerQuestFactionFix;

public sealed class QuestFactionFixMod : Mod
{
	public const string HarmonyId = "angelajfisher.multiplayerquestfactionfix";

	public QuestFactionFixMod(ModContentPack content)
		: base(content)
	{
		ApplyPatches();
		Log.Message("[MP Quest Faction Fix] v0.3.5 loaded: quest context, shared/global visibility and notification routing.");
	}

	public static void ApplyPatches()
	{
		new Harmony(HarmonyId).PatchAll(typeof(QuestFactionFixMod).Assembly);
	}
}
