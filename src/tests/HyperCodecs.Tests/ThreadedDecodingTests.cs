// Copyright (c) 2026 Glenn Watson. All rights reserved.
// Glenn Watson licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace HyperCodecs.Tests;

/// <summary>Checks that a decoder allowed several threads gives the same pictures as one that is not.</summary>
public sealed class ThreadedDecodingTests
{
    /// <summary>The largest number of threads the changing test uses.</summary>
    private const int MostThreads = 4;

    /// <summary>The number of threads of the test that gives an odd number of pictures at once.</summary>
    private const int FewThreads = 3;

    /// <summary>The number of threads of the test that gives more threads than pictures in a short clip.</summary>
    private const int ManyThreads = 8;

    /// <summary>The folder the fixtures are copied to.</summary>
    private const string FixtureFolder = "Fixtures";

    /// <summary>Gets the names of the H.264 and H.265 fixtures that have a hash list.</summary>
    /// <returns>The names, without the extension.</returns>
    public static IEnumerable<string> VideoFixtureNames()
    {
        var folder = Path.Combine(AppContext.BaseDirectory, FixtureFolder);
        foreach (var path in Directory.EnumerateFiles(folder, "h26*.mp4").Order(StringComparer.Ordinal))
        {
            if (File.Exists(Path.ChangeExtension(path, ".sha256")))
            {
                yield return Path.GetFileNameWithoutExtension(path);
            }
        }
    }

    /// <summary>The pictures match the reference sample for sample when the filters share the rows of a picture between threads.</summary>
    /// <param name="name">The fixture name.</param>
    /// <param name="threads">The most threads the decoder may use.</param>
    /// <returns>A task.</returns>
    [Test]
    [Arguments("h264-intra-baseline", 2)]
    [Arguments("h264-deblock-intra", 4)]
    [Arguments("h264-deblock-p", 2)]
    [Arguments("h264-deblock-offsets", 3)]
    [Arguments("h264-p-cabac", 4)]
    [Arguments("h264-p-cabac-weighted", 4)]
    [Arguments("h264-b-cabac-deblock", 2)]
    [Arguments("h264-b-cabac-deblock", 8)]
    [Arguments("h264-b-cavlc-pyramid", 4)]
    [Arguments("h264-high-p-cabac", 3)]
    [Arguments("h265-intra", 2)]
    [Arguments("h265-p", 4)]
    [Arguments("h265-b", 3)]
    [Arguments("h265-b-weighted", 4)]
    [Arguments("h265-loop-filters-ctu32", 4)]
    [Arguments("h265-loop-filters-ctu16-b", 2)]
    [Arguments("h265-deblock-only", 4)]
    [Arguments("h265-sao-only", 4)]
    public Task PicturesMatchTheReference(string name, int threads) => VideoFixtures.AssertRecycledPicturesMatchAsync(name, threads);

    /// <summary>Every fixture decodes to the same pictures when pictures are decoded three at a time.</summary>
    /// <param name="name">The fixture name.</param>
    /// <returns>A task.</returns>
    [Test]
    [MethodDataSource(nameof(VideoFixtureNames))]
    public Task EveryFixtureMatchesWithThreeThreads(string name) => VideoFixtures.AssertRecycledPicturesMatchAsync(name, FewThreads);

    /// <summary>Every fixture decodes to the same pictures when pictures are decoded eight at a time.</summary>
    /// <param name="name">The fixture name.</param>
    /// <returns>A task.</returns>
    [Test]
    [MethodDataSource(nameof(VideoFixtureNames))]
    public Task EveryFixtureMatchesWithEightThreads(string name) => VideoFixtures.AssertRecycledPicturesMatchAsync(name, ManyThreads);

    /// <summary>A decoder can change the number of threads between pictures.</summary>
    /// <returns>A task.</returns>
    /// <exception cref="InvalidDataException">The file did not open.</exception>
    /// <exception cref="NotSupportedException">No decoder handles the track.</exception>
    [Test]
    public async Task TheNumberOfThreadsCanChange()
    {
        using var container = MediaContainers.Open(Path.Combine(AppContext.BaseDirectory, FixtureFolder, "h264-b-cabac-deblock.mp4")) ?? throw new InvalidDataException("The file did not open.");
        using var decoder = VideoDecoders.Create(container.Tracks[container.FindTrack(MediaTrackKind.Video)]) ?? throw new NotSupportedException("No decoder.");
        var frames = new List<VideoFrame>();
        var packet = new MediaPacket();
        var threads = 1;
        var decoded = 0;
        while (container.ReadPacket(packet))
        {
            decoder.MaxThreads = threads;
            threads = (threads % MostThreads) + 1;
            decoder.Decode(packet, frames);
            decoded += frames.Count;
            frames.Clear();
        }

        decoder.Flush(frames);
        await Assert.That(decoded + frames.Count).IsGreaterThan(0);
    }

    /// <summary>A number of threads below one is refused.</summary>
    /// <returns>A task.</returns>
    /// <exception cref="InvalidDataException">The file did not open.</exception>
    /// <exception cref="NotSupportedException">No decoder handles the track.</exception>
    [Test]
    public async Task ZeroThreadsAreRefused()
    {
        using var container = MediaContainers.Open(Path.Combine(AppContext.BaseDirectory, FixtureFolder, "h264-intra-baseline.mp4")) ?? throw new InvalidDataException("The file did not open.");
        using var decoder = VideoDecoders.Create(container.Tracks[container.FindTrack(MediaTrackKind.Video)]) ?? throw new NotSupportedException("No decoder.");

        await Assert.That(() => decoder.MaxThreads = 0).Throws<ArgumentOutOfRangeException>();
    }
}
