// Copyright (c) 2026 Glenn Watson. All rights reserved.
// Glenn Watson licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Runtime.CompilerServices;

namespace HyperCodecs.H265;

/// <summary>The shift, rounding offset and largest sample that turn intermediate prediction samples into output samples.</summary>
/// <param name="Shift">The right shift.</param>
/// <param name="Offset">The rounding offset added before the shift.</param>
/// <param name="Maximum">The largest output sample.</param>
internal readonly record struct H265OutputRounding(int Shift, int Offset, int Maximum)
{
    /// <summary>Works out the rounding of a prediction.</summary>
    /// <param name="shift">The right shift.</param>
    /// <param name="bitDepth">The bit depth of the output samples.</param>
    /// <returns>The rounding.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static H265OutputRounding Create(int shift, int bitDepth) => new(shift, 1 << (shift - 1), (1 << bitDepth) - 1);
}
