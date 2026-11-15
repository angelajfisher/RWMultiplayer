using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Multiplayer.Client;
using RimWorld;
using Verse;

namespace MultiplayerQuestFactionFix;

public static class ActionableLetters
{
    private static readonly Dictionary<Type, FieldInfo> questFields = new();

    // Modded offers can inherit LetterWithTimeout directly instead of ChoiceLetter.
    public static Quest QuestFor(Letter letter)
    {
        if (letter == null) return null;
        if (letter is ChoiceLetter choice) return choice.quest;
        var type = letter.GetType();
        if (!questFields.TryGetValue(type, out var field))
        {
            field = AccessTools.Field(type, "quest");
            if (field?.FieldType != typeof(Quest)) field = null;
            questFields[type] = field;
        }
        return field?.GetValue(letter) as Quest;
    }

    public static bool IsActionable(Letter letter) => letter is not StandardLetter && QuestFor(letter) != null;

    public static bool VisibleToViewer(Letter letter)
    {
        if (!QuestOwnership.Enabled) return true;
        var quest = QuestFor(letter);
        if (quest == null) return true;
        // Ordinary quest-offer letters can open the same acceptance UI.
        if (letter is StandardLetter || letter is NewQuestLetter) return QuestOwnership.ShouldShow(quest, Multiplayer.Client.Multiplayer.RealPlayerFaction);
        var owner = QuestOwnership.Owner(QuestFor(letter));
        return owner == null || owner == Multiplayer.Client.Multiplayer.RealPlayerFaction;
    }
}
