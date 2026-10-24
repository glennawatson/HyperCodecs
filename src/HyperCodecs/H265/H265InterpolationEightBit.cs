// Copyright (c) 2026 Glenn Watson. All rights reserved.
// Glenn Watson licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace HyperCodecs.H265;

/// <summary>
/// The interpolation filters for 8 bit video, in 16 bit lanes eight samples at a time. The sum of a filter over 8 bit samples lies between -6120 and 22440, so
/// the first stage needs no more than 16 bits, and the second stage over the first stage's results is done in 32 bits. The kernels read whole vectors, so a
/// row may be read up to seven samples past its end; the caller makes sure that lies inside the array the samples are in.
/// </summary>
internal static class H265InterpolationEightBit
{
    /// <summary>The samples in a vector.</summary>
    internal const int Lanes = 8;

    /// <summary>The samples in the half of a vector that a block four samples wide uses.</summary>
    private const int HalfLanes = 4;

    /// <summary>Rounds a width up to a whole number of vectors.</summary>
    /// <param name="width">The width in samples.</param>
    /// <returns>The width the kernels read.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static int Padded(int width) => (width + Lanes - 1) & ~(Lanes - 1);

    /// <summary>Repeats each tap of a filter in every lane of a vector.</summary>
    /// <param name="taps">The taps.</param>
    /// <param name="vectors">Receives a vector for each tap; at least as long as <paramref name="taps"/>.</param>
    internal static void Broadcast(ReadOnlySpan<sbyte> taps, Span<Vector128<short>> vectors)
    {
        for (var k = 0; k < taps.Length; k++)
        {
            vectors[k] = Vector128.Create((short)taps[k]);
        }
    }

    /// <summary>Filters along rows of 8 bit samples.</summary>
    /// <param name="reference">The samples.</param>
    /// <param name="area">The row length of the samples, and the width and number of the rows to filter.</param>
    /// <param name="first">The sample row to start at.</param>
    /// <param name="taps">The filter taps.</param>
    /// <param name="destination">Receives the sums, row after row.</param>
    /// <exception cref="InvalidOperationException">The filter would read outside the samples.</exception>
    internal static void FilterRows(ReadOnlySpan<ushort> reference, in H265FilterArea area, int first, ReadOnlySpan<Vector128<short>> taps, Span<short> destination)
    {
        var width = area.Width;
        CheckReadable(reference.Length, ((first + area.Height - 1) * area.Stride) + Padded(width) - 1 + (taps.Length - 1));
        ref var origin = ref MemoryMarshal.GetReference(reference);
        ref var target = ref MemoryMarshal.GetReference(destination);
        Span<Vector<short>> wideStore = stackalloc Vector<short>[H265InterpolationFilters.LumaTaps];
        var wide = H265InterpolationWide.ShortTaps(taps, wideStore);
        for (var y = 0; y < area.Height; y++)
        {
            var row = (first + y) * area.Stride;
            var x = H265InterpolationWide.Sum(ref origin, row, 1, width, wide, ref Unsafe.Add(ref target, y * width));
            for (; x + Lanes <= width; x += Lanes)
            {
                Sum(ref origin, row + x, 1, taps).StoreUnsafe(ref target, (nuint)((y * width) + x));
            }

            if (x >= width)
            {
                continue;
            }

            StoreRemainder(Sum(ref origin, row + x, 1, taps), destination.Slice((y * width) + x, width - x));
        }
    }

    /// <summary>Filters down columns of 8 bit samples.</summary>
    /// <param name="reference">The samples, starting at the first sample of the first column to filter.</param>
    /// <param name="stride">The row length of the samples.</param>
    /// <param name="taps">The filter taps.</param>
    /// <param name="destination">Receives the sums, row after row.</param>
    /// <param name="size">The width and height of the block.</param>
    /// <exception cref="InvalidOperationException">The filter would read outside the samples.</exception>
    internal static void FilterColumns(ReadOnlySpan<ushort> reference, int stride, ReadOnlySpan<Vector128<short>> taps, Span<short> destination, BlockRect size)
    {
        var width = size.Width;
        CheckReadable(reference.Length, ((size.Height - 1 + taps.Length - 1) * stride) + Padded(width) - 1);
        ref var origin = ref MemoryMarshal.GetReference(reference);
        ref var target = ref MemoryMarshal.GetReference(destination);
        Span<Vector<short>> wideStore = stackalloc Vector<short>[H265InterpolationFilters.LumaTaps];
        var wide = H265InterpolationWide.ShortTaps(taps, wideStore);
        for (var y = 0; y < size.Height; y++)
        {
            var x = H265InterpolationWide.Sum(ref origin, y * stride, stride, width, wide, ref Unsafe.Add(ref target, y * width));
            for (; x + Lanes <= width; x += Lanes)
            {
                Sum(ref origin, (y * stride) + x, stride, taps).StoreUnsafe(ref target, (nuint)((y * width) + x));
            }

            if (x >= width)
            {
                continue;
            }

            StoreRemainder(Sum(ref origin, (y * stride) + x, stride, taps), destination.Slice((y * width) + x, width - x));
        }
    }

    /// <summary>Filters down columns of the results of the first stage.</summary>
    /// <param name="source">The results, starting with the margin above the block.</param>
    /// <param name="stride">The row length of the results.</param>
    /// <param name="taps">The filter taps.</param>
    /// <param name="destination">Receives the filtered samples, row after row.</param>
    /// <param name="size">The width and height of the block.</param>
    /// <param name="shift">The right shift.</param>
    /// <exception cref="InvalidOperationException">The filter would read outside the results.</exception>
    internal static void FilterIntermediate(ReadOnlySpan<short> source, int stride, ReadOnlySpan<Vector128<short>> taps, Span<short> destination, BlockRect size, int shift)
    {
        var width = size.Width;
        CheckReadable(source.Length, ((size.Height - 1 + taps.Length - 1) * stride) + Padded(width) - 1);
        ref var origin = ref MemoryMarshal.GetReference(source);
        ref var target = ref MemoryMarshal.GetReference(destination);
        Span<Vector<int>> wideStore = stackalloc Vector<int>[H265InterpolationFilters.LumaTaps];
        var wide = H265InterpolationWide.IntTaps(taps, wideStore);
        for (var y = 0; y < size.Height; y++)
        {
            var x = H265InterpolationWide.Intermediate(ref origin, y * stride, stride, width, wide, shift, ref Unsafe.Add(ref target, y * width));
            for (; x + Lanes <= width; x += Lanes)
            {
                Wide(ref origin, (y * stride) + x, stride, taps, shift).StoreUnsafe(ref target, (nuint)((y * width) + x));
            }

            if (x >= width)
            {
                continue;
            }

            StoreRemainder(Wide(ref origin, (y * stride) + x, stride, taps, shift), destination.Slice((y * width) + x, width - x));
        }
    }

    /// <summary>Checks that the last sample a kernel reads is inside the samples it was given.</summary>
    /// <param name="length">The length of the samples.</param>
    /// <param name="last">The index of the last sample read.</param>
    /// <exception cref="InvalidOperationException">The kernel would read outside the samples.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void CheckReadable(int length, int last)
    {
        if (last >= length)
        {
            throw new InvalidOperationException("An interpolation filter would read past its samples.");
        }
    }

    /// <summary>Sums the products of eight columns of samples with the taps, down a column or along a row.</summary>
    /// <param name="origin">The first sample of the array.</param>
    /// <param name="index">The index of the first sample under the first tap, for the first of the eight columns.</param>
    /// <param name="step">The distance between the samples under successive taps: 1 along a row, the row length down a column.</param>
    /// <param name="taps">The taps.</param>
    /// <returns>The eight sums.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector128<short> Sum(ref ushort origin, int index, int step, ReadOnlySpan<Vector128<short>> taps)
    {
        var sum = Vector128<short>.Zero;
        for (var k = 0; k < taps.Length; k++)
        {
            sum += Vector128.LoadUnsafe(ref origin, (nuint)(index + (k * step))).AsInt16() * taps[k];
        }

        return sum;
    }

    /// <summary>Sums the products of eight columns of first stage results with the taps down a column, widened to 32 bits, and shifts the sums.</summary>
    /// <param name="origin">The first value of the array.</param>
    /// <param name="index">The index of the first value under the first tap, for the first of the eight columns.</param>
    /// <param name="step">The row length of the results.</param>
    /// <param name="taps">The taps.</param>
    /// <param name="shift">The right shift.</param>
    /// <returns>The eight filtered samples.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector128<short> Wide(ref short origin, int index, int step, ReadOnlySpan<Vector128<short>> taps, int shift)
    {
        var low = Vector128<int>.Zero;
        var high = Vector128<int>.Zero;
        for (var k = 0; k < taps.Length; k++)
        {
            var values = Vector128.LoadUnsafe(ref origin, (nuint)(index + (k * step)));
            var tap = taps[k];
            low += Vector128.WidenLower(values) * Vector128.WidenLower(tap);
            high += Vector128.WidenUpper(values) * Vector128.WidenUpper(tap);
        }

        return Vector128.Narrow(Vector128.ShiftRightArithmetic(low, shift), Vector128.ShiftRightArithmetic(high, shift));
    }

    /// <summary>Stores the first lanes of a vector, as many as the span is long, which is fewer than eight.</summary>
    /// <param name="values">The values.</param>
    /// <param name="destination">Receives the values.</param>
    private static void StoreRemainder(Vector128<short> values, Span<short> destination)
    {
        var done = 0;
        if (destination.Length >= HalfLanes)
        {
            values.GetLower().CopyTo(destination);
            done = HalfLanes;
        }

        for (; done < destination.Length; done++)
        {
            destination[done] = values.GetElement(done);
        }
    }
}
