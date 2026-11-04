# macOS desync tracing

This branch initially ported the `backtrace` approach from rwmt/Multiplayer commit
`acb33a21f7ad9f35a8b9ba4ada069d0854c427b4`. Real-runtime testing reproduced its
failure to reach Mono JIT frames under Rosetta. Capture now uses Unity Mono's
public `mono_stack_walk_no_il` API through a small native Rust callback bridge.
The callback writes method handles directly into a bounded reusable buffer;
it never calls managed code during the native stack walk and has no dependencies.

macOS x86-64 processes, including RimWorld running under Rosetta, load
`Libraries/macOS/libmultiplayer_stacktrace.dylib` from the active mod directory.
Startup checks that the walker can see a known Mono managed method before enabling
tracing. Missing libraries or a failed check disable tracing, without falling back
to the private Mono LMF offsets. Native ARM64 tracing remains disabled pending
validation of the replacement on that architecture; the universal library contains
both architectures to allow further investigation.

The C# integration discards native and capture helper frames, then applies the
existing method-name normalization, Harmony original-method lookup and hashing to
managed callers. Captured MonoMethod handles are complemented to negative values
so they are distinct from legacy code addresses. `Native` recognizes those tagged
handles for hashing and report rendering. Cross-platform trace-hash parity still
requires an in-game test.
Frame classification and normalized hashes are cached by method identity to avoid
per-frame Mono metadata calls on every random-number call. Normal gameplay starts
with a 64-frame capture, retrying up to 256 only if helper frames leave too few managed frames for
the existing 32-frame report. This preserves the requested depth of a captured
stack; the event-selection policy below separately controls which stacks are captured.
Stack walking still has a runtime cost: leave tracing off for normal
play if it substantially reduces TPS, and enable it to investigate a reproducible
desync. Rosetta TPS must be measured in-game; native smoke tests do not establish
gameplay performance. Capture exceptions disable tracing and return the pooled log
item rather than escaping into simulation logic after the RNG has advanced.

## Full tracing with burst protection and a ring buffer

All platforms fully capture the first 4096 eligible trace events in each simulation
tick. Only larger ticks enter burst sampling, at zero-based ordinals 4096, 8192,
16384, ... . The next tick starts in full-capture mode again. Selection is based
only on tick and event ordinal, never elapsed time or machine speed. A five-million
event tick selects 4107 stack captures. There is no interval-wide capture cutoff,
so ordinary ticks continue to be fully captured even after earlier huge bursts.
The bound on per-tick capture work is 4160 stacks.

Each sync opinion retains only its latest 2048 full stacks in a fixed-size ring.
Overwritten records immediately return to the pool, and clearing an opinion releases
each remaining record once. The existing 30-opinion backlog remains bounded.

A 64-bit rolling digest and capture count summarize the entire captured sequence,
including records already evicted. Up to 2048 recent hashes follow that five-integer
header: the wire summary is at most 2053 integers (about 8 KiB), regardless of how
many ordinary events were fully captured. The digest still detects older
disagreement even when its exact stack is gone. Retained mismatches use global
capture indices, and peers align their ring tails by capture count.

Reports identify sampling, skipped events, evicted history and retained index
ranges. `TraceEvent` identifies the original eligible event within the interval.
Reports explicitly say when an exact older stack is unavailable; the digest cannot
reconstruct it. Random-state collection and comparisons remain unchanged. Full
tracing below the burst threshold costs more CPU than the previous aggressive
sampler, and actual colony TPS must still be measured.

Protocol 56 rejects older peers because the hybrid policy and rolling wire summary
differ from protocol 55's sampled prefix. Every player needs this build, including
Windows/Linux players. The colony save format is unchanged. Historical reports
with bare trace-hash lists remain readable.

## Build

Install Rust (stable) and Xcode command-line tools on macOS, then run:

```sh
cargo test --locked --manifest-path native/Cargo.toml
bash native/build-macos.sh
dotnet build Source/Client/Multiplayer.csproj --configuration Release
```

Ship `Libraries` alongside `Assemblies`, `AssembliesCustom`, `About`, `Defs`,
`Languages` and `Textures`. For Workshop's versioned layout, `Libraries` stays
at the mod root alongside `About`. Beta and Workshop workflows build and include the native library.
The macOS build script sets a portable install name and ad-hoc signs the universal
binary. The versioned `mp_capture_mono_stack_v1` symbol rejects an old, incompatible
native library with an explicit startup error instead of calling the wrong ABI.

## Verify

```sh
clang native/tests/native-smoke.c -o /tmp/native-smoke
/tmp/native-smoke "$PWD/Libraries/macOS/libmultiplayer_stacktrace.dylib"
```

To test against an installed RimWorld's exact Mono runtime without launching Unity:

```sh
dotnet build native/tests/MonoSmoke/MonoSmoke.csproj --configuration Release
clang -arch x86_64 native/tests/mono-host.c -o /tmp/mono-host
rimworld_contents="$HOME/Library/Application Support/Steam/steamapps/common/RimWorld/RimWorldMac.app/Contents"
DYLD_LIBRARY_PATH="$rimworld_contents/Frameworks" /tmp/mono-host \
  "$rimworld_contents/Frameworks/libmonobdwgc-2.0.dylib" \
  "$rimworld_contents/Resources/Data/Managed" \
  "$PWD/native/tests/MonoSmoke/bin/Release/net48/MonoSmoke.exe" \
  "$PWD/Libraries/macOS/libmultiplayer_stacktrace.dylib"
```

On Apple Silicon this command requires Rosetta. The test must find `Program.Capture`
in the captured managed frames, render those frames, and pass cold/cached hash
agreement, Harmony original-method normalization, skipped helper frames, generic
callers, deep stacks and 10,000 repeated captures. Compile the host with `-arch arm64`
instead to test the unsupported-architecture fallback.

Finally, run RimWorld through Rosetta, enable **Log desync traces** when hosting,
and confirm startup logs `using Mono managed macOS desync stack tracing`. Host/join with
the same patched mod, run a colony for several minutes and reconnect. Check that
there are no `TraceImpl` exceptions and that desync logs contain managed callers.
Test against a Windows/Linux player too, since trace hashes participate in desync
detection. Replacing/renaming the dylib should instead log that tracing is disabled
and leave the session playable.
