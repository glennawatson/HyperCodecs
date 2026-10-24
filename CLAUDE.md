# Project rules

- **Frozen collections are banned throughout this project.** Use `Dictionary` or `HashSet`, never `System.Collections.Frozen`, `FrozenDictionary`, `FrozenSet`, `ToFrozenDictionary`, or `ToFrozenSet`. The reusable library may run in a short-lived host. The global long-running-application exception does not apply here.

HyperCodecs is a NuGet package of sound and video containers and decoders written in managed C# for Linux, Windows and macOS. It is the codec layer that [HyperPDF](https://github.com/glennawatson/HyperPDF) plays media with. There are three packages: `src/HyperCodecs` (managed decoders and containers), `src/HyperCodecs.Platform` (Windows, Apple and Android sound output and hardware video decoding) and `src/HyperCodecs.Platform.Linux` (PulseAudio and VA-API).

## Standards

Write and check each decoder against its specification. The specifications are in the shared standards folder `docs/PdViewerLite/media` of the `glennawatson` checkout (`../glennawatson/docs/PdViewerLite/media` from this repository). [docs/standards](docs/standards/README.md) maps each codec and container to its file. Do not copy the PDFs into this repository. Where a specification is not freely published (AAC, MP3, MP4), check the result against files from an independent encoder and keep the MD5 or SHA-256 of the expected output beside the fixture.

## Performance

- Low allocations and low execution time are core requirements. Back every code change with relevant BenchmarkDotNet measurements. Compare baseline and change under the same conditions. Prefer the lowest measured cost that preserves correctness.
- Do not judge performance from source code or subjective impressions. Overlapping timing error bars are inconclusive. Every decoder and container needs a benchmark.
- Measure allocations with EventPipe and `src/tools/AllocationAudit.cs`. Never use `MemoryDiagnoser`. Remove every measured allocation. Explain one in `src/benchmarks/allocations-explained.json` only after removing it has been exhausted or removing it shows a dramatic time penalty. Decoding a packet must not allocate once the buffers have grown to the stream's size.
- Look for fast paths that avoid the computation in the first place (early exits, caches, skipping unchanged work) before optimising the computation itself. Then consider SIMD.
- Run benchmarks and allocation audits as one set. Pass every filter to one invocation (`dotnet run --file src/tools/AuditAllocations.cs -- '*A*' '*B*'`, or BenchmarkDotNet's `--filter '*A*' '*B*'`) so the build and the run happen once; never loop over families with a separate `dotnet run` each.

## Implementation

- Put behaviour in focused static classes with helper methods that operate on records holding the state. Never grow one type and split it across partial files; `partial` is only for source generators. Callers compose the focused helpers directly; change the downstream code instead of adding a facade.
- **Static fields are allowed if, and only if, their state never changes after initialisation.** Keep fixed rule data and immutable lookup tables static rather than rebuilding them for every operation. A `readonly` reference does not make a mutable cache, pool or counter immutable. Mutable reusable state belongs to the caller's lifetime owner and is passed explicitly to helpers.
- Use sealed positional records for data snapshots and positional readonly record structs for small values. Use classes for genuinely mutable state, services and owned lifetimes, with encapsulated state and proper methods. Raw fields require a concrete need, such as native layout. Record collection equality follows the collection's own equality.
- Pass only the state each focused helper needs. Keep reusable buffers and mutable working state in the caller's owner; do not hide them in global storage or ambient state.
- Never use value tuple types: not as return types, fields, parameters, collection elements, or `(a, b) = (b, a)` swaps. Name the shape with a positional record (`record struct` for small values, `record` for reference data). On hot paths, use readonly record structs that stay on the stack or in pooled arrays, so they allocate nothing.
- Keep deconstruction. Positional records deconstruct, so write `var (key, value) = entry;` wherever it reads better than member access.
- Every project targets `net10.0;net11.0`: the library, tests and benchmarks. The Roslyn generator for the benchmarks targets `netstandard2.0` because it loads into the compiler. Use `global.json`. Package versions belong in `Directory.Packages.props`.
- Shipped code must be trim and Native AOT safe. Avoid reflection and activation APIs that require unreferenced code.
- Write byte-first and span-based code on hot paths: pooled buffers, SIMD where measured to help, and early exits. Readers of untrusted files check every length and index against the data they hold and throw `InvalidDataException` for a stream that is not valid. Shared caches are thread-safe; publish them with `Volatile` or `Interlocked`, or guard them with a `Lock`.
- Use the newest .NET 11 APIs where they help. Keep every `#if NET11_0_OR_GREATER` branch in a small static helper in the project's `Compat/` folder, with the .NET 10 fallback behind the same signature. Feature code calls the helper.
- C# 15 is on: unions, `[with(...)]` collection arguments, `field`, `allows ref struct`. Unions compile on `net10.0` through a `UnionAttribute`/`IUnion` polyfill in `Compat/`. Use them only for reference-type cases, because value cases box.
- The package builds without runtime async (see `src/HyperCodecs/HyperCodecs.csproj`) so Mono hosts can load it; tests and benchmarks build with it.
- Analyzer habits:
  - Only 0, 1 and -1 inline; name other numbers as constants. Hex data tables are fine.
  - Keep methods small: complexity 10, cognitive complexity 15. Use table-driven dispatch.
  - No `x++` inside an expression.
  - Members of internal types are internal.
  - Use overloads, not optional parameters.
  - Braced switch sections.
  - Run `dotnet format analyzers --diagnostics <ids>` for mechanical fixes.
- `HyperCodecs` is managed C# only. Native calls (P/Invoke) belong only in `HyperCodecs.Platform`, behind `OperatingSystem` checks, and load their system library at run time so a missing library or device returns a reason instead of throwing. Ask the owner before adding any other native library.
- Give every `LibraryImport` a `DefaultDllImportSearchPaths` attribute.
- Tests, committed scripts and docs never contain paths or assumptions specific to one machine (home folders, installed tools or profiles, local clones). Optional inputs come from an environment variable or PATH and skip with a reason when absent.
- Follow the analyzers and existing C# style. Document members, name constants and use explicit enum values. Fix warnings; do not add suppressions.
- Use .NET tools only. Use Roslyn for structural C# edits. Run single-file tools with `dotnet run --file`. Keep docs short. Add no attribution trailers or generated-by footers.

## Layout

- `src/HyperCodecs` is the package. One folder per format: `Aac`, `Flac`, `H264`, `H265`, `Mp3`, `Mp4`, `Matroska`, `Ogg`, `Opus`, `Vorbis`, plus `Audio` (mixing and resampling), `Video` (colour conversion and scaling), `Bits`, `Checksums`, `Containers` and `Dsp`. `MediaContainers`, `AudioDecoders` and `VideoDecoders` are the entry points.
- `src/HyperCodecs.Platform` is the second package, built the way ReactiveUI builds its platform libraries: one project, one target framework per system, and each build compiles only its own system's code. `Platforms/common` holds the public surface (`IStreamingAudioOutput`, `PlatformAudioOutputs`, `PlatformVideoDecoders`) and the shared helpers. `Platforms/windows` (WASAPI, Media Foundation), `Platforms/apple` (Audio Toolbox, VideoToolbox; iOS, tvOS, macOS, Mac Catalyst), `Platforms/android` (AudioTrack, MediaCodec) and `Platforms/none` (the plain `net10.0` and `net11.0` builds, which have no system code) each define `SystemAudioOutputs` and `SystemVideoDecoders`, which the common code calls. Linux has no platform target framework, so its code is the separate `src/HyperCodecs.Platform.Linux` project (public `LinuxAudioOutputs` and `LinuxVideoDecoders`), which references `HyperCodecs.Platform`. Target frameworks come from `Directory.Build.props`; the csproj includes folders by target platform. Never compile one system's code into another system's build, and never make a system's types public: package validation compares the public API of every target framework.
- Tests and benchmarks run against the Windows build on Windows and the Linux project on Linux. The Apple and Android builds are compile-checked only.
- `src/tests/HyperCodecs.Tests` holds the tests. `Fixtures/` holds short files from independent encoders, each with the checksum of the expected output.
- `src/benchmarks/HyperCodecs.Benchmarks` holds the BenchmarkDotNet benchmarks and the allocation harness. `HyperCodecs.Benchmarks.Generator` lists the benchmarks for the harness so no reflection is needed.
- `src/tools` holds single-file C# tools; see its README.

## Verification

Keep `README.md` for users of the package; build, test and contributor notes belong here.

Always pass `-v q --nologo` (and `--no-restore` after the first build) to `dotnet build` and `dotnet test` to avoid console log bloat. Filter tests to the single class or method being worked on (`--treenode-filter`) until final verification. Never read the binary files under `src/tests/HyperCodecs.Tests/Fixtures` into context.

```console
dotnet build src/HyperCodecs.slnx -v q --nologo
dotnet test --solution src/HyperCodecs.slnx -v q --nologo
# one test project or filtered test; runner flags go after --; never --no-build
dotnet test --project "$PWD/src/tests/HyperCodecs.Tests/HyperCodecs.Tests.csproj" -f net11.0 -v q --nologo --no-restore -- --treenode-filter '/*/*/ClassName/*'
dotnet run -c Release -f net11.0 --project src/benchmarks/HyperCodecs.Benchmarks -- --filter '*RelevantBenchmark*'
dotnet run --file src/tools/AuditAllocations.cs -- '*RelevantBenchmark*'
dotnet pack src/HyperCodecs/HyperCodecs.csproj -c Release -o artifacts/nuget
```

Warnings are failures. Verify both target frameworks. TUnit uses Microsoft.Testing.Platform; runner arguments follow `--`. With SDK 11, use absolute paths for `--project`.

Continue authorised goal work through verification. Treat feedback as work to fix. Report concrete blockers and missing dependencies. Follow the user's current commit and publishing instructions.
