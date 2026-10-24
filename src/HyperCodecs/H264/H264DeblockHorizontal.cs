// Copyright (c) 2026 Glenn Watson. All rights reserved.
// Glenn Watson licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace HyperCodecs.H264;

/// <summary>
/// Filters a horizontal edge of a macroblock (clause 8.7.2) eight columns at a time. The samples across such an edge lie in different rows, so
/// each of p3 to q3 is one load of eight bytes for eight columns, and the decisions for every column are made together with masks. Each
/// calculation fits in 16 bit lanes. The results are those of filtering the columns one by one.
/// </summary>
internal static class H264DeblockHorizontal
{
    /// <summary>The columns filtered at a time.</summary>
    private const int Lanes = 8;

    /// <summary>The segments along an edge of a macroblock.</summary>
    private const int Segments = 4;

    /// <summary>The boundary strength that selects the strong filter.</summary>
    private const int StrongStrength = 4;

    /// <summary>The shift that multiplies a difference by four in the filter step.</summary>
    private const int DifferenceShift = 2;

    /// <summary>The shift of the filter step.</summary>
    private const int DeltaShift = 3;

    /// <summary>The rounding of the filter step.</summary>
    private const short DeltaRound = 4;

    /// <summary>The rounding of the strong filter's four tap averages.</summary>
    private const short QuarterRound = 2;

    /// <summary>The shift that divides alpha by four.</summary>
    private const int AlphaQuarterShift = 2;

    /// <summary>The slack added to the quarter of alpha when choosing the long smoothing.</summary>
    private const short StrongSlack = 2;

    /// <summary>The weight of the third sample in the longest strong smoothing.</summary>
    private const short ThirdWeight = 3;

    /// <summary>The largest sample value.</summary>
    private const short MaxSample = 255;

    /// <summary>The rows from q0 up to p1.</summary>
    private const int RowsToP1 = 2;

    /// <summary>The rows from q0 up to p2.</summary>
    private const int RowsToP2 = 3;

    /// <summary>The rows from q0 up to p3, the farthest sample the filters read on the near side of the edge.</summary>
    private const int RowsToP3 = 4;

    /// <summary>The rows from q0 down to q2.</summary>
    private const int RowsToQ2 = 2;

    /// <summary>The rows from q0 down to q3, the farthest sample the filters read on the far side of the edge.</summary>
    private const int RowsToQ3 = 3;

    /// <summary>The shift that divides by four.</summary>
    private const int QuarterShift = 2;

    /// <summary>The columns in a segment of a luma edge.</summary>
    private const int LumaColumnsPerSegment = 4;

    /// <summary>The columns of a luma edge.</summary>
    private const int LumaColumns = 16;

    /// <summary>The columns of a chroma edge.</summary>
    private const int ChromaColumns = 8;

    /// <summary>Tells whether an edge can be filtered here: vectors are available and its segments are either all strong or all normal.</summary>
    /// <param name="strengths">The boundary strengths of the four segments.</param>
    /// <returns><see langword="true"/> when <see cref="Filter"/> can be used.</returns>
    internal static bool Handles(ReadOnlySpan<int> strengths)
    {
        if (!Vector128.IsHardwareAccelerated)
        {
            return false;
        }

        var strong = 0;
        for (var segment = 0; segment < Segments; segment++)
        {
            strong += strengths[segment] == StrongStrength ? 1 : 0;
        }

        return strong is 0 or Segments;
    }

    /// <summary>Filters one horizontal edge of a macroblock in one plane.</summary>
    /// <param name="plane">The plane.</param>
    /// <param name="start">The index of q0 in the first column of the edge.</param>
    /// <param name="stride">The samples in a row of the plane.</param>
    /// <param name="strengths">The boundary strengths of the four segments, which <see cref="Handles"/> accepted.</param>
    /// <param name="limits">The thresholds.</param>
    /// <param name="chroma">Whether the plane is chroma: the edge is eight columns and only p0 and q0 change.</param>
    /// <exception cref="InvalidDataException">The edge reaches outside the plane.</exception>
    internal static void Filter(byte[] plane, int start, int stride, ReadOnlySpan<int> strengths, H264EdgeLimits limits, bool chroma)
    {
        if (limits.Alpha == 0 || limits.Beta == 0)
        {
            return;
        }

        var columns = chroma ? ChromaColumns : LumaColumns;
        if (start < RowsToP3 * stride || start + (RowsToQ3 * stride) + columns > plane.Length)
        {
            throw new InvalidDataException("An H.264 edge lies outside its picture.");
        }

        ref var origin = ref MemoryMarshal.GetArrayDataReference(plane);
        for (var group = 0; group < columns / Lanes; group++)
        {
            FilterGroup(ref origin, start + (group * Lanes), stride, new(strengths, group, limits, chroma));
        }
    }

    /// <summary>Filters eight columns of an edge, or of an edge laid out like one: the samples across the edge a row apart.</summary>
    /// <param name="origin">The first sample of the array that holds the samples.</param>
    /// <param name="at">The index of q0 in the first of the eight columns.</param>
    /// <param name="stride">The distance between the samples across the edge.</param>
    /// <param name="group">The strengths of the edge's segments, which group of eight columns this is, the thresholds, and whether the plane is chroma.</param>
    internal static void FilterGroup(ref byte origin, int at, int stride, H264EdgeGroup group)
    {
        var limits = group.Limits;
        var thresholds = new Thresholds(Vector128.Create((short)limits.Alpha), Vector128.Create((short)limits.Beta));
        if (group.Strengths[0] == StrongStrength)
        {
            Strong(ref origin, at, stride, thresholds, group.Chroma);
            return;
        }

        var (active, limitZero) = group.Chroma ? ChromaLanes(group.Strengths, limits.IndexA) : LumaLanes(group.Strengths, group.Index, limits.IndexA);
        Normal(ref origin, at, stride, thresholds, active, limitZero, group.Chroma);
    }

    /// <summary>Works out, for the eight columns of a luma group, whether their segment is filtered and its tc0: each of the two segments covers four columns.</summary>
    /// <param name="strengths">The boundary strengths of the four segments.</param>
    /// <param name="group">Which group of eight columns.</param>
    /// <param name="indexA">The index into the tc0 table.</param>
    /// <returns>-1 in each column whose segment has a non-zero strength, and the tc0 of each column.</returns>
    private static LaneLimits LumaLanes(ReadOnlySpan<int> strengths, int group, int indexA)
    {
        var first = group * (Lanes / LumaColumnsPerSegment);
        var strengthA = strengths[first];
        var strengthB = strengths[first + 1];
        var activeA = ActiveOf(strengthA);
        var activeB = ActiveOf(strengthB);
        var zeroA = TcOf(strengthA, indexA);
        var zeroB = TcOf(strengthB, indexA);
        return new(
            Vector128.Create(activeA, activeA, activeA, activeA, activeB, activeB, activeB, activeB),
            Vector128.Create(zeroA, zeroA, zeroA, zeroA, zeroB, zeroB, zeroB, zeroB));
    }

    /// <summary>Works out, for the eight columns of a chroma edge, whether their segment is filtered and its tc0: each of the four segments covers two columns.</summary>
    /// <param name="strengths">The boundary strengths of the four segments.</param>
    /// <param name="indexA">The index into the tc0 table.</param>
    /// <returns>-1 in each column whose segment has a non-zero strength, and the tc0 of each column.</returns>
    private static LaneLimits ChromaLanes(ReadOnlySpan<int> strengths, int indexA)
    {
        var activeA = ActiveOf(strengths[0]);
        var activeB = ActiveOf(strengths[1]);
        var activeC = ActiveOf(strengths[2]);
        var activeD = ActiveOf(strengths[3]);
        var zeroA = TcOf(strengths[0], indexA);
        var zeroB = TcOf(strengths[1], indexA);
        var zeroC = TcOf(strengths[2], indexA);
        var zeroD = TcOf(strengths[3], indexA);
        return new(
            Vector128.Create(activeA, activeA, activeB, activeB, activeC, activeC, activeD, activeD),
            Vector128.Create(zeroA, zeroA, zeroB, zeroB, zeroC, zeroC, zeroD, zeroD));
    }

    /// <summary>Gives -1 for a segment that is filtered, otherwise 0.</summary>
    /// <param name="strength">The boundary strength.</param>
    /// <returns>The lane mask value.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static short ActiveOf(int strength) => (short)(strength > 0 ? -1 : 0);

    /// <summary>Gives tc0 for a segment that is filtered, otherwise 0.</summary>
    /// <param name="strength">The boundary strength.</param>
    /// <param name="indexA">The index into the tc0 table.</param>
    /// <returns>The tc0.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static short TcOf(int strength, int indexA) =>
        strength > 0 ? H264DeblockTables.TcZero[((strength - 1) * H264DeblockTables.IndexCount) + indexA] : (short)0;

    /// <summary>Filters eight columns with a boundary strength below 4 (clause 8.7.2.3).</summary>
    /// <param name="origin">The first sample of the plane.</param>
    /// <param name="at">The index of q0 in the first of the eight columns.</param>
    /// <param name="stride">The samples in a row.</param>
    /// <param name="limits">Alpha and beta in every lane.</param>
    /// <param name="active">Which columns are filtered.</param>
    /// <param name="limitZero">The tc0 of each column.</param>
    /// <param name="chroma">Whether the plane is chroma.</param>
    private static void Normal(ref byte origin, int at, int stride, Thresholds limits, Vector128<short> active, Vector128<short> limitZero, bool chroma)
    {
        var p1 = Load(ref origin, at - (RowsToP1 * stride));
        var p0 = Load(ref origin, at - stride);
        var q0 = Load(ref origin, at);
        var q1 = Load(ref origin, at + stride);
        var mask = Vector128.LessThan(Vector128.Abs(p0 - q0), limits.Alpha)
            & Vector128.LessThan(Vector128.Abs(p1 - p0), limits.Beta)
            & Vector128.LessThan(Vector128.Abs(q1 - q0), limits.Beta)
            & active;
        var tc0 = limitZero;
        var tc = tc0 - Vector128<short>.AllBitsSet;
        var p2 = Vector128<short>.Zero;
        var q2 = Vector128<short>.Zero;
        var smoothP = Vector128<short>.Zero;
        var smoothQ = Vector128<short>.Zero;
        if (!chroma)
        {
            p2 = Load(ref origin, at - (RowsToP2 * stride));
            q2 = Load(ref origin, at + (RowsToQ2 * stride));
            smoothP = Vector128.LessThan(Vector128.Abs(p2 - p0), limits.Beta) & mask;
            smoothQ = Vector128.LessThan(Vector128.Abs(q2 - q0), limits.Beta) & mask;
            tc = tc0 - smoothP - smoothQ;
        }

        var step = Vector128.ShiftRightArithmetic(Vector128.ShiftLeft(q0 - p0, DifferenceShift) + (p1 - q1) + Vector128.Create(DeltaRound), DeltaShift);
        var delta = Vector128.Min(Vector128.Max(step, Vector128<short>.Zero - tc), tc);
        Store(ref origin, at - stride, Vector128.ConditionalSelect(mask, Clip(p0 + delta), p0));
        Store(ref origin, at, Vector128.ConditionalSelect(mask, Clip(q0 - delta), q0));
        if (chroma)
        {
            return;
        }

        var average = Vector128.ShiftRightArithmetic(p0 + q0 + Vector128<short>.One, 1);
        var low = Vector128<short>.Zero - tc0;
        var nextP = p1 + Vector128.Min(Vector128.Max(Vector128.ShiftRightArithmetic(p2 + average - Vector128.ShiftLeft(p1, 1), 1), low), tc0);
        var nextQ = q1 + Vector128.Min(Vector128.Max(Vector128.ShiftRightArithmetic(q2 + average - Vector128.ShiftLeft(q1, 1), 1), low), tc0);
        Store(ref origin, at - (RowsToP1 * stride), Vector128.ConditionalSelect(smoothP, nextP, p1));
        Store(ref origin, at + stride, Vector128.ConditionalSelect(smoothQ, nextQ, q1));
    }

    /// <summary>Filters eight columns with a boundary strength of 4 (clause 8.7.2.4).</summary>
    /// <param name="origin">The first sample of the plane.</param>
    /// <param name="at">The index of q0 in the first of the eight columns.</param>
    /// <param name="stride">The samples in a row.</param>
    /// <param name="limits">Alpha and beta in every lane.</param>
    /// <param name="chroma">Whether the plane is chroma.</param>
    private static void Strong(ref byte origin, int at, int stride, Thresholds limits, bool chroma)
    {
        var p1 = Load(ref origin, at - (RowsToP1 * stride));
        var p0 = Load(ref origin, at - stride);
        var q0 = Load(ref origin, at);
        var q1 = Load(ref origin, at + stride);
        var mask = Vector128.LessThan(Vector128.Abs(p0 - q0), limits.Alpha)
            & Vector128.LessThan(Vector128.Abs(p1 - p0), limits.Beta)
            & Vector128.LessThan(Vector128.Abs(q1 - q0), limits.Beta);
        var quarter = Vector128.Create(QuarterRound);
        var shortP0 = Vector128.ShiftRightArithmetic(Vector128.ShiftLeft(p1, 1) + p0 + q1 + quarter, QuarterShift);
        var shortQ0 = Vector128.ShiftRightArithmetic(Vector128.ShiftLeft(q1, 1) + q0 + p1 + quarter, QuarterShift);
        if (chroma)
        {
            Store(ref origin, at - stride, Vector128.ConditionalSelect(mask, shortP0, p0));
            Store(ref origin, at, Vector128.ConditionalSelect(mask, shortQ0, q0));
            return;
        }

        var p2 = Load(ref origin, at - (RowsToP2 * stride));
        var p3 = Load(ref origin, at - (RowsToP3 * stride));
        var q2 = Load(ref origin, at + (RowsToQ2 * stride));
        var q3 = Load(ref origin, at + (RowsToQ3 * stride));
        var close = Vector128.LessThan(Vector128.Abs(p0 - q0), Vector128.ShiftRightArithmetic(limits.Alpha, AlphaQuarterShift) + Vector128.Create(StrongSlack));
        var wideP = close & Vector128.LessThan(Vector128.Abs(p2 - p0), limits.Beta) & mask;
        var wideQ = close & Vector128.LessThan(Vector128.Abs(q2 - q0), limits.Beta) & mask;
        var round = Vector128.Create(DeltaRound);
        var third = Vector128.Create(ThirdWeight);
        var longP0 = Vector128.ShiftRightArithmetic(p2 + Vector128.ShiftLeft(p1 + p0 + q0, 1) + q1 + round, DeltaShift);
        var longP1 = Vector128.ShiftRightArithmetic(p2 + p1 + p0 + q0 + quarter, QuarterShift);
        var longP2 = Vector128.ShiftRightArithmetic(Vector128.ShiftLeft(p3, 1) + (p2 * third) + p1 + p0 + q0 + round, DeltaShift);
        var longQ0 = Vector128.ShiftRightArithmetic(q2 + Vector128.ShiftLeft(q1 + q0 + p0, 1) + p1 + round, DeltaShift);
        var longQ1 = Vector128.ShiftRightArithmetic(q2 + q1 + q0 + p0 + quarter, QuarterShift);
        var longQ2 = Vector128.ShiftRightArithmetic(Vector128.ShiftLeft(q3, 1) + (q2 * third) + q1 + q0 + p0 + round, DeltaShift);
        Store(ref origin, at - stride, Vector128.ConditionalSelect(mask, Vector128.ConditionalSelect(wideP, longP0, shortP0), p0));
        Store(ref origin, at - (RowsToP1 * stride), Vector128.ConditionalSelect(wideP, longP1, p1));
        Store(ref origin, at - (RowsToP2 * stride), Vector128.ConditionalSelect(wideP, longP2, p2));
        Store(ref origin, at, Vector128.ConditionalSelect(mask, Vector128.ConditionalSelect(wideQ, longQ0, shortQ0), q0));
        Store(ref origin, at + stride, Vector128.ConditionalSelect(wideQ, longQ1, q1));
        Store(ref origin, at + (RowsToQ2 * stride), Vector128.ConditionalSelect(wideQ, longQ2, q2));
    }

    /// <summary>Limits eight values to the range of a sample.</summary>
    /// <param name="values">The values.</param>
    /// <returns>The values clamped to 0 to 255.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector128<short> Clip(Vector128<short> values) => Vector128.Min(Vector128.Max(values, Vector128<short>.Zero), Vector128.Create(MaxSample));

    /// <summary>Loads eight samples as 16 bit values.</summary>
    /// <param name="origin">The first sample of the plane.</param>
    /// <param name="index">The index of the first of the eight.</param>
    /// <returns>The values.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector128<short> Load(ref byte origin, int index) =>
        Vector128.WidenLower(Vector128.CreateScalarUnsafe(Unsafe.ReadUnaligned<ulong>(ref Unsafe.Add(ref origin, index))).AsByte()).AsInt16();

    /// <summary>Stores eight values, each between 0 and 255, as bytes.</summary>
    /// <param name="origin">The first sample of the plane.</param>
    /// <param name="index">The index of the first of the eight.</param>
    /// <param name="values">The values.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Store(ref byte origin, int index, Vector128<short> values) =>
        Unsafe.WriteUnaligned(ref Unsafe.Add(ref origin, index), Vector128.Narrow(values.AsUInt16(), values.AsUInt16()).AsUInt64().ToScalar());

    /// <summary>The thresholds of an edge in every lane.</summary>
    /// <param name="Alpha">Alpha.</param>
    /// <param name="Beta">Beta.</param>
    private readonly record struct Thresholds(Vector128<short> Alpha, Vector128<short> Beta);

    /// <summary>What the normal filter needs to know about each of eight columns.</summary>
    /// <param name="Active">-1 in a column whose segment is filtered, otherwise 0.</param>
    /// <param name="LimitZero">The tc0 of each column.</param>
    private readonly record struct LaneLimits(Vector128<short> Active, Vector128<short> LimitZero);
}
