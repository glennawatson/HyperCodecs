// Copyright (c) 2026 Glenn Watson. All rights reserved.
// Glenn Watson licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using HyperCodecs;

namespace HyperCodecs.Benchmarks;

/// <summary>
/// The packets of the first sound track of a file, read once, with the decoder that decodes them and the buffer it fills. The
/// decoder is made once and reset for each run, so a run measures decoding a packet and not building a decoder.
/// </summary>
internal sealed class SoundClip : IDisposable
{
    /// <summary>The packets.</summary>
    private readonly List<MediaPacket> _packets;

    /// <summary>The decoder, made once.</summary>
    private readonly AudioDecoder _decoder;

    /// <summary>The decoded sound, which is cleared and filled again each time.</summary>
    private readonly PcmBuffer _pcm = new();

    /// <summary>Initializes a new instance of the <see cref="SoundClip"/> class.</summary>
    /// <param name="decoder">The decoder.</param>
    /// <param name="packets">The packets.</param>
    private SoundClip(AudioDecoder decoder, List<MediaPacket> packets)
    {
        _decoder = decoder;
        _packets = packets;
    }

    /// <inheritdoc/>
    public void Dispose() => _decoder.Dispose();

    /// <summary>Reads every packet of the first sound track of a file.</summary>
    /// <param name="name">The file name in the fixtures folder next to the benchmarks.</param>
    /// <returns>The clip.</returns>
    /// <exception cref="InvalidDataException">The file did not open.</exception>
    internal static SoundClip Load(string name)
    {
        using var container = MediaContainers.Open(Path.Combine(AppContext.BaseDirectory, "Fixtures", name))
            ?? throw new InvalidDataException($"{name} did not open.");
        return Load(container);
    }

    /// <summary>Reads every packet of the first sound track of a container.</summary>
    /// <param name="container">The open container.</param>
    /// <returns>The clip.</returns>
    /// <exception cref="InvalidDataException">No decoder reads the track.</exception>
    internal static SoundClip Load(MediaContainer container)
    {
        var index = container.FindTrack(MediaTrackKind.Audio);
        var packets = new List<MediaPacket>();
        while (true)
        {
            var packet = new MediaPacket();
            if (!container.ReadPacket(packet))
            {
                break;
            }

            if (packet.Track == index)
            {
                packets.Add(packet);
            }
        }

        var decoder = AudioDecoders.Create(container.Tracks[index]) ?? throw new InvalidDataException("No decoder reads the track.");
        return new(decoder, packets);
    }

    /// <summary>Decodes every packet from the start.</summary>
    /// <returns>The samples decoded.</returns>
    internal int Decode()
    {
        _decoder.Reset();
        _pcm.Clear();
        foreach (var packet in _packets)
        {
            _decoder.Decode(packet, _pcm);
        }

        return _pcm.Count;
    }
}
