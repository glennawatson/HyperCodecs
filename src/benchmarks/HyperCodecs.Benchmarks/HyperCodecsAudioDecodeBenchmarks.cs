// Copyright (c) 2026 Glenn Watson. All rights reserved.
// Glenn Watson licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using BenchmarkDotNet.Attributes;

namespace HyperCodecs.Benchmarks;

/// <summary>Decodes short clips of MP3, FLAC, Vorbis and the AAC variants the AAC benchmarks do not cover, with a decoder made once.</summary>
public class HyperCodecsAudioDecodeBenchmarks
{
    /// <summary>The packets of the file.</summary>
    private SoundClip? _clip;

    /// <summary>Gets or sets the file under test.</summary>
    [Params(
        "joint-44100-128k.mp3",
        "mono-22050-64k.mp3",
        "vbr-44100.mp3",
        "sine-16-stereo-44100.flac",
        "sine-24-stereo-48000.flac",
        "six-16-32000.flac",
        "noise-16-mono-22050.flac",
        "vorbis-stereo-44100.ogg",
        "vorbis-mono-22050.ogg",
        "aac-lc-mono-22050.m4a",
        "aac-lc-tools-44100.m4a")]
    public string Source { get; set; } = "joint-44100-128k.mp3";

    /// <summary>Reads the packets of the file.</summary>
    [GlobalSetup]
    public void Setup() => _clip = SoundClip.Load(Source);

    /// <summary>Lets go of the decoder.</summary>
    [GlobalCleanup]
    public void Cleanup() => _clip?.Dispose();

    /// <summary>Decodes every packet.</summary>
    /// <returns>The samples decoded.</returns>
    [Benchmark]
    public int DecodeAll() => _clip!.Decode();
}
