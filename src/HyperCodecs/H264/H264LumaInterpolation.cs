// Copyright (c) 2026 Glenn Watson. All rights reserved.
// Glenn Watson licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace HyperCodecs.H264;

/// <summary>
/// Predicts a luma block from a reference plane with the quarter-sample interpolation of clause 8.4.2.2.1 of ITU-T H.264. It works eight samples at a
/// time in 16 bit lanes: a six-tap sum of 8 bit samples lies between -2550 and 10710, so it fits. The position at the centre of four samples filters the
/// rows first and then the columns of those sums, and the second stage is done in 32 bit lanes. A block that reaches outside the plane is predicted from a
/// small copy of its reference samples in which the edge samples are repeated.
/// </summary>
internal static class H264LumaInterpolation
{
    /// <summary>The samples predicted at a time.</summary>
    private const int Lanes = 8;

    /// <summary>The samples before a block that the filter reads.</summary>
    private const int Before = 2;

    /// <summary>The samples after a block that the filter reads.</summary>
    private const int After = 3;

    /// <summary>The largest block, in samples along a side.</summary>
    private const int MaxBlock = 16;

    /// <summary>The samples in a row of the copy of the reference samples of a block that reaches outside the plane.</summary>
    private const int WindowStride = 32;

    /// <summary>The rows of that copy: the largest block and the margins of the filter.</summary>
    private const int WindowRows = MaxBlock + Before + After;

    /// <summary>The sums kept for the centre position: a row of eight for every row of the largest block and the margins.</summary>
    private const int CentreSize = WindowRows * Lanes;

    /// <summary>The offset of the second row of sums from the first.</summary>
    private const int SecondRow = Lanes;

    /// <summary>The offset of the third row of sums from the first.</summary>
    private const int ThirdRow = Lanes * 2;

    /// <summary>The offset of the fourth row of sums from the first.</summary>
    private const int FourthRow = Lanes * 3;

    /// <summary>The offset of the fifth row of sums from the first.</summary>
    private const int FifthRow = Lanes * 4;

    /// <summary>The offset of the sixth row of sums from the first.</summary>
    private const int SixthRow = Lanes * 5;

    /// <summary>The weight of the samples next to the centre taps of the six-tap filter.</summary>
    private const short SideWeight = 5;

    /// <summary>The weight of the two centre taps of the six-tap filter.</summary>
    private const short CentreWeight = 20;

    /// <summary>The rounding added before a single filtering step is shifted.</summary>
    private const short Round = 16;

    /// <summary>The shift after a single filtering step.</summary>
    private const int Shift = 5;

    /// <summary>The rounding added before the two-step centre sample is shifted.</summary>
    private const int CentreRound = 512;

    /// <summary>The shift after the two-step centre sample.</summary>
    private const int CentreShift = 10;

    /// <summary>The shift that multiplies by four.</summary>
    private const int TimesFour = 2;

    /// <summary>The shift that multiplies by sixteen.</summary>
    private const int TimesSixteen = 4;

    /// <summary>The largest sample value.</summary>
    private const short MaxSample = 255;

    /// <summary>The shift of a luma motion vector to whole samples.</summary>
    private const int FractionBits = 2;

    /// <summary>The mask of the fractional part of a luma motion vector.</summary>
    private const int FractionMask = 3;

    /// <summary>The shift of the vertical fraction inside the combined fraction code.</summary>
    private const int FractionYShift = 2;

    /// <summary>The entries per quarter-sample position in <see cref="Mix"/>.</summary>
    private const int PairSize = 2;

    /// <summary>The kind of sample that is the whole sample itself.</summary>
    private const int Whole = 0;

    /// <summary>The kind of sample that is the whole sample to the right.</summary>
    private const int WholeRight = 1;

    /// <summary>The kind of sample that is the whole sample below.</summary>
    private const int WholeBelow = 2;

    /// <summary>The kind of sample that is the half sample to the right.</summary>
    private const int HalfRight = 3;

    /// <summary>The kind of sample that is the half sample below.</summary>
    private const int HalfBelow = 4;

    /// <summary>The kind of sample that is the half sample at the centre.</summary>
    private const int HalfCentre = 5;

    /// <summary>The kind of sample that is the half sample below the whole sample to the right.</summary>
    private const int HalfBelowRight = 6;

    /// <summary>The kind of sample that is the half sample to the right of the whole sample below.</summary>
    private const int HalfRightBelow = 7;

    /// <summary>Gets the pair of samples whose average each quarter-sample position is (Table 8-12); a pair of equal kinds means the sample itself.</summary>
    private static ReadOnlySpan<byte> Mix =>
    [
        0x00, 0x00, 0x00, 0x03, 0x03, 0x03, 0x01, 0x03, 0x00, 0x04, 0x03, 0x04, 0x03, 0x05, 0x03, 0x06,
        0x04, 0x04, 0x04, 0x05, 0x05, 0x05, 0x05, 0x06, 0x02, 0x04, 0x04, 0x07, 0x05, 0x07, 0x06, 0x07,
    ];

    /// <summary>Predicts a block.</summary>
    /// <param name="reference">The reference frame.</param>
    /// <param name="target">Where the predicted samples go: a plane, its stride and the place of the block's first sample.</param>
    /// <param name="block">The block's position and size in the reference frame: 4, 8 or 16 samples along each side.</param>
    /// <param name="vector">The motion vector in quarter samples.</param>
    internal static void Predict(H264Picture reference, H264PlaneTarget target, BlockRect block, MotionVector vector)
    {
        var fraction = ((vector.Y & FractionMask) << FractionYShift) | (vector.X & FractionMask);
        var source = new BlockRect(block.X + (vector.X >> FractionBits), block.Y + (vector.Y >> FractionBits), block.Width, block.Height);
        var first = Mix[fraction * PairSize];
        var second = Mix[(fraction * PairSize) + 1];
        var planeHeight = reference.MbHeight * H264Picture.LumaSize;
        if (IsInside(source, reference.LumaStride, planeHeight, first != Whole || second != Whole))
        {
            PredictFrom(ref MemoryMarshal.GetArrayDataReference(reference.Luma), reference.LumaStride, target, source, first, second);
            return;
        }

        PredictOutside(reference.Luma, reference.LumaStride, planeHeight, target, source, first, second);
    }

    /// <summary>Tells whether the reference samples a block needs, with the margins of the filter when it filters, are all inside the plane.</summary>
    /// <param name="source">The block's first sample in the reference plane and its size.</param>
    /// <param name="stride">The samples in a row of the plane.</param>
    /// <param name="planeHeight">The rows in the plane.</param>
    /// <param name="filtered">Whether the position is filtered. A filtered block is read eight samples at a time, so a narrow block is read as if it were eight wide.</param>
    /// <returns><see langword="true"/> when they are.</returns>
    private static bool IsInside(BlockRect source, int stride, int planeHeight, bool filtered) => filtered
        ? source.X >= Before && source.Y >= Before && source.X + Math.Max(source.Width, Lanes) + After <= stride && source.Y + source.Height + After <= planeHeight
        : source.X >= 0 && source.Y >= 0 && source.X + source.Width <= stride && source.Y + source.Height <= planeHeight;

    /// <summary>Predicts a block whose reference samples are all inside the plane that <paramref name="origin"/> starts.</summary>
    /// <param name="origin">The first sample of the plane.</param>
    /// <param name="stride">The samples in a row of the plane.</param>
    /// <param name="target">Where the samples go.</param>
    /// <param name="source">The block's first sample in the plane and its size.</param>
    /// <param name="first">The first kind of sample whose average the position is.</param>
    /// <param name="second">The second kind; equal to the first for a position that is one kind.</param>
    private static void PredictFrom(ref byte origin, int stride, H264PlaneTarget target, BlockRect source, int first, int second)
    {
        if (first == Whole && second == Whole)
        {
            CopyBlock(ref origin, stride, target, source);
            return;
        }

        if (first == HalfCentre || second == HalfCentre)
        {
            PredictCentre(ref origin, stride, target, source, first, second);
            return;
        }

        Strips(ref origin, stride, target, source, first, second, default);
    }

    /// <summary>Predicts a block of a position that is, or averages with, the centre half sample, which needs room for the sums of the first stage.</summary>
    /// <param name="origin">The first sample of the plane.</param>
    /// <param name="stride">The samples in a row of the plane.</param>
    /// <param name="target">Where the samples go.</param>
    /// <param name="source">The block's first sample in the plane and its size.</param>
    /// <param name="first">The first kind of sample whose average the position is.</param>
    /// <param name="second">The second kind.</param>
    [SkipLocalsInit]
    private static void PredictCentre(ref byte origin, int stride, H264PlaneTarget target, BlockRect source, int first, int second)
    {
        Span<short> centre = stackalloc short[CentreSize];
        Strips(ref origin, stride, target, source, first, second, centre);
    }

    /// <summary>Predicts a block that reaches outside the plane from a copy of its reference samples in which the samples at the edge are repeated.</summary>
    /// <param name="plane">The reference plane.</param>
    /// <param name="stride">The samples in a row of the plane, which is also its width.</param>
    /// <param name="planeHeight">The rows in the plane.</param>
    /// <param name="target">Where the samples go.</param>
    /// <param name="source">The block's first sample in the plane and its size.</param>
    /// <param name="first">The first kind of sample whose average the position is.</param>
    /// <param name="second">The second kind.</param>
    [SkipLocalsInit]
    private static void PredictOutside(byte[] plane, int stride, int planeHeight, H264PlaneTarget target, BlockRect source, int first, int second)
    {
        Span<byte> window = stackalloc byte[WindowStride * WindowRows];
        FillWindow(plane, stride, planeHeight, source, window);
        PredictFrom(ref MemoryMarshal.GetReference(window), WindowStride, target, new(Before, Before, source.Width, source.Height), first, second);
    }

    /// <summary>Copies the reference samples of a block and the margins of the filter, repeating the samples at the edge of the plane for places outside it.</summary>
    /// <param name="plane">The reference plane.</param>
    /// <param name="stride">The samples in a row of the plane, which is also its width.</param>
    /// <param name="planeHeight">The rows in the plane.</param>
    /// <param name="source">The block's first sample in the plane and its size.</param>
    /// <param name="window">Receives the samples, <see cref="WindowStride"/> to a row, the block's first sample at column and row <see cref="Before"/>.</param>
    private static void FillWindow(byte[] plane, int stride, int planeHeight, BlockRect source, Span<byte> window)
    {
        var columns = source.Width + Before + After;
        var left = source.X - Before;
        for (var row = 0; row < source.Height + Before + After; row++)
        {
            var line = plane.AsSpan(Math.Clamp(source.Y - Before + row, 0, planeHeight - 1) * stride, stride);
            var destination = window.Slice(row * WindowStride, columns);
            if (left >= 0 && left + columns <= stride)
            {
                line.Slice(left, columns).CopyTo(destination);
                continue;
            }

            for (var column = 0; column < columns; column++)
            {
                destination[column] = line[Math.Clamp(left + column, 0, stride - 1)];
            }
        }
    }

    /// <summary>Copies a block of whole samples.</summary>
    /// <param name="origin">The first sample of the plane.</param>
    /// <param name="stride">The samples in a row of the plane.</param>
    /// <param name="target">Where the samples go.</param>
    /// <param name="source">The block's first sample in the plane and its size.</param>
    private static void CopyBlock(ref byte origin, int stride, H264PlaneTarget target, BlockRect source)
    {
        for (var row = 0; row < source.Height; row++)
        {
            ref var from = ref Unsafe.Add(ref origin, ((source.Y + row) * stride) + source.X);
            ref var to = ref MemoryMarshal.GetReference(target.Plane.AsSpan(((target.Y + row) * target.Stride) + target.X, source.Width));
            switch (source.Width)
            {
                case MaxBlock:
                {
                    Vector128.LoadUnsafe(ref from).StoreUnsafe(ref to);
                    break;
                }

                case Lanes:
                {
                    Unsafe.WriteUnaligned(ref to, Unsafe.ReadUnaligned<ulong>(ref from));
                    break;
                }

                default:
                {
                    Unsafe.WriteUnaligned(ref to, Unsafe.ReadUnaligned<uint>(ref from));
                    break;
                }
            }
        }
    }

    /// <summary>Predicts a block eight columns at a time.</summary>
    /// <param name="origin">The first sample of the plane.</param>
    /// <param name="stride">The samples in a row of the plane.</param>
    /// <param name="target">Where the samples go.</param>
    /// <param name="source">The block's first sample in the plane and its size.</param>
    /// <param name="first">The first kind of sample whose average the position is.</param>
    /// <param name="second">The second kind; equal to the first for a position that is one kind.</param>
    /// <param name="centre">Room for the sums of the first stage of the centre position, or empty when no kind is the centre.</param>
    private static void Strips(ref byte origin, int stride, H264PlaneTarget target, BlockRect source, int first, int second, Span<short> centre)
    {
        var stored = Math.Min(Lanes, source.Width);
        for (var column = 0; column < source.Width; column += Lanes)
        {
            if (!centre.IsEmpty)
            {
                FillCentre(ref origin, stride, source with { X = source.X + column }, centre);
            }

            for (var row = 0; row < source.Height; row++)
            {
                var at = ((source.Y + row) * stride) + source.X + column;
                var value = Kind(ref origin, stride, at, first, centre, row);
                if (second != first)
                {
                    value = Vector128.ShiftRightLogical(value + Kind(ref origin, stride, at, second, centre, row) + Vector128<short>.One, 1);
                }

                var bytes = Vector128.Narrow(value.AsUInt16(), value.AsUInt16());
                ref var to = ref MemoryMarshal.GetReference(target.Plane.AsSpan(((target.Y + row) * target.Stride) + target.X + column, stored));
                if (stored == Lanes)
                {
                    Unsafe.WriteUnaligned(ref to, bytes.AsUInt64().ToScalar());
                    continue;
                }

                Unsafe.WriteUnaligned(ref to, bytes.AsUInt32().ToScalar());
            }
        }
    }

    /// <summary>Gets eight side by side samples of one kind.</summary>
    /// <param name="origin">The first sample of the plane.</param>
    /// <param name="stride">The samples in a row.</param>
    /// <param name="at">The index of the whole sample the kind is relative to.</param>
    /// <param name="kind">Which sample.</param>
    /// <param name="centre">The sums of the first stage of the centre position, when the kind is the centre.</param>
    /// <param name="row">The row in the block, which says where in the sums the centre position is.</param>
    /// <returns>The eight values, each between 0 and 255.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector128<short> Kind(ref byte origin, int stride, int at, int kind, ReadOnlySpan<short> centre, int row) => kind switch
    {
        WholeRight => Load(ref origin, at + 1),
        WholeBelow => Load(ref origin, at + stride),
        HalfRight => Filter(ref origin, at, 1),
        HalfBelow => Filter(ref origin, at, stride),
        HalfCentre => Centre(centre, row),
        HalfBelowRight => Filter(ref origin, at + 1, stride),
        HalfRightBelow => Filter(ref origin, at + stride, 1),
        _ => Load(ref origin, at),
    };

    /// <summary>Loads eight samples as 16 bit values.</summary>
    /// <param name="origin">The first sample of the plane.</param>
    /// <param name="index">The index of the first of the eight.</param>
    /// <returns>The values.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector128<short> Load(ref byte origin, int index) =>
        Vector128.WidenLower(Vector128.CreateScalarUnsafe(Unsafe.ReadUnaligned<ulong>(ref Unsafe.Add(ref origin, index))).AsByte()).AsInt16();

    /// <summary>Filters six samples along a row or a column without rounding.</summary>
    /// <param name="origin">The first sample of the plane.</param>
    /// <param name="at">The index of the third sample of the six, for the first of the eight outputs.</param>
    /// <param name="step">The distance between the six samples: 1 along a row, the stride down a column.</param>
    /// <returns>The eight sums, each scaled by 32.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector128<short> Taps(ref byte origin, int at, int step)
    {
        var outer = Load(ref origin, at - (Before * step)) + Load(ref origin, at + (After * step));
        var middle = Load(ref origin, at - step) + Load(ref origin, at + (Before * step));
        var inner = Load(ref origin, at) + Load(ref origin, at + step);
        return outer - (Vector128.Create(SideWeight) * middle) + (Vector128.Create(CentreWeight) * inner);
    }

    /// <summary>Filters six samples along a row or a column, giving the half sample between the third and fourth.</summary>
    /// <param name="origin">The first sample of the plane.</param>
    /// <param name="at">The index of the third sample of the six, for the first of the eight outputs.</param>
    /// <param name="step">The distance between the six samples: 1 along a row, the stride down a column.</param>
    /// <returns>The eight half samples, rounded and limited to 0 to 255.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector128<short> Filter(ref byte origin, int at, int step)
    {
        var rounded = Vector128.ShiftRightArithmetic(Taps(ref origin, at, step) + Vector128.Create(Round), Shift);
        return Vector128.Min(Vector128.Max(rounded, Vector128<short>.Zero), Vector128.Create(MaxSample));
    }

    /// <summary>Works out the first stage of the centre position for eight columns: the row sums for every row the block and the filter's margins cover.</summary>
    /// <param name="origin">The first sample of the plane.</param>
    /// <param name="stride">The samples in a row of the plane.</param>
    /// <param name="strip">The block's first sample in the plane, at the strip's first column, and the block's size.</param>
    /// <param name="centre">Receives eight sums for each row, the first row being the one the filter reads two rows above the block.</param>
    private static void FillCentre(ref byte origin, int stride, BlockRect strip, Span<short> centre)
    {
        ref var sums = ref MemoryMarshal.GetReference(centre);
        for (var windowRow = 0; windowRow < strip.Height + Before + After; windowRow++)
        {
            var at = ((strip.Y - Before + windowRow) * stride) + strip.X;
            Taps(ref origin, at, 1).StoreUnsafe(ref sums, (nuint)(windowRow * Lanes));
        }
    }

    /// <summary>Gets the centre half sample for eight columns of one row, by filtering the sums of the first stage down their columns.</summary>
    /// <param name="centre">The sums of the first stage.</param>
    /// <param name="row">The row in the block.</param>
    /// <returns>The eight samples, rounded and limited to 0 to 255.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector128<short> Centre(ReadOnlySpan<short> centre, int row)
    {
        ref var start = ref Unsafe.Add(ref MemoryMarshal.GetReference(centre), row * Lanes);
        var a = Vector128.LoadUnsafe(ref start);
        var b = Vector128.LoadUnsafe(ref start, SecondRow);
        var c = Vector128.LoadUnsafe(ref start, ThirdRow);
        var d = Vector128.LoadUnsafe(ref start, FourthRow);
        var e = Vector128.LoadUnsafe(ref start, FifthRow);
        var f = Vector128.LoadUnsafe(ref start, SixthRow);
        var low = Vertical(Vector128.WidenLower(a), Vector128.WidenLower(b), Vector128.WidenLower(c), Vector128.WidenLower(d), Vector128.WidenLower(e), Vector128.WidenLower(f));
        var high = Vertical(Vector128.WidenUpper(a), Vector128.WidenUpper(b), Vector128.WidenUpper(c), Vector128.WidenUpper(d), Vector128.WidenUpper(e), Vector128.WidenUpper(f));
        return Vector128.Min(Vector128.Max(Vector128.Narrow(low, high), Vector128<short>.Zero), Vector128.Create(MaxSample));
    }

    /// <summary>Filters six rows of first stage sums for four columns and rounds the result.</summary>
    /// <param name="a">The first row.</param>
    /// <param name="b">The second row.</param>
    /// <param name="c">The third row.</param>
    /// <param name="d">The fourth row.</param>
    /// <param name="e">The fifth row.</param>
    /// <param name="f">The sixth row.</param>
    /// <returns>The four values, before they are limited to the range of a sample.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector128<int> Vertical(Vector128<int> a, Vector128<int> b, Vector128<int> c, Vector128<int> d, Vector128<int> e, Vector128<int> f)
    {
        var middle = b + e;
        var inner = c + d;
        var sum = a + f - (middle + Vector128.ShiftLeft(middle, TimesFour)) + Vector128.ShiftLeft(inner, TimesFour) + Vector128.ShiftLeft(inner, TimesSixteen);
        return Vector128.ShiftRightArithmetic(sum + Vector128.Create(CentreRound), CentreShift);
    }
}
