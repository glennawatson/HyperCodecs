// Copyright (c) 2026 Glenn Watson. All rights reserved.
// Glenn Watson licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;

namespace HyperCodecs.Compat;

/// <summary>
/// Interleaves the halves of two vectors, the building block of transposing a block of bytes. .NET 11 has <c>Vector128.ZipLower</c> and
/// <c>Vector128.ZipUpper</c>; before that each result is made from two shuffles. Either way the units are the 1, 2 or 4 byte pieces of a vector of bytes.
/// </summary>
internal static class VectorZip
{
#if !NET11_0_OR_GREATER
    /// <summary>Gets the shuffle that places the lower bytes of the left vector in the even places and zeroes the rest.</summary>
    private static ReadOnlySpan<byte> LowerBytesLeft => [0x00, 0xFF, 0x01, 0xFF, 0x02, 0xFF, 0x03, 0xFF, 0x04, 0xFF, 0x05, 0xFF, 0x06, 0xFF, 0x07, 0xFF];

    /// <summary>Gets the shuffle that places the lower bytes of the right vector in the odd places and zeroes the rest.</summary>
    private static ReadOnlySpan<byte> LowerBytesRight => [0xFF, 0x00, 0xFF, 0x01, 0xFF, 0x02, 0xFF, 0x03, 0xFF, 0x04, 0xFF, 0x05, 0xFF, 0x06, 0xFF, 0x07];

    /// <summary>Gets the shuffle that places the upper bytes of the left vector in the even places and zeroes the rest.</summary>
    private static ReadOnlySpan<byte> UpperBytesLeft => [0x08, 0xFF, 0x09, 0xFF, 0x0A, 0xFF, 0x0B, 0xFF, 0x0C, 0xFF, 0x0D, 0xFF, 0x0E, 0xFF, 0x0F, 0xFF];

    /// <summary>Gets the shuffle that places the upper bytes of the right vector in the odd places and zeroes the rest.</summary>
    private static ReadOnlySpan<byte> UpperBytesRight => [0xFF, 0x08, 0xFF, 0x09, 0xFF, 0x0A, 0xFF, 0x0B, 0xFF, 0x0C, 0xFF, 0x0D, 0xFF, 0x0E, 0xFF, 0x0F];

    /// <summary>Gets the shuffle that places the lower two byte units of the left vector in the even places.</summary>
    private static ReadOnlySpan<byte> LowerWordsLeft => [0x00, 0x01, 0xFF, 0xFF, 0x02, 0x03, 0xFF, 0xFF, 0x04, 0x05, 0xFF, 0xFF, 0x06, 0x07, 0xFF, 0xFF];

    /// <summary>Gets the shuffle that places the lower two byte units of the right vector in the odd places.</summary>
    private static ReadOnlySpan<byte> LowerWordsRight => [0xFF, 0xFF, 0x00, 0x01, 0xFF, 0xFF, 0x02, 0x03, 0xFF, 0xFF, 0x04, 0x05, 0xFF, 0xFF, 0x06, 0x07];

    /// <summary>Gets the shuffle that places the upper two byte units of the left vector in the even places.</summary>
    private static ReadOnlySpan<byte> UpperWordsLeft => [0x08, 0x09, 0xFF, 0xFF, 0x0A, 0x0B, 0xFF, 0xFF, 0x0C, 0x0D, 0xFF, 0xFF, 0x0E, 0x0F, 0xFF, 0xFF];

    /// <summary>Gets the shuffle that places the upper two byte units of the right vector in the odd places.</summary>
    private static ReadOnlySpan<byte> UpperWordsRight => [0xFF, 0xFF, 0x08, 0x09, 0xFF, 0xFF, 0x0A, 0x0B, 0xFF, 0xFF, 0x0C, 0x0D, 0xFF, 0xFF, 0x0E, 0x0F];

    /// <summary>Gets the shuffle that places the lower four byte units of the left vector in the even places.</summary>
    private static ReadOnlySpan<byte> LowerDoubleWordsLeft => [0x00, 0x01, 0x02, 0x03, 0xFF, 0xFF, 0xFF, 0xFF, 0x04, 0x05, 0x06, 0x07, 0xFF, 0xFF, 0xFF, 0xFF];

    /// <summary>Gets the shuffle that places the lower four byte units of the right vector in the odd places.</summary>
    private static ReadOnlySpan<byte> LowerDoubleWordsRight => [0xFF, 0xFF, 0xFF, 0xFF, 0x00, 0x01, 0x02, 0x03, 0xFF, 0xFF, 0xFF, 0xFF, 0x04, 0x05, 0x06, 0x07];

    /// <summary>Gets the shuffle that places the upper four byte units of the left vector in the even places.</summary>
    private static ReadOnlySpan<byte> UpperDoubleWordsLeft => [0x08, 0x09, 0x0A, 0x0B, 0xFF, 0xFF, 0xFF, 0xFF, 0x0C, 0x0D, 0x0E, 0x0F, 0xFF, 0xFF, 0xFF, 0xFF];

    /// <summary>Gets the shuffle that places the upper four byte units of the right vector in the odd places.</summary>
    private static ReadOnlySpan<byte> UpperDoubleWordsRight => [0xFF, 0xFF, 0xFF, 0xFF, 0x08, 0x09, 0x0A, 0x0B, 0xFF, 0xFF, 0xFF, 0xFF, 0x0C, 0x0D, 0x0E, 0x0F];
#endif

    /// <summary>Interleaves the bytes of the lower halves of two vectors: left 0, right 0, left 1, right 1 and so on.</summary>
    /// <param name="left">The vector whose bytes take the even places.</param>
    /// <param name="right">The vector whose bytes take the odd places.</param>
    /// <returns>The interleaved bytes.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Vector128<byte> LowerBytes(Vector128<byte> left, Vector128<byte> right) =>
#if NET11_0_OR_GREATER
        Vector128.ZipLower(left, right);
#else
        Vector128.Shuffle(left, Vector128.Create(LowerBytesLeft)) | Vector128.Shuffle(right, Vector128.Create(LowerBytesRight));
#endif

    /// <summary>Interleaves the bytes of the upper halves of two vectors.</summary>
    /// <param name="left">The vector whose bytes take the even places.</param>
    /// <param name="right">The vector whose bytes take the odd places.</param>
    /// <returns>The interleaved bytes.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Vector128<byte> UpperBytes(Vector128<byte> left, Vector128<byte> right) =>
#if NET11_0_OR_GREATER
        Vector128.ZipUpper(left, right);
#else
        Vector128.Shuffle(left, Vector128.Create(UpperBytesLeft)) | Vector128.Shuffle(right, Vector128.Create(UpperBytesRight));
#endif

    /// <summary>Interleaves the two byte units of the lower halves of two vectors.</summary>
    /// <param name="left">The vector whose units take the even places.</param>
    /// <param name="right">The vector whose units take the odd places.</param>
    /// <returns>The interleaved units.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Vector128<byte> LowerWords(Vector128<byte> left, Vector128<byte> right) =>
#if NET11_0_OR_GREATER
        Vector128.ZipLower(left.AsUInt16(), right.AsUInt16()).AsByte();
#else
        Vector128.Shuffle(left, Vector128.Create(LowerWordsLeft)) | Vector128.Shuffle(right, Vector128.Create(LowerWordsRight));
#endif

    /// <summary>Interleaves the two byte units of the upper halves of two vectors.</summary>
    /// <param name="left">The vector whose units take the even places.</param>
    /// <param name="right">The vector whose units take the odd places.</param>
    /// <returns>The interleaved units.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Vector128<byte> UpperWords(Vector128<byte> left, Vector128<byte> right) =>
#if NET11_0_OR_GREATER
        Vector128.ZipUpper(left.AsUInt16(), right.AsUInt16()).AsByte();
#else
        Vector128.Shuffle(left, Vector128.Create(UpperWordsLeft)) | Vector128.Shuffle(right, Vector128.Create(UpperWordsRight));
#endif

    /// <summary>Interleaves the four byte units of the lower halves of two vectors.</summary>
    /// <param name="left">The vector whose units take the even places.</param>
    /// <param name="right">The vector whose units take the odd places.</param>
    /// <returns>The interleaved units.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Vector128<byte> LowerDoubleWords(Vector128<byte> left, Vector128<byte> right) =>
#if NET11_0_OR_GREATER
        Vector128.ZipLower(left.AsUInt32(), right.AsUInt32()).AsByte();
#else
        Vector128.Shuffle(left, Vector128.Create(LowerDoubleWordsLeft)) | Vector128.Shuffle(right, Vector128.Create(LowerDoubleWordsRight));
#endif

    /// <summary>Interleaves the four byte units of the upper halves of two vectors.</summary>
    /// <param name="left">The vector whose units take the even places.</param>
    /// <param name="right">The vector whose units take the odd places.</param>
    /// <returns>The interleaved units.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Vector128<byte> UpperDoubleWords(Vector128<byte> left, Vector128<byte> right) =>
#if NET11_0_OR_GREATER
        Vector128.ZipUpper(left.AsUInt32(), right.AsUInt32()).AsByte();
#else
        Vector128.Shuffle(left, Vector128.Create(UpperDoubleWordsLeft)) | Vector128.Shuffle(right, Vector128.Create(UpperDoubleWordsRight));
#endif
}
