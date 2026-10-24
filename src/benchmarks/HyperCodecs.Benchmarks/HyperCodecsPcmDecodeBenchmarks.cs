// Copyright (c) 2026 Glenn Watson. All rights reserved.
// Glenn Watson licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using BenchmarkDotNet.Attributes;
using HyperCodecs;

namespace HyperCodecs.Benchmarks;

/// <summary>Decodes five seconds of uncompressed stereo sound at each sample size.</summary>
public class HyperCodecsPcmDecodeBenchmarks
{
    /// <summary>The packets of the file.</summary>
    private SoundClip? _clip;

    /// <summary>Gets or sets the bits in each sample.</summary>
    [Params(16, 24, 32)]
    public int Bits { get; set; } = 16;

    /// <summary>Makes the file and reads its packets.</summary>
    /// <exception cref="InvalidDataException">The file did not open.</exception>
    [GlobalSetup]
    public void Setup()
    {
        using var container = MediaContainers.Open(new MemoryStream(WaveClip.Build(Bits), false)) ?? throw new InvalidDataException("The WAVE file did not open.");
        _clip = SoundClip.Load(container);
    }

    /// <summary>Lets go of the decoder.</summary>
    [GlobalCleanup]
    public void Cleanup() => _clip?.Dispose();

    /// <summary>Decodes every packet.</summary>
    /// <returns>The samples decoded.</returns>
    [Benchmark]
    public int DecodeAll() => _clip!.Decode();
}
