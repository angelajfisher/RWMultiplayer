using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Multiplayer.Client;
using MultiplayerQuestFactionFix;
using RimWorld;
using Verse;
using MP = Multiplayer.Client.Multiplayer;

static partial class Program
{
    static void TestSpectatorNotifications(Quest owned, Quest publicQuest, Faction colony, Faction other, Faction spectator)
    {
        var publicOffer = new NewQuestLetter { quest = publicQuest, ID = 8501 };
        var ownerlessDecision = new ChoiceLetter_AcceptCreepJoiner { quest = publicQuest, ID = 8502 };
        var privateOffer = new NewQuestLetter { quest = owned, ID = 8503 };
        var failure = new StandardLetter { quest = publicQuest, ID = 8504 };
        var worldEvent = new StandardLetter { ID = 8505 };
        var stack = new LetterStack();
        var backlog = new Letter[] { publicOffer, ownerlessDecision, privateOffer, failure, worldEvent };
        stack.LettersListForReading.AddRange(backlog);
        var layout = AccessTools.Method(typeof(ActionableLetters).Assembly.GetType("MultiplayerQuestFactionFix.PrivateChoiceLayoutPatch"), "VisibleLetters");
        var autoOpen = AccessTools.Method(typeof(ActionableLetters).Assembly.GetType("MultiplayerQuestFactionFix.PrivateChoiceAutoOpenPatch"), "Postfix");
        var openOffer = AccessTools.Method(typeof(ActionableLetters).Assembly.GetType("MultiplayerQuestFactionFix.QuestOfferOpenPatch"), "Prefix");
        // Joiner may still have a colony simulation context while its local
        // viewer is a spectator. The local faction must drive presentation.
        MP.game.myFactionLoading = spectator;
        FactionContext.Push(colony);
        try
        {
            Check(!ActionableLetters.VisibleToViewer(publicOffer), "spectator cannot see public quest notifications restored on join");
            Check(!ActionableLetters.VisibleToViewer(ownerlessDecision), "spectator cannot see ownerless pending quest decisions");
            Check(!ActionableLetters.VisibleToViewer(privateOffer) && !ActionableLetters.VisibleToViewer(failure), "spectator cannot see private offers or public quest outcome letters");
            Check(ActionableLetters.VisibleToViewer(worldEvent), "spectator retains ordinary non-quest events");
            var visible = (List<Letter>)layout.Invoke(null, new object[] { stack });
            Check(visible.SequenceEqual(new[] { worldEvent }), "spectator's existing quest backlog consumes no letter-layout slots");
            object[] args = { ownerlessDecision, true }; autoOpen.Invoke(null, args);
            Check(!(bool)args[1], "spectator quest decisions cannot automatically open");
            Check(!(bool)openOffer.Invoke(null, new object[] { publicOffer }), "direct opening cannot bypass spectator quest notification suppression");
            Check(stack.LettersListForReading.SequenceEqual(backlog), "spectator filtering preserves pending decisions and all shared letter IDs");
        }
        finally { FactionContext.Pop(); }
        MP.game.myFactionLoading = colony;
        Check(ActionableLetters.VisibleToViewer(publicOffer) && ActionableLetters.VisibleToViewer(privateOffer), "switching from spectator into the owning colony restores eligible quest offers");
        MP.game.myFactionLoading = other;
        Check(ActionableLetters.VisibleToViewer(publicOffer), "public quest notifications remain available to other colonies");
        Check(!ActionableLetters.VisibleToViewer(privateOffer), "foreign private offers stay hidden from uninvolved colonies");
        MP.game.myFactionLoading = colony;
    }
}
