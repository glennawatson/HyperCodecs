// Copyright (c) 2026 Glenn Watson. All rights reserved.
// Glenn Watson licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Buffers;
using BenchmarkDotNet.Attributes;
using HyperCodecs.Platform.Video;

namespace HyperCodecs.Benchmarks;

/// <summary>Converts every packet of an H.264 file to the byte stream with start codes that the Windows and Android decoders take.</summary>
public class HyperCodecsAnnexBBenchmarks
{
    /// <summary>The file.</summary>
    private const string File = "h264-p-cabac.mp4";

    /// <summary>The coded pictures, read once.</summary>
    private List<MediaPacket>? _packets;

    /// <summary>The bytes of each NAL unit's length.</summary>
    private int _lengthSize;

    /// <summary>Receives the converted packets, emptied before each packet.</summary>
    private ArrayBufferWriter<byte>? _destination;

    /// <summary>Reads the packets of the file.</summary>
    /// <exception cref="InvalidDataException">The file did not open.</exception>
    [GlobalSetup]
    public void Setup()
    {
        using var container = MediaContainers.Open(Path.Combine(AppContext.BaseDirectory, "Fixtures", File)) ?? throw new InvalidDataException($"{File} did not open.");
        var index = container.FindTrack(MediaTrackKind.Video);
        _lengthSize = AnnexB.LengthSize(container.Tracks[index].Codec, container.Tracks[index].Configuration.Span);
        _packets = [];
        var packet = new MediaPacket();
        while (container.ReadPacket(packet))
        {
            if (packet.Track != index)
            {
                continue;
            }

            var copy = new MediaPacket();
            packet.Data.CopyTo(copy.Begin(packet.Track, packet.Length, packet.Time, packet.Duration, packet.IsKey));
            _packets.Add(copy);
        }

        _destination = new();
    }

    /// <summary>Converts every packet.</summary>
    /// <returns>The bytes written.</returns>
    [Benchmark]
    public long ConvertAll()
    {
        var bytes = 0L;
        foreach (var packet in _packets!)
        {
            _destination!.ResetWrittenCount();
            _ = AnnexB.WriteUnits(packet.Data, _lengthSize, _destination);
            bytes += _destination.WrittenCount;
        }

        return bytes;
    }
}
