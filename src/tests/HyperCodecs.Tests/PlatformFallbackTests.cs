// Copyright (c) 2026 Glenn Watson. All rights reserved.
// Glenn Watson licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using HyperCodecs.Platform.Audio;
using HyperCodecs.Platform.Video;

namespace HyperCodecs.Tests;

/// <summary>
/// Checks what the platform packages do on a machine that may have no sound server or video hardware: they report it and the
/// managed decoders stay as the fallback. These checks hold on every machine, with or without a device.
/// </summary>
public sealed class PlatformFallbackTests
{
    /// <summary>A small H.264 fixture, below the size where the hardware is tried first.</summary>
    private const string SmallFile = "h264-p-cabac.mp4";

    /// <summary>The sample rate asked of the sound output.</summary>
    private const int SampleRate = 48_000;

    /// <summary>The channels asked of the sound output.</summary>
    private const int Channels = 2;

    /// <summary>The samples in the chunk written to the sound output.</summary>
    private const int ChunkSamples = 960;

    /// <summary>The index of the track in the track made for the unreadable-track check.</summary>
    private const int FirstTrack = 0;

    /// <summary>The decoder the platform package gives decodes the same pictures as the managed decoder.</summary>
    /// <returns>A task.</returns>
    [Test]
    public async Task PlatformVideoDecoders_GiveTheManagedPictures()
    {
        var managed = DecodeAll(VideoDecoders.Create);
        var platform = DecodeAll(static track => OperatingSystem.IsLinux() ? LinuxVideoDecoders.Create(track) : PlatformVideoDecoders.Create(track));

        await Assert.That(platform.Count).IsGreaterThan(0);
        await Assert.That(platform.Count).IsEqualTo(managed.Count);
        for (var i = 0; i < managed.Count; i++)
        {
            await Assert.That(platform[i]).IsEquivalentTo(managed[i]);
        }
    }

    /// <summary>A track that is not H.264 or H.265 gets a reason and no decoder from the system.</summary>
    /// <returns>A task.</returns>
    [Test]
    public async Task TryCreate_GivesAReasonForATrackItCannotRead()
    {
        var track = new MediaTrack(FirstTrack, MediaTrackKind.Audio, MediaCodec.Pcm, SampleRate, Channels, Channels, 0, 0, ReadOnlyMemory<byte>.Empty);

        using var decoder = OperatingSystem.IsLinux() ? LinuxVideoDecoders.TryCreate(track, out var reason) : PlatformVideoDecoders.TryCreateNative(track, out reason);

        await Assert.That(decoder).IsNull();
        await Assert.That(reason).IsNotNull();
    }

    /// <summary>The Linux sound output reports that it cannot start instead of throwing, and refuses writes until it starts.</summary>
    /// <returns>A task.</returns>
    [Test]
    public async Task LinuxAudioOutput_NeverThrowsWithoutAServer()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        using var output = LinuxAudioOutputs.Create();
        await Assert.That(output).IsNotNull();
        var samples = new float[ChunkSamples * Channels];

        var written = output!.Write(samples, CancellationToken.None);
        var started = output.Start(SampleRate, Channels);

        await Assert.That(written).IsFalse();
        if (started)
        {
            await Assert.That(output.Write(samples, CancellationToken.None)).IsTrue();
        }
    }

    /// <summary>Decodes every picture of the fixture and keeps a copy of each picture's luma.</summary>
    /// <param name="create">Makes the decoder.</param>
    /// <returns>The luma planes, in output order.</returns>
    /// <exception cref="InvalidDataException">The file did not open or no decoder reads it.</exception>
    private static List<byte[]> DecodeAll(Func<MediaTrack, VideoDecoder?> create)
    {
        using var container = MediaContainers.Open(Path.Combine(AppContext.BaseDirectory, "Fixtures", SmallFile)) ?? throw new InvalidDataException("The file did not open.");
        var index = container.FindTrack(MediaTrackKind.Video);
        using var decoder = create(container.Tracks[index]) ?? throw new InvalidDataException("No decoder reads the track.");
        var planes = new List<byte[]>();
        var frames = new List<VideoFrame>();
        var packet = new MediaPacket();
        while (container.ReadPacket(packet))
        {
            if (packet.Track != index)
            {
                continue;
            }

            decoder.Decode(packet, frames);
            Collect(frames, planes);
        }

        decoder.Flush(frames);
        Collect(frames, planes);
        return planes;
    }

    /// <summary>Copies the luma of each frame and clears the list.</summary>
    /// <param name="frames">The frames.</param>
    /// <param name="planes">Receives the copies.</param>
    private static void Collect(List<VideoFrame> frames, List<byte[]> planes)
    {
        foreach (var frame in frames)
        {
            planes.Add((byte[])frame.Y.Clone());
        }

        frames.Clear();
    }
}
