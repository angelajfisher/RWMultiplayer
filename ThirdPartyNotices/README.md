# Compression dependencies

ZstdSharp.Port 0.8.8: https://github.com/oleg-st/ZstdSharp
Managed port of Zstandard 1.5.7. Distributed under MIT; upstream Zstandard BSD
notice is also included. Runtime dependencies System.Memory, System.Buffers,
System.Runtime.CompilerServices.Unsafe, System.Threading.Tasks.Extensions and
System.Numerics.Vectors are distributed under the .NET MIT notice included here.
NuGet chooses compatible .NET Framework builds; no native Zstd library is shipped.
