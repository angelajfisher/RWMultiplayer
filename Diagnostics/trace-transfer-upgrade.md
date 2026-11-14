# Expanded desync reports and Zstandard transfer

Protocol 57 retains the first 256 and latest 2048 captured stacks per comparison
interval, with a rolling digest of every capture. An explicit gap identifies
stacks lost between these ranges. Both host and local reports export all retained
stacks by default, rather than the previous +/-40 window. Routine hash packets
remain bounded below 10 KiB in the regression scenario. Stack-capture frequency
and burst sampling are unchanged; this adds bounded retention, not extra unwinds.

Trace and method-list payloads use ZstdSharp.Port 0.8.8, a managed Zstandard port,
level 9 with frame checksums. Compression occurs only when preparing a report.
A versioned envelope supports gzip fallback and validates expanded length. Legacy
gzip payloads also decode. Each compressed field is capped at 8 MiB; decoded text
is capped at 32 MiB, with a 32 MiB Zstd window limit. Report export stops visibly
if its text cap is reached. Both network transfer legs explicitly call
SendFragmented; combined fields stay below the existing 32 MiB fragment limit.

Correction to earlier investigation: PacketBuffer.BindBytes ignores maxLength.
The nominal 32767-byte parameter was not an enforced cap on these fields. New
trace-specific binding checks enforce lengths before receive allocation without
changing unrelated byte-array packet behavior.

## Measurements on actual game Mono (macOS x64/Rosetta)

Input                      Text bytes   gzip (.NET)   Zstd level 9
419 host traces              205917        7191          4427
419 local traces             203858        6228          4013
419 host JIT methods          269530       32287         29074
419 local JIT methods         268373       35197         31738

These benchmark sizes exclude the new 9-byte envelope and Zstd checksum (4 bytes).
The final host-trace payload is 4440 bytes. The first Zstd compression included
148 ms of startup/JIT; warm trace compression measured 4 ms and method lists
14-15 ms in this small run. These are illustrative measurements, not guarantees.

## Validation

105 .NET regressions pass. The production server relay is exercised with real
fragmentation/reassembly and reports larger than 64 KiB, including its host-only
check. Corruption, truncation, unknown codecs, declared/expanded size violations,
gzip compatibility, Unicode, bounded retention/release, prefix/tail index
alignment and legacy trace-hash formats are covered.

The TraceTransferSmoke net48 harness passes 17 checks on RimWorld's x64 Mono.
It invokes the actual ClientPlayingState host request handler, server relay and
fragment receive code, verifies the full report survives both legs, checks startup field/type loading of all six newly bundled compression
assemblies, checks export ignores a configured radius of 1, and round-trips the supplied actual reports.
This isolated test does not replace a live network playtest.

Build the client first, then native/tests/TraceTransferSmoke. Run its EXE with
native/tests/mono-host.c, the game runtime/managed paths and a directory containing
419-*traces.txt fixtures. No game assembly is modified.

All peers must update together. Normal save XML is unchanged; diagnostics protocol
and codec framing have changed. The new reader understands historical bare hashes
and protocol-56 TRC2 summaries when reading old report data. This does not recover
stacks already discarded, trace uninstrumented state mutations, or extend the
30-opinion request backlog. Standard saved report ZIPs still contain readable text;
Zstandard applies to network transport, not ZIP container compatibility.
