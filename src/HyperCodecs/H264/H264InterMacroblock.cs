// Copyright (c) 2026 Glenn Watson. All rights reserved.
// Glenn Watson licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace HyperCodecs.H264;

/// <summary>Sets up the macroblock about to be decoded as an inter macroblock.</summary>
internal static class H264InterMacroblock
{
    /// <summary>Makes the macroblock an inter macroblock with no motion, residual or neighbour context yet.</summary>
    /// <param name="mb">The macroblock being decoded.</param>
    /// <param name="picture">The picture being decoded.</param>
    /// <param name="slice">The number of the slice.</param>
    /// <param name="address">The macroblock's address.</param>
    internal static void Begin(H264CurrentMb mb, H264Picture picture, int slice, int address)
    {
        mb.Address = address;
        mb.X = address % picture.MbWidth;
        mb.Y = address / picture.MbWidth;
        mb.Kind = H264MbKind.Inter;
        mb.CbpLuma = 0;
        mb.CbpChroma = 0;
        mb.ClearCoefficients();
        picture.SliceOfMb[address] = slice;
        picture.Kinds[address] = H264MbKind.Inter;
        Array.Clear(picture.Counts, address * H264Picture.CountsPerMb, H264Picture.CountsPerMb);
        picture.ClearMotion(address);
    }

    /// <summary>Does what <see cref="Begin"/> does and also clears what the CABAC contexts of the neighbours read.</summary>
    /// <param name="mb">The macroblock being decoded.</param>
    /// <param name="picture">The picture being decoded.</param>
    /// <param name="slice">The number of the slice.</param>
    /// <param name="address">The macroblock's address.</param>
    internal static void BeginCabac(H264CurrentMb mb, H264Picture picture, int slice, int address)
    {
        Begin(mb, picture, slice, address);
        picture.Cbp[address] = 0;
        picture.ChromaModes[address] = 0;
        picture.DcFlags[address] = 0;
    }
}
