// Copyright (c) 2026 Glenn Watson. All rights reserved.
// Glenn Watson licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Numerics;
using System.Runtime.Intrinsics;

namespace HyperCodecs.H265;

/// <summary>
/// The widest vectors the processor has, for the interpolation of 8 bit video: 16 samples at a time on a processor with 256 bit vectors. Each routine does
/// as many whole vectors of a row as fit and says how far it got, and the 128 bit routines finish the row. On a processor whose widest vector is 128 bits
/// nothing here applies.
/// </summary>
internal static class H265InterpolationWide
{
    /// <summary>Gets the 16 bit samples in a vector.</summary>
    internal static int Lanes => Vector<short>.Count;

    /// <summary>Gets a value indicating whether the processor has vectors wider than 128 bits.</summary>
    internal static bool IsAvailable => Vector.IsHardwareAccelerated && Vector<short>.Count > Vector128<short>.Count;

    /// <summary>Repeats each tap of a filter in every lane of a wide vector.</summary>
    /// <param name="taps">The taps, each in every lane of a 128 bit vector.</param>
    /// <param name="store">Room for at least as many vectors as taps.</param>
    /// <returns>The wide taps, or nothing when the processor has no wide vectors.</returns>
    internal static ReadOnlySpan<Vector<short>> ShortTaps(ReadOnlySpan<Vector128<short>> taps, Span<Vector<short>> store)
    {
        if (!IsAvailable)
        {
            return default;
        }

        for (var k = 0; k < taps.Length; k++)
        {
            store[k] = new(taps[k].GetElement(0));
        }

        return store[..taps.Length];
    }

    /// <summary>Repeats each tap of a filter in every lane of a wide vector of 32 bit values.</summary>
    /// <param name="taps">The taps, each in every lane of a 128 bit vector.</param>
    /// <param name="store">Room for at least as many vectors as taps.</param>
    /// <returns>The wide taps, or nothing when the processor has no wide vectors.</returns>
    internal static ReadOnlySpan<Vector<int>> IntTaps(ReadOnlySpan<Vector128<short>> taps, Span<Vector<int>> store)
    {
        if (!IsAvailable)
        {
            return default;
        }

        for (var k = 0; k < taps.Length; k++)
        {
            store[k] = new(taps[k].GetElement(0));
        }

        return store[..taps.Length];
    }

    /// <summary>Sums the products of whole wide vectors of columns of 8 bit samples with the taps, down a column or along a row.</summary>
    /// <param name="origin">The first sample of the array.</param>
    /// <param name="index">The index of the first sample under the first tap, for the first column.</param>
    /// <param name="step">The distance between the samples under successive taps.</param>
    /// <param name="width">The columns to do, of which only whole vectors are done.</param>
    /// <param name="taps">The wide taps; empty when there are none.</param>
    /// <param name="target">Receives the sums, starting at the first column.</param>
    /// <returns>The columns done.</returns>
    internal static int Sum(ref ushort origin, int index, int step, int width, ReadOnlySpan<Vector<short>> taps, ref short target)
    {
        var lanes = Lanes;
        var x = 0;
        if (taps.IsEmpty)
        {
            return x;
        }

        for (; x + lanes <= width; x += lanes)
        {
            var sum = Vector<short>.Zero;
            for (var k = 0; k < taps.Length; k++)
            {
                sum += Vector.AsVectorInt16(Vector.LoadUnsafe(ref origin, (nuint)(index + x + (k * step)))) * taps[k];
            }

            Vector.StoreUnsafe(sum, ref target, (nuint)x);
        }

        return x;
    }

    /// <summary>Filters whole wide vectors of columns of first stage results down the columns, in 32 bits.</summary>
    /// <param name="origin">The first value of the array.</param>
    /// <param name="index">The index of the first value under the first tap, for the first column.</param>
    /// <param name="step">The row length of the results.</param>
    /// <param name="width">The columns to do, of which only whole vectors are done.</param>
    /// <param name="taps">The wide taps as 32 bit values; empty when there are none.</param>
    /// <param name="shift">The right shift.</param>
    /// <param name="target">Receives the filtered samples, starting at the first column.</param>
    /// <returns>The columns done.</returns>
    internal static int Intermediate(ref short origin, int index, int step, int width, ReadOnlySpan<Vector<int>> taps, int shift, ref short target)
    {
        var lanes = Lanes;
        var x = 0;
        if (taps.IsEmpty)
        {
            return x;
        }

        for (; x + lanes <= width; x += lanes)
        {
            var low = Vector<int>.Zero;
            var high = Vector<int>.Zero;
            for (var k = 0; k < taps.Length; k++)
            {
                Vector.Widen(Vector.LoadUnsafe(ref origin, (nuint)(index + x + (k * step))), out var lower, out var upper);
                low += lower * taps[k];
                high += upper * taps[k];
            }

            Vector.StoreUnsafe(Vector.Narrow(Vector.ShiftRightArithmetic(low, shift), Vector.ShiftRightArithmetic(high, shift)), ref target, (nuint)x);
        }

        return x;
    }
}
