using System;
using System.Collections.Generic;
using System.Linq;
using MultiplayerQuestFactionFix;
using RimWorld;
using RimWorld.Planet;
using Verse;

static partial class Program
{
    class HiddenParentPart : QuestPart
    {
        private MapParent mapParent;
        public void Retarget(MapParent parent) => mapParent = parent;
    }
    class InheritedParentPart : HiddenParentPart { }
    class ThrowingLookPart : QuestPart
    {
        public override IEnumerable<GlobalTargetInfo> QuestLookTargets => throw new InvalidOperationException("fixture lookup");
    }
    class NestedLookPart : QuestPart
    {
        public Quest nested;
        public Faction observed;
        public override IEnumerable<GlobalTargetInfo> QuestLookTargets
        {
            get { observed = QuestOwnership.Owner(nested); yield break; }
        }
    }
    static void TestFreshLookups(Faction colony, Faction other)
    {
        var ownMap = MakeTarget(colony); var otherMap = MakeTarget(other);
        var hidden = new InheritedParentPart(); hidden.Retarget(ownMap);
        var quest = new Quest { id = 456789 };
        Set(quest, "parts", new List<QuestPart> { hidden });
        Check(QuestOwnership.ResolveContext(quest).Key == colony, "compiled map reader supports inherited private fields");
        hidden.Retarget(otherMap);
        Check(QuestOwnership.ResolveContext(quest).Key == other, "direct retarget within a tick is visible on the next batched lookup");
        Set(otherMap, "factionInt", colony);
        Check(QuestOwnership.Owner(quest) == colony, "map faction transfer is visible without a stale ownership cache");
        Set(otherMap, "factionInt", other);
        Set(quest, "parts", new List<QuestPart> { new QuestPart_JoinPlayer { mapParent = ownMap } });
        Check(QuestOwnership.Owner(quest) == colony, "replacing a same-size parts list invalidates lookup results naturally");
        Set(quest, "parts", new List<QuestPart> { new QuestPart_JoinPlayer { mapParent = ownMap }, hidden });
        Check(QuestOwnership.ResolveContext(quest).Key == null, "batched lookup preserves multi-colony classification");
        Check(QuestOwnership.Targets(quest).First() == ownMap, "target precedence survives early enumeration disposal");
        var nested = new Quest { id = 456790 };
        Set(nested, "parts", new List<QuestPart> { hidden });
        var look = new NestedLookPart { nested = nested };
        Set(quest, "parts", new List<QuestPart> { new QuestPart_JoinPlayer { mapParent = ownMap }, look });
        Check(QuestOwnership.Owner(quest) == colony && look.observed == other, "reentrant look-target queries use independent scratch buffers");
        Set(quest, "parts", new List<QuestPart> { new ThrowingLookPart() });
        bool threw = false;
        try { QuestOwnership.ResolveContext(quest); } catch (InvalidOperationException) { threw = true; }
        Set(quest, "parts", new List<QuestPart> { hidden });
        Check(threw && QuestOwnership.ResolveContext(quest).Key == other, "exception cleanup clears reusable lookup buffers");
    }
}
