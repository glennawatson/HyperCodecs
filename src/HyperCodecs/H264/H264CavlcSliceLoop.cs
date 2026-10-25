// Copyright (c) 2026 Glenn Watson. All rights reserved.
// Glenn Watson licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using HyperCodecs.Bits;

namespace HyperCodecs.H264;

/// <summary>Reads the macroblocks of a CAVLC slice that has skip runs, which is how P and B slices are laid out.</summary>
internal static class H264CavlcSliceLoop
{
    /// <summary>Decodes the macroblocks of a slice.</summary>
    /// <typeparam name="TDecoder">The decoder of the slice's macroblocks.</typeparam>
    /// <param name="decoder">The decoder that reads and reconstructs the macroblocks.</param>
    /// <param name="picture">The picture being decoded.</param>
    /// <param name="rbsp">The payload of the slice's NAL unit.</param>
    /// <param name="dataBit">The bit index where the slice data starts.</param>
    /// <param name="firstMb">The address of the slice's first macroblock.</param>
    /// <exception cref="InvalidDataException">The slice data is damaged.</exception>
    internal static void Run<TDecoder>(TDecoder decoder, H264Picture picture, ReadOnlySpan<byte> rbsp, long dataBit, int firstMb)
        where TDecoder : class, IH264CavlcMacroblocks
    {
        var reader = new MsbBitReader(rbsp);
        reader.Seek(dataBit);
        var stop = ExpGolomb.StopBit(rbsp);
        var total = picture.MbWidth * picture.MbHeight;
        var address = firstMb;
        while (true)
        {
            var run = (int)ExpGolomb.ReadUe(ref reader);
            for (var skipped = 0; skipped < run; skipped++)
            {
                CheckAddress(address, total);
                decoder.SkipMacroblock(address);
                H264Deblocking.MacroblockDecoded(picture, address);
                address++;
            }

            if ((run > 0 && !ExpGolomb.MoreData(reader.BitPosition, stop)) || reader.IsOverrun)
            {
                break;
            }

            CheckAddress(address, total);
            decoder.DecodeMacroblock(ref reader, address);
            H264Deblocking.MacroblockDecoded(picture, address);
            address++;
            if (!ExpGolomb.MoreData(reader.BitPosition, stop) || reader.IsOverrun)
            {
                break;
            }
        }

        if (reader.IsOverrun)
        {
            throw new InvalidDataException("An H.264 slice is cut short.");
        }
    }

    /// <summary>Checks that a macroblock address is inside the picture.</summary>
    /// <param name="address">The address.</param>
    /// <param name="total">The macroblocks in the picture.</param>
    /// <exception cref="InvalidDataException">The address is past the last macroblock.</exception>
    private static void CheckAddress(int address, int total)
    {
        if (address >= total)
        {
            throw new InvalidDataException("An H.264 slice has more macroblocks than the picture.");
        }
    }
}
