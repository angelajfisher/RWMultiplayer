# Native ARM64 macOS tracing

The packaged native library already contains ARM64 and x86-64 slices. Both use
Unity Mono's public mono_stack_walk_no_il API, with method-handle tagging and no
architecture-specific register or private Mono-layout inspection. The managed
gate now admits both architectures and still requires its startup self-test.
Missing libraries and failed managed-frame checks continue to disable tracing.
The Rosetta path remains supported by this same build. Protocol stays at 57.

Validation used the exact installed RimWorld Mono runtime/class libraries in
separate clang-built arm64 and x86_64 hosts, without launching Unity or editing
its binaries. Each process reported its expected architecture. Both found
Program.Capture, Program.Main and the runtime-invoke frame, with the same hash
8847381 for this controlled call chain. Both passed:

- Missing-library fallback and known-managed-caller startup validation.
- Cold/cached capture agreement and full method rendering.
- Actual Harmony-patched original-method normalization and skipFrames.
- Generic callers, a 150-deep call chain returning 128 requested frames.
- 10,000 repeated captures and five million burst events selecting 4,107 captures.
- Enlarged ring disposal, report indices and hash-summary round trip.

The native ARM64 host also passed the 21-check production trace transfer fixture
(Zstandard, payload bounds, fragmentation/reassembly through both relay legs,
40,000-event retained history), and the 110-check quest/dryad fixture (including
saved quest ownership, Scribe values, history culling, insult delivery, actual
marriage handler with controlled signal delivery, ally sharing, and sync struct
return code). These fixtures control engine queries and do not replace a live
colony/network test. Test timings do not predict colony TPS.

Install this complete Multiplayer build on every peer and fully restart.
Existing addon versions remain unchanged. Start RimWorld in native mode and
enable Log desync traces when hosting. Player.log should contain:

    Multiplayer: using Mono managed macOS desync stack tracing (Arm64)

An X64 suffix means the process is running under Rosetta. A tracing-unavailable
warning means the self-test failed, so tracing has been disabled. No legacy
unwinder fallback is used on either macOS architecture.

A live native-native and native-Rosetta/Windows/Linux session is still needed to
validate full simulation determinism and third-party mod behavior. The existing
ARM compatibility warning remains because tracing support does not prove that
arbitrary gameplay/mod code will simulate identically across architectures.
