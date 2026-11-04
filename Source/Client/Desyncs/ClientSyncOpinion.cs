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
        public readonly BoundedRingBuffer<StackTraceLogItem> desyncStackTraces =
            new(DesyncTraceSummary.Capacity, item => item.Dispose());
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
            desyncStackTraces.Add(item);
            cachedTraceHashes = null;
        }

        public string GetFormattedStackTracesForRange(int diffAt)
            => GetFormattedStackTracesForRange(diffAt, Multiplayer.settings.desyncTracesRadius);

        public string GetFormattedStackTracesForRange(int diffAt, int radius)
        {
            long total = DesyncTraceSummary.CaptureCount(desyncStackTraceHashes);
            long first = total - desyncStackTraces.Count;
            var text = new StringBuilder()
                .AppendLine($"Captured stacks: {total}; retained: {desyncStackTraces.Count}; evicted: {first}.")
                .AppendLine($"Eligible events: {traceBudget.EventsSeen}; burst events skipped: {traceBudget.EventsSkipped}.")
                .AppendLine($"Full capture through {DesyncTraceBudget.FullTracesPerTick} events per tick; only larger ticks use power-of-two samples.")
                .AppendLine($"The rolling digest covers all captured stacks. Ring capacity: {DesyncTraceSummary.Capacity}. RNG-state checks remain enabled.");

            if (diffAt == DesyncTraceSummary.EarlierHistoryDiffers)
                text.AppendLine("Earlier captured history differs, but its exact stacks have already been evicted.");
            else if (diffAt >= 0 && diffAt < first)
                text.AppendLine($"Requested capture {diffAt} has been evicted; retained captures begin at {first}.");
            else if (diffAt >= total && diffAt >= 0)
                text.AppendLine($"Capture {diffAt} is missing on this peer.");
            else if (diffAt >= 0)
                text.AppendLine($"First retained mismatch at capture {diffAt}:\n{desyncStackTraces[(int)(diffAt - first)]}");

            if (desyncStackTraces.Count == 0) return text.ToString();
            long center = diffAt >= 0 ? Math.Max(first, Math.Min(total - 1, diffAt)) : total - 1;
            long start = Math.Max(first, center - radius);
            long end = Math.Min(total, center + radius + 1);
            text.AppendLine("\nRetained context (global capture indices; TraceEvent is the original eligible event number):");
            for (long index = start; index < end; index++)
                text.AppendLine($"{index} {desyncStackTraces[(int)(index - first)]}\n");
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
