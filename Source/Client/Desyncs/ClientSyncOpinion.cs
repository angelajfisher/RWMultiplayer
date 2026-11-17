using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using HarmonyLib;
using Multiplayer.Common;
using Multiplayer.Common.Networking.Packet;
using Verse;

namespace Multiplayer.Client
{

    public class ClientSyncOpinion(int startTick)
    {
        public bool isLocalClientsOpinion;

        public int startTick = startTick;
        public List<uint> commandRandomStates = new();
        public List<uint> worldRandomStates = new();
        public List<MapRandomStateData> mapStates = new();

        // todo Unused for now
        public List<int> pawnCapacityHashes = new();
        public List<int> pawnStatHashes = new();
        public List<int> pawnNeedHashes = new();

        // Serialized only after a desync to reduce bandwidth usage in regular gameplay (only the hashes are used) and
        // help with debugging in case something goes wrong.
        public readonly HeadTailBuffer<StackTraceLogItem> desyncStackTraces =
            new(RetainedHeadCapacity, RetainedTailCapacity, item => item.Dispose());
        public const int RetainedHeadCapacity = 4096;
        public const int RetainedTailCapacity = 32768;
        private readonly TraceFrameStore frames = new();
        private readonly DesyncTraceSummary traceSummary = new();
        private List<int> cachedTraceHashes;
        public List<int> desyncStackTraceHashes
        {
            get => cachedTraceHashes ??= traceSummary.ToWire();
            set => cachedTraceHashes = value;
        }
        public readonly DesyncTraceBudget traceBudget = new();
        public bool simulating;
        public RoundModeEnum roundMode;

        public string CheckForDesync(ClientSyncOpinion other)
        {
            if (roundMode != other.roundMode)
                return $"FP round mode doesn't match: {roundMode} != {other.roundMode}";

            if (!mapStates.Select(m => m.mapId).SequenceEqual(other.mapStates.Select(m => m.mapId)))
                return "Map instances don't match";

            foreach (var g in
                     from map1 in mapStates
                     join map2 in other.mapStates on map1.mapId equals map2.mapId
                     select (map1, map2))
            {
                if (!g.map1.randomStates.SequenceEqual(g.map2.randomStates))
                    return $"Wrong random state on map {g.map1.mapId}";
            }

            if (!worldRandomStates.SequenceEqual(other.worldRandomStates))
                return "Wrong random state for the world";

            if (!commandRandomStates.SequenceEqual(other.commandRandomStates))
                return "Random state from commands doesn't match";

            if (!simulating && !other.simulating && desyncStackTraceHashes.Any() && other.desyncStackTraceHashes.Any() && !desyncStackTraceHashes.SequenceEqual(other.desyncStackTraceHashes))
                return "Trace hashes don't match";

            return null;
        }

        public List<uint> GetRandomStatesForMap(int mapId)
        {
            var result = mapStates.Find(m => m.mapId == mapId);
            if (result != null) return result.randomStates;
            mapStates.Add(result = new MapRandomStateData(mapId));
            return result.randomStates;
        }

        public byte[] Serialize()
        {
            var writer = new ByteWriter();

            writer.WriteInt32(startTick);
            writer.WritePrefixedUInts(commandRandomStates);
            writer.WritePrefixedUInts(worldRandomStates);

            writer.WriteInt32(mapStates.Count);
            foreach (var map in mapStates)
            {
                writer.WriteInt32(map.mapId);
                writer.WritePrefixedUInts(map.randomStates);
            }

            writer.WritePrefixedInts(desyncStackTraceHashes);
            writer.WriteBool(simulating);
            writer.WriteShort((short)roundMode);

            return writer.ToArray();
        }

        public static ClientSyncOpinion FromNet(SyncOpinion sync) => new(sync.startTick)
        {
            commandRandomStates = sync.commandRandomStates,
            worldRandomStates = sync.worldRandomStates,
            mapStates = sync.mapRandomStates.Select(state => new MapRandomStateData(state.mapId)
                { randomStates = state.randomStates }).ToList(),
            desyncStackTraceHashes = sync.traceHashes,
            simulating = sync.simulating,
            roundMode = sync.roundMode
        };

        public SyncOpinion ToNet() => new()
        {
            startTick = startTick,
            commandRandomStates = commandRandomStates,
            worldRandomStates = worldRandomStates,
            mapRandomStates = mapStates.Select(state => new MapRandomState
                { mapId = state.mapId, randomStates = state.randomStates }).ToList(),
            traceHashes = desyncStackTraceHashes,
            simulating = simulating,
            roundMode = roundMode
        };

        public static ClientSyncOpinion Deserialize(ByteReader data)
        {
            var startTick = data.ReadInt32();

            var cmds = new List<uint>(data.ReadPrefixedUInts());
            var world = new List<uint>(data.ReadPrefixedUInts());

            var maps = new List<MapRandomStateData>();
            int mapCount = data.ReadInt32();
            for (int i = 0; i < mapCount; i++)
            {
                int mapId = data.ReadInt32();
                var mapData = new List<uint>(data.ReadPrefixedUInts());
                maps.Add(new MapRandomStateData(mapId) { randomStates = mapData });
            }

            var traceHashes = new List<int>(data.ReadPrefixedInts());
            var simulating = data.ReadBool();
            var roundMode = data.ReadShort();

            return new ClientSyncOpinion(startTick)
            {
                commandRandomStates = cmds,
                worldRandomStates = world,
                mapStates = maps,
                desyncStackTraceHashes = traceHashes,
                simulating = simulating,
                roundMode = (RoundModeEnum)roundMode
            };
        }

        public void TryMarkSimulating()
        {
            if (TickPatch.Simulating)
                simulating = true;
        }

        public void AddDesyncTrace(StackTraceLogItem item)
        {
            traceSummary.Add(item.hash);
            if (item is StackTraceLogItemRaw raw)
            {
                var retained = RetainedStackTrace.Acquire(raw, frames);
                raw.Dispose();
                item = retained;
            }
            desyncStackTraces.Add(item);
            cachedTraceHashes = null;
        }

        public string GetFormattedStackTracesForRange(int diffAt)
            => GetFormattedStackTracesForRange(diffAt, int.MaxValue);

        public string GetFormattedStackTracesForRange(int diffAt, int radius)
        {
            long total = DesyncTraceSummary.CaptureCount(desyncStackTraceHashes);
            long evicted = total - desyncStackTraces.Count;
            var text = new StringBuilder()
                .AppendLine($"Captured stacks: {total}; retained: {desyncStackTraces.Count}; evicted: {evicted}.")
                .AppendLine($"Eligible events: {traceBudget.EventsSeen}; burst events skipped: {traceBudget.EventsSkipped}.")
                .AppendLine($"Full capture through {DesyncTraceBudget.FullTracesPerTick} events per tick; only larger ticks use power-of-two samples.")
                .AppendLine($"Retains first {RetainedHeadCapacity} and latest {RetainedTailCapacity} captured stacks. RNG-state checks remain enabled.");
            if (diffAt == DesyncTraceSummary.EarlierHistoryDiffers)
                text.AppendLine("A difference occurred outside the transmitted hash window. Compare the retained event histories below; full stacks may still be present locally.");
            else if (diffAt >= 0 && desyncStackTraces.TryGet(diffAt, out var mismatch))
                text.AppendLine($"First retained mismatch at capture {diffAt}:\n{mismatch}");
            else if (diffAt >= total && diffAt >= 0)
                text.AppendLine($"Capture {diffAt} is missing on this peer.");
            else if (diffAt >= 0)
                text.AppendLine($"Requested capture {diffAt} is in the evicted gap.");
            text.AppendLine("\nRetained context (global capture indices; TraceEvent is the original eligible event number):");
            var stackIds = new Dictionary<object, int>();
            long previous = -1;
            foreach (var entry in desyncStackTraces.Entries())
            {
                if (radius != int.MaxValue && diffAt >= 0 && Math.Abs(entry.Key - diffAt) > radius) continue;
                if (entry.Key > previous + 1) text.AppendLine($"[captures {previous + 1}..{entry.Key - 1} not retained]");
                var item = entry.Value;
                object identity = item is StackTraceLogItemRaw raw ? raw.raw : item;
                bool fresh = !stackIds.TryGetValue(identity, out int stackId);
                if (fresh) stackIds.Add(identity, stackId = stackIds.Count + 1);
                string stack = $"Tick:{item.tick} TraceEvent:{item.traceEvent} Hash:{item.hash} '{item.AdditionalInfo}' Stack:{stackId}";
                if (fresh) stack += $"\nStack definition {stackId}:\n{item.StackTraceString}";
                if (text.Length + stack.Length > TracePayload.MaxReportChars)
                {
                    text.AppendLine("[report text limit reached; remaining retained stacks omitted]");
                    break;
                }
                text.AppendLine($"{entry.Key} {stack}\n");
                previous = entry.Key;
            }
            return text.ToString();
        }

        public void Clear()
        {
            desyncStackTraces.Clear();
            traceSummary.Clear();
            cachedTraceHashes = null;
        }
    }
}
