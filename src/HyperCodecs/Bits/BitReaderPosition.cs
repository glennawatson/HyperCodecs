// Copyright (c) 2026 Glenn Watson. All rights reserved.
// Glenn Watson licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Runtime.CompilerServices;

namespace HyperCodecs.Bits;

/// <summary>The bit counting the readers that keep a window of bits share.</summary>
internal static class BitReaderPosition
{
    /// <summary>The bits in a byte.</summary>
    private const int ByteBits = 8;

    /// <summary>Works out how many bits have been read.</summary>
    /// <param name="next">The index of the next byte to load into the window.</param>
    /// <param name="virtualBits">The zero bits supplied after the end of the data and counted in the window.</param>
    /// <param name="count">The bits in the window that count.</param>
    /// <returns>The number of bits read so far.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static long Read(int next, int virtualBits, int count) => ((long)next * ByteBits) + virtualBits - count;

    /// <summary>Works out how many bits are left in the data.</summary>
    /// <param name="length">The length of the data in bytes.</param>
    /// <param name="position">The number of bits read so far.</param>
    /// <returns>The number of bits left, which is negative after an overrun.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static long Left(int length, long position) => ((long)length * ByteBits) - position;
}
