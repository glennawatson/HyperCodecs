// Copyright (c) 2026 Glenn Watson. All rights reserved.
// Glenn Watson licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using BenchmarkDotNet.Attributes;

namespace HyperCodecs.Benchmarks;

/// <summary>Decodes about a second of Opus sound coded by each layer: CELT, SILK, and both together.</summary>
public class HyperCodecsOpusDecodeBenchmarks
{
    /// <summary>The CELT file, a stereo music clip.</summary>
    private const string CeltFile = "opus-stereo-music.opus";

    /// <summary>The SILK file, a mono wideband speech clip.</summary>
    private const string SilkFile = "silk-mono-wb-20ms.opus";

    /// <summary>The hybrid file, a mono speech clip with the CELT layer on top.</summary>
    private const string HybridFile = "opus-mono-voip.opus";

    /// <summary>The packets of the CELT file.</summary>
    private SoundClip? _celt;

    /// <summary>The packets of the SILK file.</summary>
    private SoundClip? _silk;

    /// <summary>The packets of the hybrid file.</summary>
    private SoundClip? _hybrid;

    /// <summary>Reads the packets of the files.</summary>
    [GlobalSetup]
    public void Setup()
    {
        _celt = SoundClip.Load(CeltFile);
        _silk = SoundClip.Load(SilkFile);
        _hybrid = SoundClip.Load(HybridFile);
    }

    /// <summary>Lets go of the decoders.</summary>
    [GlobalCleanup]
    public void Cleanup()
    {
        _celt?.Dispose();
        _silk?.Dispose();
        _hybrid?.Dispose();
    }

    /// <summary>Decodes the CELT file.</summary>
    /// <returns>The samples decoded.</returns>
    [Benchmark]
    public int DecodeCelt() => _celt!.Decode();

    /// <summary>Decodes the SILK file.</summary>
    /// <returns>The samples decoded.</returns>
    [Benchmark]
    public int DecodeSilk() => _silk!.Decode();

    /// <summary>Decodes the hybrid file.</summary>
    /// <returns>The samples decoded.</returns>
    [Benchmark]
    public int DecodeHybrid() => _hybrid!.Decode();
}
