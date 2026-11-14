using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Multiplayer.Client.Comp;
using Multiplayer.Client;
using RimWorld;
using MP = Multiplayer.Client.Multiplayer;
using Multiplayer.Common;
using Verse;

namespace ThreatPauseVotes;

public sealed class ThreatPauseVotesMod : Mod
{
    public const string HarmonyId = "angelajfisher.threatpausevotes";
    public ThreatPauseVotesMod(ModContentPack content) : base(content)
    {
        ApplyPatches();
        Log.Message("[Threat Pause Votes] v0.2.0: letter auto-pauses request only recipients' pause votes.");
    }
    public static void ApplyPatches() => new Harmony(HarmonyId).PatchAll(typeof(ThreatPauseVotesMod).Assembly);
}

public static class PauseVotes
{
    public static bool Receives(Letter letter, Faction viewer, Faction context)
    {
        if (viewer?.def?.isPlayer != true || viewer == MP.game?.worldComp?.spectatorFaction) return false;
        // The quest addon also knows about accepted allies and public quests.
        var routing = AccessTools.TypeByName("MultiplayerQuestFactionFix.QuestLetterRouting");
        if (letter != null && routing != null)
        {
            var recipients = AccessTools.Method(routing, "Recipients")?.Invoke(null, new object[] { letter }) as IEnumerable<Faction>;
            if (recipients != null)
            {
                foreach (var faction in recipients) if (faction == viewer) return true;
                return false;
            }
        }
        bool targeted = false;
        bool ours = false;
        if (letter?.lookTargets != null)
            foreach (var target in letter.lookTargets.targets)
            {
                var owner = target.Map?.ParentFaction ?? target.Thing?.Faction ?? target.WorldObject?.Faction;
                if (owner?.def?.isPlayer != true || owner == MP.game?.worldComp?.spectatorFaction) continue;
                targeted = true;
                if (owner == viewer) ours = true;
            }
        if (targeted) return ours;
        // World/public notifications reach all colonies; private context has one owner.
        return context == null || context == MP.game?.worldComp?.spectatorFaction || context == viewer;
    }

    public static void SetPaused(MultiplayerGameComp game, int tickableId)
    {
        if (MP.Client == null || !game.IsLowestWins || MP.IsReplay || TickPatch.Simulating || MP.session.desynced) return;
        if (!Receives(LetterContext.Current, MP.RealPlayerFaction, Faction.OfPlayer)) return;
        if (game.LocalPlayerDataOrNull?.GetTimeVoteOrNull(tickableId) == TimeVote.Paused) return;
        // Emit the same scheduled command as a user selecting pause. Never mutate
        // shared simulation data based on which client happens to be running it.
        MP.Client.SendCommand(CommandType.TimeSpeedVote, ScheduledCommand.Global, (byte)TimeVote.Paused, tickableId);
    }
}

[HarmonyPatch(typeof(LetterStack), nameof(LetterStack.ReceiveLetter), new[] { typeof(Letter), typeof(string), typeof(int), typeof(bool) })]
static class LetterContext
{
    [ThreadStatic] private static Stack<Letter> letters;
    internal static Letter Current => letters != null && letters.Count > 0 ? letters.Peek() : null;
    [HarmonyPriority(Priority.Last)]
    static void Prefix(Letter let, out bool __state)
    {
        (letters ??= new Stack<Letter>()).Push(let);
        __state = true;
    }
    static void Finalizer(bool __state) { if (__state) letters.Pop(); }
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
