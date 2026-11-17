using System;
using System.Collections.Generic;
using System.Text;
using Multiplayer.Client.Desyncs;
using Verse;
using Multiplayer.Common;

namespace Multiplayer.Client
{
    public abstract class StackTraceLogItem : IDisposable
    {
        public int tick;
        public long traceEvent;
        public int hash;

        public abstract string AdditionalInfo { get; }

        public abstract string StackTraceString { get; }

        public override string ToString()
        {
            return $"Tick:{tick} TraceEvent:{traceEvent} Hash:{hash} '{AdditionalInfo}'\n{StackTraceString}";
        }

        public virtual void Dispose()
        {
        }
    }

    public class StackTraceLogItemObj : StackTraceLogItem
    {
        public string info1;
        public string info2;

        public override string AdditionalInfo => $"{info1} {info2}";

        public override string StackTraceString => "";
    }

    public class StackTraceLogItemRaw : StackTraceLogItem
    {
        public const int MaxDepth = 32;
        public long[] raw;
        public StackTraceLogItemRaw() : this(true) { }
        protected StackTraceLogItemRaw(bool allocate) { raw = allocate ? new long[MaxDepth] : null; }
        public int depth;

        public int ticksGame;
        public ulong rngState;
        public ThingDef thingDef;
        public int thingId;
        public string factionName;
        public string moreInfo;

        public override string AdditionalInfo => $"{ticksGame} {thingDef}{thingId} {factionName} {depth} {rngState} {moreInfo}";

        private static Dictionary<long, string> methodNameCache = new();

        public override string StackTraceString
        {
            get
            {
                var builder = new StringBuilder();
                for (int i = 0; i < depth; i++)
                {
                    var addr = raw[i];

                    if (!methodNameCache.TryGetValue(addr, out string method))
                        methodNameCache[addr] = method = Native.MethodNameFromAddr(raw[i], false);

                    builder.AppendLine(method != null ? SyncCoordinator.MethodNameWithIL(method) : "Null");
                }

                return builder.ToString();
            }
        }

        public static StackTraceLogItemRaw GetFromPool()
        {
            return SimplePool<StackTraceLogItemRaw>.Get();
        }

        public override void Dispose()
        {
            depth = 0;
            traceEvent = 0;
            ticksGame = 0;
            rngState = 0;
            thingId = 0;
            thingDef = null;
            factionName = null;
            moreInfo = null;

            SimplePool<StackTraceLogItemRaw>.Return(this);
        }
    }
    // Owns metadata, but shares immutable frames. The capture object is returned
    // to its pool immediately instead of pinning a 32-frame array per event.
    public sealed class RetainedStackTrace : StackTraceLogItemRaw
    {
        private TraceFrameStore store;
        private TraceFrameStore.Entry entry;
        // Captures/opinions run on the simulation thread, like Verse.SimplePool.
        // Bound the reusable records to one full history, not the 30-opinion backlog.
        private const int PoolLimit = ClientSyncOpinion.RetainedHeadCapacity + ClientSyncOpinion.RetainedTailCapacity + 1;
        private static readonly Stack<RetainedStackTrace> pool = new();
        public static int PooledCount => pool.Count;
        private RetainedStackTrace() : base(false) { }
        public static RetainedStackTrace Acquire(StackTraceLogItemRaw source, TraceFrameStore store)
        {
            var entry = store.Acquire(source.raw, source.depth);
            var retained = pool.Count > 0 ? pool.Pop() : new RetainedStackTrace();
            retained.store = store; retained.entry = entry; retained.raw = entry.Frames;
            retained.depth = source.depth; retained.tick = source.tick; retained.traceEvent = source.traceEvent; retained.hash = source.hash;
            retained.ticksGame = source.ticksGame; retained.rngState = source.rngState; retained.thingDef = source.thingDef;
            retained.thingId = source.thingId; retained.factionName = source.factionName; retained.moreInfo = source.moreInfo;
            return retained;
        }
        public override void Dispose()
        {
            if (store == null) return;
            store.Release(entry);
            store = null; entry = null; raw = null;
            depth = tick = ticksGame = hash = thingId = 0;
            traceEvent = 0; rngState = 0;
            thingDef = null; factionName = null; moreInfo = null;
            if (pool.Count < PoolLimit) pool.Push(this);
        }
    }

}
