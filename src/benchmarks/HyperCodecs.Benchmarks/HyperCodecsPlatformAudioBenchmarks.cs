// Copyright (c) 2026 Glenn Watson. All rights reserved.
// Glenn Watson licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using BenchmarkDotNet.Attributes;
using HyperCodecs.Platform.Audio;

namespace HyperCodecs.Benchmarks;

/// <summary>
/// Measures the calls a player makes to the system sound output: reading the latency and writing a chunk that fits in the free
/// buffer. They measure nothing on a machine with no sound server or device, where every call returns at once and <c>WriteChunk</c> returns <see langword="false"/>.
/// </summary>
public class HyperCodecsPlatformAudioBenchmarks
{
    /// <summary>The samples a second.</summary>
    private const int SampleRate = 48_000;

    /// <summary>The channels.</summary>
    private const int Channels = 2;

    /// <summary>The samples a channel in a chunk: 10 ms, which fits in the output's buffer.</summary>
    private const int ChunkFrames = 480;

    /// <summary>The sound written, interleaved.</summary>
    private readonly float[] _chunk = new float[ChunkFrames * Channels];

    /// <summary>The sound output, or <see langword="null"/> when the system has none.</summary>
    private IStreamingAudioOutput? _output;

    /// <summary>Whether the output started.</summary>
    private bool _started;

    /// <summary>Opens the output.</summary>
    [GlobalSetup]
    public void Setup()
    {
        _output = OperatingSystem.IsLinux() ? LinuxAudioOutputs.Create() : PlatformAudioOutputs.Create();
        _started = _output?.Start(SampleRate, Channels) ?? false;
    }

    /// <summary>Closes the output.</summary>
    [GlobalCleanup]
    public void Cleanup() => _output?.Dispose();

    /// <summary>Reads the latency, as a player does every video frame.</summary>
    /// <returns>The latency in ticks.</returns>
    [Benchmark]
    public long ReadLatency() => _output?.Latency.Ticks ?? 0;

    /// <summary>Empties the output, then writes a chunk that fits without waiting.</summary>
    /// <returns>Whether the chunk was written.</returns>
    [Benchmark]
    public bool WriteChunk()
    {
        if (!_started)
        {
            return false;
        }

        _output!.Flush();
        return _output.Write(_chunk, CancellationToken.None);
    }
}
