# macOS desync tracing

This ports the native `backtrace` approach from rwmt/Multiplayer commit
`acb33a21f7ad9f35a8b9ba4ada069d0854c427b4`. The original experimental implementation
is replaced with a bounded 32-bit-count C ABI and no per-capture managed allocation.

macOS x86-64 processes, including RimWorld running under Rosetta, load
`Libraries/macOS/libmultiplayer_stacktrace.dylib` from the active mod directory.
Startup checks that the unwinder can see a known Mono managed method before enabling
tracing. Missing libraries or a failed check disable tracing, without falling back
to the private Mono LMF offsets. Native ARM64 tracing remains disabled: testing with
RimWorld's ARM64 Mono runtime did not recover managed frames. The universal library
contains both architectures to allow further investigation.

The C# integration discards native and capture helper frames, then applies the
existing method-name normalization, Harmony original-method lookup and hashing to
managed callers. Cross-platform trace-hash parity still requires an in-game test.
Frame classification is cached by return address to avoid per-frame Mono calls on
every random-number call. Normal gameplay starts with a 64-frame native capture,
retrying up to 256 only if native/helper frames leave too few managed frames for
the existing 32-frame report. This preserves trace events rather than sampling
them, but native unwinding still has a runtime cost: leave tracing off for normal
play if it substantially reduces TPS, and enable it to investigate a reproducible
desync. Rosetta TPS must be measured in-game; native smoke tests do not establish
gameplay performance.

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
The macOS build script forces unwind metadata even for release builds, sets a
portable install name and ad-hoc signs the universal binary.

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

On Apple Silicon this command requires Rosetta. The test must find `Program:Capture`
in the captured managed frames. Compile the host with `-arch arm64` instead to test
the unsupported-architecture fallback.

Finally, run RimWorld through Rosetta, enable **Log desync traces** when hosting,
and confirm startup logs `using native macOS desync stack tracing`. Host/join with
the same patched mod, run a colony for several minutes and reconnect. Check that
there are no `TraceImpl` exceptions and that desync logs contain managed callers.
Test against a Windows/Linux player too, since trace hashes participate in desync
detection. Replacing/renaming the dylib should instead log that tracing is disabled
and leave the session playable.
