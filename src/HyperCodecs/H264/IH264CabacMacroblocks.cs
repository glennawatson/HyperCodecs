// Copyright (c) 2026 Glenn Watson. All rights reserved.
// Glenn Watson licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace HyperCodecs.H264;

/// <summary>Reads and reconstructs the macroblocks of a CABAC slice.</summary>
internal interface IH264CabacMacroblocks
{
    /// <summary>Reads and reconstructs one macroblock.</summary>
    /// <param name="engine">The arithmetic decoder.</param>
    /// <param name="rbsp">The slice's payload, for macroblocks of raw samples.</param>
    /// <param name="address">The macroblock's address.</param>
    /// <exception cref="InvalidDataException">The macroblock is damaged.</exception>
    void DecodeMacroblock(ref CabacEngine engine, ReadOnlySpan<byte> rbsp, int address);
}
