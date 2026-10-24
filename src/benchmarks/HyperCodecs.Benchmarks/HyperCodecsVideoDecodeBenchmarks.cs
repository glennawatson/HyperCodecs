// Copyright (c) 2026 Glenn Watson. All rights reserved.
// Glenn Watson licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using BenchmarkDotNet.Attributes;

namespace HyperCodecs.Benchmarks;

/// <summary>
/// Decodes H.264 and H.265 files that exercise each coding tool: intra, predicted and bi-predicted pictures, CAVLC and CABAC,
/// weighted prediction and the loop filters. The decoder is made once and reused.
/// </summary>
public class HyperCodecsVideoDecodeBenchmarks
{
    /// <summary>The packets of the file.</summary>
    private VideoClip? _clip;

    /// <summary>Gets or sets the file under test.</summary>
    [Params(
        "h264-intra-baseline.mp4",
        "h264-intra-cabac.mp4",
        "h264-p-cavlc-partitions.mp4",
        "h264-p-cabac.mp4",
        "h264-p-cabac-weighted.mp4",
        "h264-high-p-cabac.mp4",
        "h264-b-cavlc-temporal.mp4",
        "h264-b-cabac-deblock.mp4",
        "h265-intra.mp4",
        "h265-p.mp4",
        "h265-b.mp4",
        "h265-b-weighted.mp4",
        "h265-loop-filters-ctu32.mp4",
        "h265-intra-wavefront.mp4")]
    public string Source { get; set; } = "h264-p-cabac.mp4";

    /// <summary>Reads the coded pictures of the file.</summary>
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
