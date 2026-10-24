// Copyright (c) 2026 Glenn Watson. All rights reserved.
// Glenn Watson licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Buffers.Binary;
using System.Security.Cryptography;

namespace HyperCodecs.Benchmarks;

/// <summary>Builds a RIFF/WAVE file of stereo noise in memory, so the WAVE and PCM benchmarks need no fixture.</summary>
internal static class WaveClip
{
    /// <summary>The samples a second.</summary>
    private const int Rate = 44_100;

    /// <summary>The channels.</summary>
    private const int Channels = 2;

    /// <summary>The seconds of sound.</summary>
    private const int Seconds = 5;

    /// <summary>The bits in a sample of the plain file.</summary>
    private const int DefaultBits = 16;

    /// <summary>The bits in a byte.</summary>
    private const int BitsPerByte = 8;

    /// <summary>The format code of whole number sound.</summary>
    private const short PcmFormat = 1;

    /// <summary>The size of the format chunk's body.</summary>
    private const int FormatSize = 16;

    /// <summary>The offset of the RIFF length.</summary>
    private const int RiffLengthOffset = 4;

    /// <summary>The offset of the form name and the format chunk's name.</summary>
    private const int FormOffset = 8;

    /// <summary>The offset of the format chunk's length.</summary>
    private const int FormatLengthOffset = 16;

    /// <summary>The offset of the format code.</summary>
    private const int FormatCodeOffset = 20;

    /// <summary>The offset of the channel count.</summary>
    private const int ChannelsOffset = 22;

    /// <summary>The offset of the sample rate.</summary>
    private const int RateOffset = 24;

    /// <summary>The offset of the bytes a second.</summary>
    private const int ByteRateOffset = 28;

    /// <summary>The offset of the bytes in a frame.</summary>
    private const int BlockAlignOffset = 32;

    /// <summary>The offset of the bits in a sample.</summary>
    private const int BitsOffset = 34;

    /// <summary>The offset of the sound chunk's name.</summary>
    private const int DataNameOffset = 36;

    /// <summary>The offset of the sound chunk's length.</summary>
    private const int DataLengthOffset = 40;

    /// <summary>The size of the headers before the sound.</summary>
    private const int HeaderSize = 44;

    /// <summary>The size of a chunk header, used to find the RIFF length.</summary>
    private const int ChunkHeaderSize = 8;

    /// <summary>Builds a 16-bit file.</summary>
    /// <returns>The file.</returns>
    internal static byte[] Build() => Build(DefaultBits);

    /// <summary>Builds a file of noise.</summary>
    /// <param name="bits">The bits in each sample: 16, 24 or 32.</param>
    /// <returns>The file.</returns>
    internal static byte[] Build(int bits)
    {
        var bytesPerSample = bits / BitsPerByte;
        var data = Rate * Channels * Seconds * bytesPerSample;
        var file = new byte[HeaderSize + data];
        "RIFF"u8.CopyTo(file);
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(RiffLengthOffset), file.Length - ChunkHeaderSize);
        "WAVEfmt "u8.CopyTo(file.AsSpan(FormOffset));
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(FormatLengthOffset), FormatSize);
        BinaryPrimitives.WriteInt16LittleEndian(file.AsSpan(FormatCodeOffset), PcmFormat);
        BinaryPrimitives.WriteInt16LittleEndian(file.AsSpan(ChannelsOffset), Channels);
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(RateOffset), Rate);
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(ByteRateOffset), Rate * Channels * bytesPerSample);
        BinaryPrimitives.WriteInt16LittleEndian(file.AsSpan(BlockAlignOffset), (short)(Channels * bytesPerSample));
        BinaryPrimitives.WriteInt16LittleEndian(file.AsSpan(BitsOffset), (short)bits);
        "data"u8.CopyTo(file.AsSpan(DataNameOffset));
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(DataLengthOffset), data);
        RandomNumberGenerator.Fill(file.AsSpan(HeaderSize));
        return file;
    }
}
