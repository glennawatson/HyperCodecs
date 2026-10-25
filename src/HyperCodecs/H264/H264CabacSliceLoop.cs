// Copyright (c) 2026 Glenn Watson. All rights reserved.
// Glenn Watson licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace HyperCodecs.H264;

/// <summary>Reads the macroblocks of a CABAC slice, whatever its type.</summary>
internal static class H264CabacSliceLoop
{
    /// <summary>Decodes the macroblocks of a slice.</summary>
    /// <typeparam name="TDecoder">The decoder of the slice's macroblocks.</typeparam>
    /// <param name="decoder">The decoder that reads and reconstructs the macroblocks.</param>
    /// <param name="picture">The picture being decoded.</param>
    /// <param name="start">What the context states are initialised from.</param>
    /// <param name="rbsp">The payload of the slice's NAL unit.</param>
    /// <param name="dataBit">The bit index where the slice data starts (before the alignment bits).</param>
    /// <param name="firstMb">The address of the slice's first macroblock.</param>
    /// <exception cref="InvalidDataException">The slice data is damaged.</exception>
    internal static void Run<TDecoder>(TDecoder decoder, H264Picture picture, H264CabacSliceStart start, ReadOnlySpan<byte> rbsp, long dataBit, int firstMb)
        where TDecoder : class, IH264CabacMacroblocks
    {
        H264CabacContexts.Initialize(start.Contexts, start.IsIntra, start.InitIdc, start.Qp);
        var engine = new CabacEngine(rbsp, (dataBit + H264Layout.SampleBits - 1) / H264Layout.SampleBits * H264Layout.SampleBits, start.Contexts);
        var total = picture.MbWidth * picture.MbHeight;
        var address = firstMb;
        do
        {
            if (address >= total)
            {
                throw new InvalidDataException("An H.264 slice has more macroblocks than the picture.");
            }

            decoder.DecodeMacroblock(ref engine, rbsp, address);
            H264Deblocking.MacroblockDecoded(picture, address);
            address++;
        }
        while (engine.DecodeTerminate() == 0 && !engine.IsOverrun);
        if (engine.IsOverrun)
        {
            throw new InvalidDataException("An H.264 slice is cut short.");
        }
    }
}
