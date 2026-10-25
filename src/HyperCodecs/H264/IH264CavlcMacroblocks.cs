// Copyright (c) 2026 Glenn Watson. All rights reserved.
// Glenn Watson licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using HyperCodecs.Bits;

namespace HyperCodecs.H264;

/// <summary>Reads and reconstructs the macroblocks of a CAVLC slice that has skip runs.</summary>
internal interface IH264CavlcMacroblocks
{
    /// <summary>Reconstructs a skipped macroblock.</summary>
    /// <param name="address">The macroblock's address.</param>
    void SkipMacroblock(int address);

    /// <summary>Reads and reconstructs one macroblock.</summary>
    /// <param name="reader">The bits.</param>
    /// <param name="address">The macroblock's address.</param>
    /// <exception cref="InvalidDataException">The macroblock is damaged.</exception>
    void DecodeMacroblock(ref MsbBitReader reader, int address);
}
