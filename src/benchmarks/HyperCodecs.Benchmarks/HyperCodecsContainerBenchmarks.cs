// Copyright (c) 2026 Glenn Watson. All rights reserved.
// Glenn Watson licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using BenchmarkDotNet.Attributes;
using HyperCodecs;

namespace HyperCodecs.Benchmarks;

/// <summary>Opens, reads and seeks in each container format: MP3, FLAC, Ogg, MP4, ADTS AAC and WAVE.</summary>
public class HyperCodecsContainerBenchmarks
{
    /// <summary>The name that stands for the WAVE file made in memory.</summary>
    private const string WaveName = "wave";

    /// <summary>The seconds to seek to.</summary>
    private const double SeekTarget = 1.0;

    /// <summary>The container, opened once for the read and seek benchmarks.</summary>
    private MediaContainer? _container;

    /// <summary>The packet the read and seek benchmarks fill.</summary>
    private MediaPacket? _packet;

    /// <summary>The WAVE file, made once.</summary>
    private byte[]? _wave;

    /// <summary>Gets or sets the file under test.</summary>
    [Params("joint-44100-128k.mp3", "sine-16-stereo-44100.flac", "vorbis-stereo-44100.ogg", "aac-lc-stereo-44100.m4a", "aac-lc-stereo-fragmented.mp4", "aac-lc-stereo-44100.aac", WaveName)]
    public string Source { get; set; } = "joint-44100-128k.mp3";

    /// <summary>Opens the file for the read and seek benchmarks.</summary>
    /// <exception cref="InvalidDataException">The file did not open.</exception>
    [GlobalSetup]
    public void Setup()
    {
        _wave = WaveClip.Build();
        _packet = new();
        _container = OpenSource();
    }

    /// <summary>Lets go of the file.</summary>
    [GlobalCleanup]
    public void Cleanup() => _container?.Dispose();

    /// <summary>Opens the file and counts its tracks.</summary>
    /// <returns>The number of tracks.</returns>
    [Benchmark]
    public int Open()
    {
        using var container = OpenSource();
        return container.Tracks.Count;
    }

    /// <summary>Reads every packet from the start.</summary>
    /// <returns>The bytes read.</returns>
    [Benchmark]
    public long ReadAll()
    {
        _ = _container!.Seek(TimeSpan.Zero);
        var bytes = 0L;
        while (_container.ReadPacket(_packet!))
        {
            bytes += _packet!.Length;
        }

        return bytes;
    }

    /// <summary>Seeks into the file.</summary>
    /// <returns>Whether the file moved.</returns>
    [Benchmark]
    public bool SeekInside() => _container!.Seek(TimeSpan.FromSeconds(SeekTarget));

    /// <summary>Opens the file under test.</summary>
    /// <returns>The container.</returns>
    /// <exception cref="InvalidDataException">The file did not open.</exception>
    private MediaContainer OpenSource() => (Source == WaveName
        ? MediaContainers.Open(new MemoryStream(_wave!, false))
        : MediaContainers.Open(Path.Combine(AppContext.BaseDirectory, "Fixtures", Source))) ?? throw new InvalidDataException($"{Source} did not open.");
}
