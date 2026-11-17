using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Multiplayer.Client;
using RimWorld;
using RimWorld.Planet;
using RimWorld.QuestGen;
using Verse;

namespace MultiplayerQuestFactionFix;

public static class QuestOwnership
{
	private static readonly Dictionary<Type, Func<QuestPart, MapParent>> mapParentReaders = new();
    private sealed class TargetBuffer
    {
        internal readonly List<MapParent> targets = new();
        internal readonly HashSet<MapParent> seen = new();
        internal void Add(MapParent target)
        {
            if (IsColonyFaction(target?.Faction) && seen.Add(target)) targets.Add(target);
        }
    }
    [ThreadStatic] private static Stack<TargetBuffer> buffers;
    private static TargetBuffer Borrow()
    {
        buffers ??= new();
        return buffers.Count > 0 ? buffers.Pop() : new TargetBuffer();
    }
    private static void Return(TargetBuffer buffer)
    {
        buffer.targets.Clear(); buffer.seen.Clear();
        if (buffers.Count < 8) buffers.Push(buffer);
    }
    private static MapParent NoParent(QuestPart part) => null;
    private static Func<QuestPart, MapParent> ParentReader(Type type)
    {
        if (mapParentReaders.TryGetValue(type, out var reader)) return reader;
        var field = AccessTools.Field(type, "mapParent");
        if (field == null || field.FieldType.IsValueType) reader = NoParent;
        else
        {
            var method = new DynamicMethod("QuestMapParent_" + type.Name, typeof(MapParent), new[] { typeof(QuestPart) }, typeof(QuestOwnership).Module, true);
            var il = method.GetILGenerator();
            if (field.IsStatic) il.Emit(OpCodes.Ldsfld, field);
            else
            {
                il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Castclass, field.DeclaringType); il.Emit(OpCodes.Ldfld, field);
            }
            il.Emit(OpCodes.Isinst, typeof(MapParent)); il.Emit(OpCodes.Ret);
            reader = (Func<QuestPart, MapParent>)method.CreateDelegate(typeof(Func<QuestPart, MapParent>));
        }
        mapParentReaders.Add(type, reader);
        return reader;
    }
    private static void Gather(Quest quest, TargetBuffer buffer)
    {
        var parts = quest?.parts;
        if (parts == null) return;
        // Keep the original precedence: recruitment, explicit map fields,
        // look targets, then a creep joiner's current map as a fallback.
        foreach (var part in parts)
            if (part is QuestPart_JoinPlayer join) buffer.Add(join.mapParent);
        foreach (var part in parts)
            if (part != null && part is not QuestPart_JoinPlayer) buffer.Add(ParentReader(part.GetType())(part));
        foreach (var part in parts)
        {
            if (part == null) continue;
            foreach (var target in part.QuestLookTargets)
                if (target.HasWorldObject && target.WorldObject is MapParent parent) buffer.Add(parent);
        }
        if (buffer.targets.Count != 0) return;
        foreach (var part in parts)
            if (part is QuestPart_CreepJoinerOutcomes creep) buffer.Add(creep.pawn?.MapHeld?.Parent);
    }

	public static bool Enabled
	{
		get
		{
			if (Multiplayer.Client.Multiplayer.Client != null)
			{
				MultiplayerGame game = Multiplayer.Client.Multiplayer.game;
				if (game == null)
				{
					return false;
				}
				return game.gameComp?.multifaction == true;
			}
			return false;
		}
	}

	public static bool IsColonyFaction(Faction faction)
	{
		if (faction != null && faction.def?.isPlayer == true)
		{
			return faction != Multiplayer.Client.Multiplayer.game?.worldComp?.spectatorFaction;
		}
		return false;
	}

    // Scratch data lives for one lookup only: direct part-field writes, faction
    // transfers, pawn movement and cleanup are visible on the very next query.
    public static IEnumerable<MapParent> Targets(Quest quest)
    {
        var buffer = Borrow();
        try { Gather(quest, buffer); foreach (var target in buffer.targets) yield return target; }
        finally { Return(buffer); }
    }
    private static Faction OwnerFromTargets(Quest quest, List<MapParent> targets)
    {
        Faction owner = null;
        foreach (var target in targets)
        {
            if (owner != null && owner != target.Faction) return null;
            owner = target.Faction;
        }
        if (owner != null) return owner;
        var remembered = QuestOwnerMemory.Owner(quest);
        if (IsColonyFaction(remembered)) return remembered;
        var accepter = quest?.AccepterPawn?.Faction;
        if (IsColonyFaction(accepter)) return accepter;
        return quest?.parent == null ? null : Owner(quest.parent);
    }
    public static Faction Owner(Quest quest)
    {
        var buffer = Borrow();
        try { Gather(quest, buffer); return OwnerFromTargets(quest, buffer.targets); }
        finally { Return(buffer); }
    }
    public static KeyValuePair<Faction, Map> ResolveContext(Quest quest)
    {
        var buffer = Borrow();
        try
        {
            Gather(quest, buffer);
            return new KeyValuePair<Faction, Map>(OwnerFromTargets(quest, buffer.targets), buffer.targets.Count == 0 ? null : buffer.targets[0].Map);
        }
        finally { Return(buffer); }
    }
    public static QuestFactionScope Enter(Quest quest)
    {
        var context = ResolveContext(quest);
        return QuestFactionScope.Enter(context.Key, context.Value);
    }
    public static Map TargetMap(Quest quest)
    {
        var buffer = Borrow();
        try { Gather(quest, buffer); return buffer.targets.Count == 0 ? null : buffer.targets[0].Map; }
        finally { Return(buffer); }
    }

	public static MapParent JoinOfferTarget(QuestPart_PawnJoinOffer offer)
	{
		if (offer.quest?.parts != null)
		{
			foreach (QuestPart part in offer.quest.parts)
			{
				if (part is QuestPart_JoinPlayer questPart_JoinPlayer && questPart_JoinPlayer.inSignal == offer.outSignalPawnAccepted && IsColonyFaction(questPart_JoinPlayer.mapParent?.Faction))
				{
					return questPart_JoinPlayer.mapParent;
				}
			}
		}
		using (IEnumerator<MapParent> enumerator2 = Targets(offer.quest).GetEnumerator())
		{
			if (enumerator2.MoveNext())
			{
				return enumerator2.Current;
			}
		}
		return null;
	}

	public static bool ShouldShow(Quest quest, Faction viewer)
	{
		Faction faction = Owner(quest);
		if (faction != null)
		{
			return faction == viewer || SharedQuestParticipation.CanView(quest, viewer);
		}
		return true;
	}

    public static AsyncTimeComp FindHomeTime(Quest quest)
    {
        var buffer = Borrow();
        try
        {
            Gather(quest, buffer);
            if (OwnerFromTargets(quest, buffer.targets) == null) return null;
            foreach (var target in buffer.targets)
            {
                var map = target.Map;
                if (map == null) continue;
                FactionContext.Push(target.Faction);
                try
                {
                    if (map.IsPlayerHome && map.AsyncTime() is AsyncTimeComp time) return time;
                }
                finally { FactionContext.Pop(); }
            }
            return null;
        }
        finally { Return(buffer); }
    }
}
