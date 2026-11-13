using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Multiplayer.Client.Comp;
using Multiplayer.Common;
using Verse;

namespace ThreatPauseVotes;

public sealed class ThreatPauseVotesMod : Mod
{
    public const string HarmonyId = "angelajfisher.threatpausevotes";
    public ThreatPauseVotesMod(ModContentPack content) : base(content)
    {
        ApplyPatches();
        Log.Message("[Threat Pause Votes] v0.1.0: letter auto-pauses retain explicit paused speed votes.");
    }
    public static void ApplyPatches() => new Harmony(HarmonyId).PatchAll(typeof(ThreatPauseVotesMod).Assembly);
}

public static class PauseVotes
{
    public static void SetPaused(MultiplayerGameComp game, int tickableId)
    {
        foreach (var player in game.playerData.Values)
            // Absent votes include disconnected/desynced player records. Do not
            // reactivate them, or no online player could release their pause.
            if (player.GetTimeVoteOrNull(tickableId) != null)
                player.SetTimeVote(tickableId, TimeVote.Paused);
    }
}

[HarmonyPatch]
static class LetterAutoPausePatch
{
    static MethodBase TargetMethod() => AccessTools.Method(
        AccessTools.TypeByName("Multiplayer.Client.AsyncTime.ReceiveLetterPause"), "PauseOnLetter") ??
        throw new MissingMethodException("Multiplayer's letter auto-pause method has changed.");

    static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var reset = AccessTools.Method(typeof(MultiplayerGameComp), nameof(MultiplayerGameComp.ResetAllTimeVotes));
        var replacement = AccessTools.Method(typeof(PauseVotes), nameof(PauseVotes.SetPaused));
        var result = new List<CodeInstruction>();
        int matches = 0;
        foreach (var instruction in instructions)
        {
            if (instruction.Calls(reset))
            {
                instruction.opcode = OpCodes.Call;
                instruction.operand = replacement;
                matches++;
            }
            result.Add(instruction);
        }
        // Both async-map and synchronous-world branches must be covered.
        if (matches != 2) throw new InvalidOperationException($"Expected two letter auto-pause vote resets; found {matches}.");
        return result;
    }
}
