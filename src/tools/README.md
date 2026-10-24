# Repository tools

Run tools from the checkout with `dotnet run --file src/tools/<name>.cs -- <arguments>`.
Each entry point targets .NET 11. `ScriptProcess.cs` and `SourceLines.cs` are shared source, not entry points.

| Task | Tool |
| --- | --- |
| Measure and explain allocations for all supplied benchmark filters in one process | `AuditAllocations.cs '*Benchmark*' '*Another*'` (`--root <checkout>` audits another worktree) |
| Compare a baseline ref with the current code using the same benchmark definitions | `CompareBenchmarks.cs <ref> '*Benchmark*' [more filters] [--leeway percent]` |
| Read existing allocation or CPU traces | `AllocationAudit.cs`, `CpuAudit.cs` |
| Name the source line behind each allocation or CPU sample | Set `HYPERCODECS_SYMBOLS` to the folder of the built benchmark assemblies, which embed their PDB files, before `AuditAllocations.cs` or `AllocationAudit.cs` to see `File.cs:line` after each site. For CPU, run the benchmark project with `--cpu-lines --artifacts <folder>` (it traces samples from start-up with the JIT line map instead of BenchmarkDotNet's profiler), then `CpuAudit.cs <folder>` lists the busiest lines. The sampler stops threads at GC safepoints, so a line is where the thread was stopped and can sit a little after the code that cost the time; a profile with `Thread.PollGCWorker` high in it needs reading with that in mind. |
| Run a test project or solution with a time limit | `RunBoundedTests.cs <project or solution> <framework> <results> <test timeout> <process timeout>` |

Allocation and comparison evidence stays under `~/.cache/hypercodecs`. Set `BENCHMARK_FRAMEWORK=net10.0` to measure the other supported runtime. The default is `net11.0`.
