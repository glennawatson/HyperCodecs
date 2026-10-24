// Copyright (c) 2026 Glenn Watson. All rights reserved.
// Glenn Watson licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using BenchmarkDotNet.Attributes;

namespace HyperCodecs.Benchmarks;

/// <summary>
/// Decodes large pictures with the C# decoders, where the cost of decoding is in the work on each picture and not in starting up. The files
/// are not in the repository: set HYPERCODECS_BIG_CLIPS to a folder of H.264 or H.265 files of 720p or larger. Without it, a small fixture runs.
/// </summary>
public class HyperCodecsLargeClipBenchmarks
{
    /// <summary>The name of the environment variable that names a folder of larger files.</summary>
    private const string BigClipsVariable = "HYPERCODECS_BIG_CLIPS";

    /// <summary>The prefix of a source that lives in the folder of larger files.</summary>
    private const string BigPrefix = "big:";

    /// <summary>The fixture that runs when no folder is named.</summary>
    private const string Fallback = "h264-p-cabac.mp4";

    /// <summary>The coded pictures and the decoder.</summary>
    private VideoClip? _clip;

    /// <summary>Gets or sets the file under test.</summary>
    [ParamsSource(nameof(Sources))]
    public string Source { get; set; } = Fallback;

    /// <summary>Gets the files to measure: those in the folder named by the environment variable, or a small fixture.</summary>
    /// <returns>The sources.</returns>
    public static IEnumerable<string> Sources()
    {
        var folder = Environment.GetEnvironmentVariable(BigClipsVariable);
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
        {
            yield return Fallback;
            yield break;
        }

        foreach (var path in Directory.EnumerateFiles(folder, "*.mp4").Order(StringComparer.Ordinal))
        {
            yield return BigPrefix + Path.GetFileName(path);
        }
    }

    /// <summary>Reads the coded pictures of the file and makes the decoder.</summary>
    [GlobalSetup]
    public void Setup() => _clip = Source.StartsWith(BigPrefix, StringComparison.Ordinal)
        ? VideoClip.LoadFile(Path.Combine(Environment.GetEnvironmentVariable(BigClipsVariable)!, Source[BigPrefix.Length..]))
        : VideoClip.Load(Source);

    /// <summary>Lets go of the decoder.</summary>
    [GlobalCleanup]
    public void Cleanup() => _clip?.Dispose();

    /// <summary>Decodes every picture.</summary>
    /// <returns>The number of pictures.</returns>
    [Benchmark]
    public int DecodeAll() => _clip!.Decode();
}
