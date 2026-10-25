// Copyright (c) 2026 Glenn Watson. All rights reserved.
// Glenn Watson licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using BenchmarkDotNet.Attributes;

namespace HyperCodecs.Benchmarks;

/// <summary>Decodes a fixture with the C# decoders, where the cost of decoding is in the work on each picture and not in starting up.</summary>
public class HyperCodecsLargeClipBenchmarks
{
    /// <summary>The fixture that runs.</summary>
    private const string Fallback = "h264-p-cabac.mp4";

    /// <summary>The coded pictures and the decoder.</summary>
    private VideoClip? _clip;

    /// <summary>Gets or sets the file under test.</summary>
    [Params(Fallback)]
    public string Source { get; set; } = Fallback;

    /// <summary>Reads the coded pictures of the file and makes the decoder.</summary>
    [GlobalSetup]
    public void Setup() => _clip = VideoClip.Load(Source);

    /// <summary>Lets go of the decoder.</summary>
    [GlobalCleanup]
    public void Cleanup() => _clip?.Dispose();

    /// <summary>Decodes every picture.</summary>
    /// <returns>The number of pictures.</returns>
    [Benchmark]
    public int DecodeAll() => _clip!.Decode();
}
