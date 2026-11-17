# Trace storage and quest lookup optimization

Trace retention uses a bounded reusable record pool, capped at one complete
36,864-event history plus the in-flight record. Disposing a record releases its
interned frame lease, resets tick/event/RNG/thing/faction/text fields and drops
frame/store references before pooling. The capture buffer pool is independent.
History capacity, sampling, stack depth, wire hashes and report detail stay the
same. The pool trades a bounded amount of reusable memory for reduced allocation.

Quest scope setup resolves owner and map in one operation. Cached compiled field
readers avoid FieldInfo.GetValue in repeated map-parent queries. Thread-local
scratch buffers reuse list/set storage and are cleared/released in finally.
No resolved targets persist across operations, so direct writes by other mods,
cleanup, faction transfers and reentrant getters do not require invalidation
hooks. No property getter or simulation work is skipped.

The latest inspected save had 33 uncleaned active/available quests and 532 parts;
QuestEnd, Letter and Pass were the most common types. Benchmark inputs are
synthetic (60 simple/end parts, 20 delay parts, one map target), not a simulation
of that save. The repeatable harness is native/tests/TpsHotPathSmoke. Run using
the game's Mono host, with all mod dependencies and the isolated game fixture
copy as used by the other tests. Compare the installed preceding core/addon
against this build using the same executable and input.

One ARM64 before/after run (three repeated quest measurements):

                                Before          After
200k stored trace events        211 ms, 5 GC    202 ms, 0 GC
20k mixed owner/map lookups      622-628 ms      409-425 ms

The lookup result owner/map was checked each iteration. Individual absolute
timings varied under background workload; an earlier low-load run measured
trace storage at 47 vs 43 ms. Treat ratios as microbenchmark evidence, not an
expected colony TPS increase. GC.GetAllocatedBytesForCurrentThread returned zero
on this Unity Mono build, so byte allocations are unavailable; zero is not a
zero-allocation claim. The pooled live objects also change GC heap behavior;
the quest benchmark's collection count is not comparable as an allocation total.

Validation on both ARM64 and x86-64/Rosetta: 118 quest/addon checks, 24 actual
trace-retention/fragmented-relay checks. Also 79 targeted .NET tests for summary,
sampling, frame lifecycle, compression and packets. Regressions include reuse
with fresh metadata, cleared references, bounded pooling, field retargeting,
private inherited fields, nested queries and cleanup after exceptions.

Diagnostics remain disabled. No polling/profiling hooks were added to live pawn,
job, pathfinding or hauling ticks. Full Unity live-save profiling and a controlled
in-game TPS comparison still require a running session; headless fixture timings
cannot identify the dominant gameplay mod or measure full-colony performance.
