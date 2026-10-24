// Copyright (c) 2026 Glenn Watson. All rights reserved.
// Glenn Watson licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using HyperCodecs.Compat;

namespace HyperCodecs.H264;

/// <summary>
/// Filters a vertical edge of a macroblock (clause 8.7.2) eight rows at a time. The samples across such an edge lie in one row, so eight rows of eight
/// bytes are turned on their side, which puts the samples at the same distance from the edge in one vector, and the filter of horizontal edges is run on them.
/// The results are those of filtering the rows one by one.
/// </summary>
internal static class H264DeblockVertical
{
    /// <summary>The rows filtered at a time.</summary>
    private const int Rows = 8;

    /// <summary>The samples of a row that are read: four either side of the edge.</summary>
    private const int Reach = 4;

    /// <summary>The bytes of the turned block.</summary>
    private const int BlockSize = Rows * Rows;

    /// <summary>The rows of a luma edge.</summary>
    private const int LumaRows = 16;

    /// <summary>The rows of a chroma edge.</summary>
    private const int ChromaRows = 8;

    /// <summary>The index in the turned block of the first sample of q0, which is the fifth column.</summary>
    private const int Q0Index = Reach * Rows;

    /// <summary>The bytes in a vector.</summary>
    private const int VectorBytes = 16;

    /// <summary>The offset of the second vector of the turned block.</summary>
    private const int Second = VectorBytes;

    /// <summary>The offset of the third vector of the turned block.</summary>
    private const int Third = VectorBytes * 2;

    /// <summary>The offset of the fourth vector of the turned block.</summary>
    private const int Fourth = VectorBytes * 3;

    /// <summary>The bytes of one column of the turned block.</summary>
    private const int ColumnStep = Rows;

    /// <summary>The number of the third row or column; the first two are 0 and 1, and the rows and columns are named by letters after them.</summary>
    private const int RowC = 2;

    /// <summary>The number of the fourth row or column.</summary>
    private const int RowD = 3;

    /// <summary>The number of the fifth row or column.</summary>
    private const int RowE = 4;

    /// <summary>The number of the sixth row or column.</summary>
    private const int RowF = 5;

    /// <summary>The number of the seventh row or column.</summary>
    private const int RowG = 6;

    /// <summary>The number of the eighth row or column.</summary>
    private const int RowH = 7;

    /// <summary>Filters one vertical edge of a macroblock in one plane.</summary>
    /// <param name="plane">The plane.</param>
    /// <param name="start">The index of q0 in the first row of the edge.</param>
    /// <param name="stride">The samples in a row of the plane.</param>
    /// <param name="strengths">The boundary strengths of the four segments, which <see cref="H264DeblockHorizontal.Handles"/> accepted.</param>
    /// <param name="limits">The thresholds.</param>
    /// <param name="chroma">Whether the plane is chroma: the edge is eight rows and only p0 and q0 change.</param>
    /// <exception cref="InvalidDataException">The edge reaches outside the plane.</exception>
    [SkipLocalsInit]
    internal static void Filter(byte[] plane, int start, int stride, ReadOnlySpan<int> strengths, H264EdgeLimits limits, bool chroma)
    {
        if (limits.Alpha == 0 || limits.Beta == 0)
        {
            return;
        }

        var rows = chroma ? ChromaRows : LumaRows;
        if (start < Reach || start + ((rows - 1) * stride) + Reach > plane.Length)
        {
            throw new InvalidDataException("An H.264 edge lies outside its picture.");
        }

        ref var origin = ref MemoryMarshal.GetArrayDataReference(plane);
        Span<byte> block = stackalloc byte[BlockSize];
        for (var group = 0; group < rows / Rows; group++)
        {
            var at = start - Reach + (group * Rows * stride);
            TurnToColumns(ref origin, at, stride, block);
            H264DeblockHorizontal.FilterGroup(ref MemoryMarshal.GetReference(block), Q0Index, Rows, new(strengths, group, limits, chroma));
            TurnToRows(ref origin, at, stride, block);
        }
    }

    /// <summary>Reads eight rows of eight samples and writes them to a block turned on its side: the first byte of each row first, then the second, and so on.</summary>
    /// <param name="origin">The first sample of the plane.</param>
    /// <param name="at">The index of the first sample of the first row.</param>
    /// <param name="stride">The samples in a row of the plane.</param>
    /// <param name="block">Receives the eight columns, each of eight bytes, one after the other.</param>
    private static void TurnToColumns(ref byte origin, int at, int stride, Span<byte> block)
    {
        var a0 = VectorZip.LowerBytes(Row(ref origin, at), Row(ref origin, at + stride));
        var a1 = VectorZip.LowerBytes(Row(ref origin, at + (RowC * stride)), Row(ref origin, at + (RowD * stride)));
        var a2 = VectorZip.LowerBytes(Row(ref origin, at + (RowE * stride)), Row(ref origin, at + (RowF * stride)));
        var a3 = VectorZip.LowerBytes(Row(ref origin, at + (RowG * stride)), Row(ref origin, at + (RowH * stride)));
        var b0 = VectorZip.LowerWords(a0, a1);
        var b1 = VectorZip.UpperWords(a0, a1);
        var b2 = VectorZip.LowerWords(a2, a3);
        var b3 = VectorZip.UpperWords(a2, a3);
        ref var target = ref MemoryMarshal.GetReference(block);
        VectorZip.LowerDoubleWords(b0, b2).StoreUnsafe(ref target);
        VectorZip.UpperDoubleWords(b0, b2).StoreUnsafe(ref target, Second);
        VectorZip.LowerDoubleWords(b1, b3).StoreUnsafe(ref target, Third);
        VectorZip.UpperDoubleWords(b1, b3).StoreUnsafe(ref target, Fourth);
    }

    /// <summary>Writes a block turned on its side back as eight rows of eight samples.</summary>
    /// <param name="origin">The first sample of the plane.</param>
    /// <param name="at">The index of the first sample of the first row.</param>
    /// <param name="stride">The samples in a row of the plane.</param>
    /// <param name="block">The eight columns, each of eight bytes, one after the other.</param>
    private static void TurnToRows(ref byte origin, int at, int stride, ReadOnlySpan<byte> block)
    {
        ref var source = ref MemoryMarshal.GetReference(block);
        var d0 = Pair(ref source, 0, ColumnStep * RowC);
        var d1 = Pair(ref source, ColumnStep, ColumnStep * RowD);
        var d2 = Pair(ref source, ColumnStep * RowE, ColumnStep * RowG);
        var d3 = Pair(ref source, ColumnStep * RowF, ColumnStep * RowH);
        var x0 = VectorZip.LowerBytes(d0, d1);
        var x1 = VectorZip.UpperBytes(d0, d1);
        var y0 = VectorZip.LowerBytes(d2, d3);
        var y1 = VectorZip.UpperBytes(d2, d3);
        var z0 = VectorZip.LowerWords(x0, x1);
        var z1 = VectorZip.UpperWords(x0, x1);
        var w0 = VectorZip.LowerWords(y0, y1);
        var w1 = VectorZip.UpperWords(y0, y1);
        Store(ref origin, at, stride, VectorZip.LowerDoubleWords(z0, w0).AsUInt64(), 0);
        Store(ref origin, at, stride, VectorZip.UpperDoubleWords(z0, w0).AsUInt64(), RowC);
        Store(ref origin, at, stride, VectorZip.LowerDoubleWords(z1, w1).AsUInt64(), RowE);
        Store(ref origin, at, stride, VectorZip.UpperDoubleWords(z1, w1).AsUInt64(), RowG);
    }

    /// <summary>Reads the eight samples of a row as the low half of a vector.</summary>
    /// <param name="origin">The first sample of the plane.</param>
    /// <param name="index">The index of the first of the eight.</param>
    /// <returns>The vector.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector128<byte> Row(ref byte origin, int index) =>
        Vector128.CreateScalarUnsafe(Unsafe.ReadUnaligned<ulong>(ref Unsafe.Add(ref origin, index))).AsByte();

    /// <summary>Puts two columns of the block side by side in a vector.</summary>
    /// <param name="source">The first byte of the block.</param>
    /// <param name="low">The offset of the column for the low half.</param>
    /// <param name="high">The offset of the column for the high half.</param>
    /// <returns>The vector.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector128<byte> Pair(ref byte source, int low, int high) =>
        Vector128.Create(Unsafe.ReadUnaligned<ulong>(ref Unsafe.Add(ref source, low)), Unsafe.ReadUnaligned<ulong>(ref Unsafe.Add(ref source, high))).AsByte();

    /// <summary>Writes two rows of eight samples from a vector.</summary>
    /// <param name="origin">The first sample of the plane.</param>
    /// <param name="at">The index of the first sample of the first row of the group.</param>
    /// <param name="stride">The samples in a row of the plane.</param>
    /// <param name="rows">The two rows, one in each half.</param>
    /// <param name="first">The number of the first of the two rows.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Store(ref byte origin, int at, int stride, Vector128<ulong> rows, int first)
    {
        Unsafe.WriteUnaligned(ref Unsafe.Add(ref origin, at + (first * stride)), rows.GetElement(0));
        Unsafe.WriteUnaligned(ref Unsafe.Add(ref origin, at + ((first + 1) * stride)), rows.GetElement(1));
    }
}
