using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Verse;

namespace MultiplayerQuestFactionFix;

[HarmonyPatch(typeof(LetterStack), "LettersOnGUI")]
internal static class PrivateChoiceLayoutPatch
{
	private static List<Letter> VisibleLetters(LetterStack stack)
	{
		if (!QuestOwnership.Enabled)
		{
			return stack.LettersListForReading;
		}
		return stack.LettersListForReading.FindAll(ActionableLetters.VisibleToViewer);
	}

	private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
	{
		FieldInfo field = AccessTools.Field(typeof(LetterStack), "letters");
		MethodInfo replacement = AccessTools.Method(typeof(PrivateChoiceLayoutPatch), "VisibleLetters");
		int replaced = 0;
		foreach (CodeInstruction instruction in instructions)
		{
			if (instruction.opcode == OpCodes.Ldfld && object.Equals(instruction.operand, field))
			{
				instruction.opcode = OpCodes.Call;
				instruction.operand = replacement;
				replaced++;
			}
			yield return instruction;
		}
		if (replaced == 0)
		{
			throw new InvalidOperationException("Letter layout no longer reads the expected letter list.");
		}
	}
}
