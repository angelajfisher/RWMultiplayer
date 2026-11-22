# macOS desync tracing

This branch initially ported the `backtrace` approach from rwmt/Multiplayer commit
`acb33a21f7ad9f35a8b9ba4ada069d0854c427b4`. Real-runtime testing reproduced its
failure to reach Mono JIT frames under Rosetta. Capture now uses Unity Mono's
public `mono_stack_walk_no_il` API through a small native Rust callback bridge.
The callback writes method handles directly into a bounded reusable buffer;
it never calls managed code during the native stack walk and has no dependencies.

macOS ARM64 and x86-64 processes, including RimWorld running under Rosetta, load
`Libraries/macOS/libmultiplayer_stacktrace.dylib` from the active mod directory.
Startup checks that the walker can see a known Mono managed method before enabling
tracing. Missing libraries or a failed check disable tracing, without falling back
to the private Mono LMF offsets. Native ARM64 uses the same validated Mono walker
and universal library as x86-64; a known-managed-caller check gates both paths.
See ARM64.md for validation results and remaining live-session checks.

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

Each sync opinion retains its first 4,096 and latest 32,768 captured events,
sharing repeated frame arrays and reporting stack definitions once. Eviction
releases definitions when their last event is gone. The 30-opinion backlog is
bounded. See TraceRetention.md for memory, report-size and retention details.

Routine hash summaries keep the first 256 and latest 2,048 hashes plus a rolling
digest/count covering every capture (six-integer header). A difference outside
that smaller wire window may still be present in the expanded local reports.
Normal hash traffic and RNG-state checks are unchanged by ARM support. The rebased custom build uses protocol 58
to separate its trace hashes and report packets from upstream protocol 57.
All players should use the same build. Stack hashes matched for the controlled
ARM64/x86-64 capture test; in-game architecture/platform parity still needs testing.

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

The x86-64 host requires Rosetta on Apple Silicon. The test must find `Program.Capture`
in the captured managed frames, render those frames, and pass cold/cached hash
agreement, Harmony original-method normalization, skipped helper frames, generic
callers, deep stacks and 10,000 repeated captures. Compile the host with `-arch arm64`
instead to run the same supported tracing tests natively.

Finally, run RimWorld natively or through Rosetta, enable **Log desync traces** when hosting,
and confirm startup logs `using Mono managed macOS desync stack tracing`. Host/join with
the same patched mod, run a colony for several minutes and reconnect. Check that
there are no `TraceImpl` exceptions and that desync logs contain managed callers.
Test against a Windows/Linux player too, since trace hashes participate in desync
detection. Replacing/renaming the dylib should instead log that tracing is disabled
and leave the session playable.
