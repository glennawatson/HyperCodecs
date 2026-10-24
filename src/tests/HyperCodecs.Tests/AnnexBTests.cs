// Copyright (c) 2026 Glenn Watson. All rights reserved.
// Glenn Watson licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Buffers;
using HyperCodecs.Platform.Video;

namespace HyperCodecs.Tests;

/// <summary>Checks the conversion from MP4 style length prefixed NAL units to the byte stream with start codes that operating system decoders take.</summary>
public sealed class AnnexBTests
{
    /// <summary>The H.264 fixture.</summary>
    private const string H264File = "h264-intra-baseline.mp4";

    /// <summary>The H.265 fixture.</summary>
    private const string H265File = "h265-intra.mp4";

    /// <summary>The bytes of a start code.</summary>
    private const int StartCodeLength = 4;

    /// <summary>The bytes of a NAL unit length in the test packets.</summary>
    private const int LengthSize = 4;

    /// <summary>The H.264 NAL unit type of a sequence parameter set.</summary>
    private const int H264Sps = 7;

    /// <summary>The H.264 NAL unit type of a picture parameter set.</summary>
    private const int H264Pps = 8;

    /// <summary>The mask of the NAL unit type in the first byte of an H.264 NAL unit.</summary>
    private const int H264TypeMask = 0x1F;

    /// <summary>The H.265 NAL unit type of a video parameter set.</summary>
    private const int H265Vps = 32;

    /// <summary>The H.265 NAL unit type of a sequence parameter set.</summary>
    private const int H265Sps = 33;

    /// <summary>The H.265 NAL unit type of a picture parameter set.</summary>
    private const int H265Pps = 34;

    /// <summary>The shift of the NAL unit type in the first byte of an H.265 NAL unit.</summary>
    private const int H265TypeShift = 1;

    /// <summary>The mask of the NAL unit type in the first byte of an H.265 NAL unit.</summary>
    private const int H265TypeMask = 0x3F;

    /// <summary>The length of the one unit in the packets of short lengths.</summary>
    private const byte UnitLength = 2;

    /// <summary>The first byte of the unit in the packets of short lengths.</summary>
    private const byte UnitHeader = 0x65;

    /// <summary>The second byte of the unit in the packets of short lengths.</summary>
    private const byte UnitBody = 0x11;

    /// <summary>The units of a packet with four byte lengths, then the same units behind start codes.</summary>
    private static readonly byte[] TwoUnitPacket = [0, 0, 0, 3, 0x65, 0xAA, 0xBB, 0, 0, 0, 2, 0x41, 0xCC];

    /// <summary>The stream the packet becomes.</summary>
    private static readonly byte[] TwoUnitStream = [0, 0, 0, 1, 0x65, 0xAA, 0xBB, 0, 0, 0, 1, 0x41, 0xCC];

    /// <summary>A packet whose length runs past its end.</summary>
    private static readonly byte[] OverrunPacket = [0, 0, 0, 9, 0x65, 0xAA];

    /// <summary>A stream of two units, as <see cref="AnnexB.ListUnits"/> reads it.</summary>
    private static readonly byte[] ListedStream = [0, 0, 0, 1, 0x67, 1, 0xFF, 0, 0, 0, 1, 0x68, 0xFE];

    /// <summary>The first unit of <see cref="ListedStream"/>.</summary>
    private static readonly byte[] FirstListed = [0x67, 1, 0xFF];

    /// <summary>The second unit of <see cref="ListedStream"/>.</summary>
    private static readonly byte[] SecondListed = [0x68, 0xFE];

    /// <summary>The unit the packets of short lengths become.</summary>
    private static readonly byte[] ShortUnitStream = [0, 0, 0, 1, UnitHeader, UnitBody];

    /// <summary>A record too short to hold anything.</summary>
    private static readonly byte[] ShortRecord = [1, 0xFF, 0xFE];

    /// <summary>Two NAL units with four byte lengths come out behind start codes, in order.</summary>
    /// <returns>A task.</returns>
    [Test]
    public async Task WriteUnits_PutsAStartCodeBeforeEachUnit()
    {
        var destination = new ArrayBufferWriter<byte>();

        var ok = AnnexB.WriteUnits(TwoUnitPacket, LengthSize, destination);

        await Assert.That(ok).IsTrue();
        await Assert.That(destination.WrittenSpan.ToArray()).IsEquivalentTo(TwoUnitStream);
    }

    /// <summary>A length that runs past the end of the packet is refused.</summary>
    /// <returns>A task.</returns>
    [Test]
    public async Task WriteUnits_RefusesALengthPastTheEnd()
    {
        var ok = AnnexB.WriteUnits(OverrunPacket, LengthSize, new ArrayBufferWriter<byte>());

        await Assert.That(ok).IsFalse();
    }

    /// <summary>Lengths of one, two and three bytes are read.</summary>
    /// <param name="lengthSize">The bytes of each length.</param>
    /// <returns>A task.</returns>
    [Test]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    public async Task WriteUnits_ReadsShortLengths(int lengthSize)
    {
        var packet = new byte[lengthSize + UnitLength];
        packet[lengthSize - 1] = UnitLength;
        packet[lengthSize] = UnitHeader;
        packet[lengthSize + 1] = UnitBody;
        var destination = new ArrayBufferWriter<byte>();

        var ok = AnnexB.WriteUnits(packet, lengthSize, destination);

        await Assert.That(ok).IsTrue();
        await Assert.That(destination.WrittenSpan.ToArray()).IsEquivalentTo(ShortUnitStream);
    }

    /// <summary>The units a stream holds are listed without their start codes.</summary>
    /// <returns>A task.</returns>
    [Test]
    public async Task ListUnits_FindsEachUnit()
    {
        var units = new List<Range>();

        AnnexB.ListUnits(ListedStream, units);

        await Assert.That(units.Count).IsEqualTo(UnitLength);
        await Assert.That(ListedStream[units[0]]).IsEquivalentTo(FirstListed);
        await Assert.That(ListedStream[units[1]]).IsEquivalentTo(SecondListed);
    }

    /// <summary>The configuration record of an H.264 file gives a sequence and a picture parameter set.</summary>
    /// <returns>A task.</returns>
    [Test]
    public async Task WriteParameterSets_ReadsTheH264Record()
    {
        var types = ParameterSetTypes(H264File, MediaCodec.H264, static first => first & H264TypeMask);

        await Assert.That(types).Contains(H264Sps);
        await Assert.That(types).Contains(H264Pps);
    }

    /// <summary>The configuration record of an H.265 file gives video, sequence and picture parameter sets.</summary>
    /// <returns>A task.</returns>
    [Test]
    public async Task WriteParameterSets_ReadsTheH265Record()
    {
        var types = ParameterSetTypes(H265File, MediaCodec.H265, static first => (first >> H265TypeShift) & H265TypeMask);

        await Assert.That(types).Contains(H265Vps);
        await Assert.That(types).Contains(H265Sps);
        await Assert.That(types).Contains(H265Pps);
    }

    /// <summary>A record that is too short gives no sets and no length size.</summary>
    /// <returns>A task.</returns>
    [Test]
    public async Task ShortRecords_GiveNothing()
    {
        var destination = new ArrayBufferWriter<byte>();

        await Assert.That(AnnexB.WriteParameterSets(MediaCodec.H264, ShortRecord, destination)).IsFalse();
        await Assert.That(AnnexB.WriteParameterSets(MediaCodec.H265, ShortRecord, destination)).IsFalse();
        await Assert.That(AnnexB.LengthSize(MediaCodec.H264, ShortRecord)).IsEqualTo(0);
        await Assert.That(destination.WrittenCount).IsEqualTo(0);
    }

    /// <summary>The length size of a real record is four bytes, and every packet of the file converts.</summary>
    /// <returns>A task.</returns>
    /// <exception cref="InvalidDataException">The file did not open.</exception>
    [Test]
    public async Task EveryPacketOfAFileConverts()
    {
        using var container = MediaContainers.Open(Path.Combine(AppContext.BaseDirectory, "Fixtures", H264File)) ?? throw new InvalidDataException("The file did not open.");
        var index = container.FindTrack(MediaTrackKind.Video);
        var lengthSize = AnnexB.LengthSize(MediaCodec.H264, container.Tracks[index].Configuration.Span);
        var packet = new MediaPacket();
        var converted = 0;
        var failed = 0;
        while (container.ReadPacket(packet))
        {
            if (packet.Track != index)
            {
                continue;
            }

            var destination = new ArrayBufferWriter<byte>();
            if (AnnexB.WriteUnits(packet.Data, lengthSize, destination) && destination.WrittenCount > StartCodeLength)
            {
                converted++;
            }
            else
            {
                failed++;
            }
        }

        await Assert.That(lengthSize).IsEqualTo(LengthSize);
        await Assert.That(failed).IsEqualTo(0);
        await Assert.That(converted).IsGreaterThan(0);
    }

    /// <summary>Reads the parameter sets of a file's configuration record and lists the type of each.</summary>
    /// <param name="file">The fixture.</param>
    /// <param name="codec">The coding.</param>
    /// <param name="typeOf">Gets the NAL unit type from a unit's first byte.</param>
    /// <returns>The types.</returns>
    /// <exception cref="InvalidDataException">The file did not open, or its record held no parameter set.</exception>
    private static List<int> ParameterSetTypes(string file, MediaCodec codec, Func<int, int> typeOf)
    {
        using var container = MediaContainers.Open(Path.Combine(AppContext.BaseDirectory, "Fixtures", file)) ?? throw new InvalidDataException("The file did not open.");
        var track = container.Tracks[container.FindTrack(MediaTrackKind.Video)];
        var destination = new ArrayBufferWriter<byte>();
        if (!AnnexB.WriteParameterSets(codec, track.Configuration.Span, destination))
        {
            throw new InvalidDataException("The record held no parameter set.");
        }

        var stream = destination.WrittenMemory.ToArray();
        var units = new List<Range>();
        AnnexB.ListUnits(stream, units);
        return [.. units.Select(unit => typeOf(stream[unit][0]))];
    }
}
