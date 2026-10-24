// Copyright (c) 2026 Glenn Watson. All rights reserved.
// Glenn Watson licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using HyperCodecs;

namespace HyperCodecs.Benchmarks;

/// <summary>
/// The coded pictures of the first picture track of a file, read once, with the decoder that decodes them. The decoder is made once
/// and reset for each run, and each picture is given back as the player does, so a run measures decoding and not building a decoder.
/// </summary>
internal sealed class VideoClip : IDisposable
{
    /// <summary>The coded pictures.</summary>
    private readonly List<MediaPacket> _packets;

    /// <summary>The decoder, made once.</summary>
    private readonly VideoDecoder _decoder;

    /// <summary>The decoded pictures, cleared after each run.</summary>
    private readonly List<VideoFrame> _frames = [];

    /// <summary>Initializes a new instance of the <see cref="VideoClip"/> class.</summary>
    /// <param name="decoder">The decoder.</param>
    /// <param name="packets">The coded pictures.</param>
    private VideoClip(VideoDecoder decoder, List<MediaPacket> packets)
    {
        _decoder = decoder;
        _packets = packets;
    }

    /// <inheritdoc/>
    public void Dispose() => _decoder.Dispose();

    /// <summary>Reads the coded pictures of a file.</summary>
    /// <param name="name">The file name in the fixtures folder next to the benchmarks.</param>
    /// <returns>The clip.</returns>
    /// <exception cref="InvalidDataException">The file did not open, or no decoder reads its pictures.</exception>
    internal static VideoClip Load(string name) => LoadFile(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));

    /// <summary>Reads the coded pictures of a file.</summary>
    /// <param name="path">The path of the file.</param>
    /// <returns>The clip.</returns>
    /// <exception cref="InvalidDataException">The file did not open, or no decoder reads its pictures.</exception>
    internal static VideoClip LoadFile(string path)
    {
        var name = Path.GetFileName(path);
        using var container = MediaContainers.Open(path)
            ?? throw new InvalidDataException($"{name} did not open.");
        var index = container.FindTrack(MediaTrackKind.Video);
        var packets = new List<MediaPacket>();
        var packet = new MediaPacket();
        while (container.ReadPacket(packet))
        {
            if (packet.Track != index)
            {
                continue;
            }

            var copy = new MediaPacket();
            packet.Data.CopyTo(copy.Begin(packet.Track, packet.Length, packet.Time, packet.Duration, packet.IsKey));
            packets.Add(copy);
        }

        var decoder = VideoDecoders.Create(container.Tracks[index]) ?? throw new InvalidDataException($"No decoder reads {name}.");
        return new(decoder, packets);
    }

    /// <summary>Decodes every picture from the start.</summary>
    /// <returns>The number of pictures.</returns>
    internal int Decode()
    {
        _decoder.Reset();
        var count = 0;
        foreach (var packet in _packets)
        {
            _decoder.Decode(packet, _frames);
            count += Recycle();
        }

        _decoder.Flush(_frames);
        return count + Recycle();
    }

    /// <summary>Hands the pictures back to the decoder, as the player does once it has shown them.</summary>
    /// <returns>The number of pictures.</returns>
    private int Recycle()
    {
        var count = _frames.Count;
        foreach (var frame in _frames)
        {
            _decoder.Recycle(frame);
        }

        _frames.Clear();
        return count;
    }
}
