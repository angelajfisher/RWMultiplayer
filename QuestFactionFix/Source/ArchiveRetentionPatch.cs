using HarmonyLib;
using RimWorld;
using Verse;

namespace MultiplayerQuestFactionFix;

// Frequent short messages must not consume the entire letter history allowance.
[HarmonyPatch(typeof(Archive), "CheckCullArchivables")]
internal static class ArchiveRetentionPatch
{
    private static bool Prefix(Archive __instance)
    {
        if (!QuestOwnership.Enabled) return true;
        var entries = __instance.ArchivablesListForReading;
        int letters = 0, other = 0;
        foreach (var entry in entries)
        {
            if (__instance.IsPinned(entry) || !entry.CanCullArchivedNow) continue;
            if (entry is Letter) letters++; else other++;
        }
        int excessLetters = letters - Archive.MaxNonPinnedArchivables;
        int excessOther = other - Archive.MaxNonPinnedArchivables;
        for (int i = 0; i < entries.Count && (excessLetters > 0 || excessOther > 0);)
        {
            var entry = entries[i];
            if (!__instance.IsPinned(entry) && entry.CanCullArchivedNow &&
                (entry is Letter ? excessLetters > 0 : excessOther > 0))
            {
                __instance.Remove(entry);
                if (entry is Letter) excessLetters--; else excessOther--;
            }
            else i++;
        }
        return false;
    }
}
