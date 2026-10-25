// Copyright (c) 2026 Glenn Watson. All rights reserved.
// Glenn Watson licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace HyperCodecs.H265;

/// <summary>Interpolates fractional sample positions of a reference block to the 14 bit intermediate precision the weighted prediction takes (8.5.3.3.3).</summary>
internal static class H265Interpolation
{
    /// <summary>The number of 32 bit values in a 128 bit vector.</summary>
    private const int Lanes = 4;

    /// <summary>The intermediate bit depth.</summary>
    private const int IntermediateBits = 14;

    /// <summary>The shift of the second filter stage.</summary>
    private const int SecondShift = 6;

    /// <summary>The largest first stage shift.</summary>
    private const int MaxFirstShift = 4;

    /// <summary>The smallest shift that brings a whole sample to the intermediate precision.</summary>
    private const int MinWholeShift = 2;

    /// <summary>The bit depth of 8 bit video.</summary>
    private const int EightBits = 8;

    /// <summary>Interpolates a block.</summary>
    /// <param name="job">The block.</param>
    /// <param name="buffers">The work areas.</param>
    /// <param name="destination">Receives the block, row after row.</param>
    internal static void Run(in H265InterpolationJob job, H265PredictionBuffers buffers, Span<short> destination)
    {
        var taps = job.Chroma ? H265InterpolationFilters.ChromaTaps : H265InterpolationFilters.LumaTaps;
        var eightBit = job.BitDepth == EightBits && Vector128.IsHardwareAccelerated;
        var window = Window(job, buffers, taps, eightBit ? H265InterpolationEightBit.Padded(job.Source.Width) : job.Source.Width);
        if (job.FracX == 0 && job.FracY == 0)
        {
            var wholeShift = Math.Max(MinWholeShift, IntermediateBits - job.BitDepth);
            CopyWhole(window.Samples, window.Stride, (taps >> 1) - 1, destination, job.Source.Width, job.Source.Height, wholeShift);
            return;
        }

        if (eightBit)
        {
            RunEightBit(job, buffers, window, destination);
            return;
        }

        RunGeneral(job, buffers, window, destination);
    }

    /// <summary>Interpolates a block of video of more than 8 bits with a fraction in at least one direction, in 32 bit lanes.</summary>
    /// <param name="job">The block.</param>
    /// <param name="buffers">The work areas.</param>
    /// <param name="window">The reference samples of the block and its margins.</param>
    /// <param name="destination">Receives the block, row after row.</param>
    private static void RunGeneral(in H265InterpolationJob job, H265PredictionBuffers buffers, in H265ReferenceWindow window, Span<short> destination)
    {
        var taps = job.Chroma ? H265InterpolationFilters.ChromaTaps : H265InterpolationFilters.LumaTaps;
        var before = (taps >> 1) - 1;
        var width = job.Source.Width;
        var height = job.Source.Height;
        var shift1 = Math.Min(MaxFirstShift, job.BitDepth - EightBits);
        var reference = window.Samples;
        var horizontal = job.FracX == 0 ? default : Filter(job, job.FracX);
        var vertical = job.FracY == 0 ? default : Filter(job, job.FracY);
        if (job.FracY == 0)
        {
            FilterRows(reference, new(window.Stride, width, height), before, horizontal, destination, shift1);
        }
        else if (job.FracX == 0)
        {
            FilterColumns(reference[before..], window.Stride, vertical, destination, width, height, shift1);
        }
        else
        {
            var middle = buffers.Horizontal.AsSpan();
            FilterRows(reference, new(window.Stride, width, height + taps - 1), 0, horizontal, middle, shift1);
            FilterColumns(middle, width, vertical, destination, width, height, SecondShift);
        }
    }

    /// <summary>Interpolates a block of 8 bit video with a fraction in at least one direction, in 16 bit lanes.</summary>
    /// <param name="job">The block.</param>
    /// <param name="buffers">The work areas.</param>
    /// <param name="window">The reference samples of the block and its margins.</param>
    /// <param name="destination">Receives the block, row after row.</param>
    private static void RunEightBit(in H265InterpolationJob job, H265PredictionBuffers buffers, in H265ReferenceWindow window, Span<short> destination)
    {
        var taps = job.Chroma ? H265InterpolationFilters.ChromaTaps : H265InterpolationFilters.LumaTaps;
        var before = (taps >> 1) - 1;
        var size = new BlockRect(0, 0, job.Source.Width, job.Source.Height);
        Span<Vector128<short>> horizontalStore = stackalloc Vector128<short>[H265InterpolationFilters.LumaTaps];
        Span<Vector128<short>> verticalStore = stackalloc Vector128<short>[H265InterpolationFilters.LumaTaps];
        var horizontal = horizontalStore[..taps];
        var vertical = verticalStore[..taps];
        var samples = window.Samples;
        if (job.FracY == 0)
        {
            H265InterpolationEightBit.Broadcast(Filter(job, job.FracX), horizontal);
            H265InterpolationEightBit.FilterRows(samples, new(window.Stride, size.Width, size.Height), before, horizontal, destination);
        }
        else if (job.FracX == 0)
        {
            H265InterpolationEightBit.Broadcast(Filter(job, job.FracY), vertical);
            H265InterpolationEightBit.FilterColumns(samples[before..], window.Stride, vertical, destination, size);
        }
        else
        {
            H265InterpolationEightBit.Broadcast(Filter(job, job.FracX), horizontal);
            H265InterpolationEightBit.Broadcast(Filter(job, job.FracY), vertical);
            var middle = buffers.Horizontal.AsSpan();
            H265InterpolationEightBit.FilterRows(samples, new(window.Stride, size.Width, size.Height + taps - 1), 0, horizontal, middle);
            H265InterpolationEightBit.FilterIntermediate(middle, size.Width, vertical, destination, size, SecondShift);
        }
    }

    /// <summary>Finds the reference samples of a block and its margins: in the plane itself when they all lie inside it, otherwise in a copy made with the edge samples repeated.</summary>
    /// <param name="job">The block.</param>
    /// <param name="buffers">The work areas.</param>
    /// <param name="taps">The taps of the filter.</param>
    /// <param name="readWidth">The width the filters read in each row of the block, which is more than the block is wide when they read whole vectors.</param>
    /// <returns>The samples, starting at the first sample of the margin above and left of the block, and their row length.</returns>
    private static H265ReferenceWindow Window(in H265InterpolationJob job, H265PredictionBuffers buffers, int taps, int readWidth)
    {
        var before = (taps >> 1) - 1;
        var left = job.Source.X - before;
        var top = job.Source.Y - before;
        if (left >= 0 && top >= 0 && left + readWidth + taps - 1 <= job.PlaneWidth && top + job.Source.Height + taps - 1 <= job.PlaneHeight)
        {
            return new(job.Plane.AsMemory((top * job.PlaneWidth) + left), job.PlaneWidth);
        }

        Fetch(job, buffers.Reference, before, taps);
        return new(buffers.Reference.AsMemory(), job.Source.Width + taps - 1);
    }

    /// <summary>Gets the taps for a fraction.</summary>
    /// <param name="job">The block.</param>
    /// <param name="fraction">The fraction.</param>
    /// <returns>The taps.</returns>
    private static ReadOnlySpan<sbyte> Filter(in H265InterpolationJob job, int fraction) =>
        job.Chroma ? H265InterpolationFilters.Chroma(fraction) : H265InterpolationFilters.Luma(fraction);

    /// <summary>Copies the reference block and its margins into the work area, repeating the edge samples of the plane where the block reaches past them.</summary>
    /// <param name="job">The block.</param>
    /// <param name="target">The work area.</param>
    /// <param name="before">The margin before the block.</param>
    /// <param name="taps">The taps of the filter.</param>
    private static void Fetch(in H265InterpolationJob job, ushort[] target, int before, int taps)
    {
        var width = job.Source.Width + taps - 1;
        var height = job.Source.Height + taps - 1;
        var left = job.Source.X - before;
        var top = job.Source.Y - before;
        var inside = left >= 0 && left + width <= job.PlaneWidth;
        for (var row = 0; row < height; row++)
        {
            var sourceRow = Math.Clamp(top + row, 0, job.PlaneHeight - 1) * job.PlaneWidth;
            var line = target.AsSpan(row * width, width);
            if (inside)
            {
                job.Plane.AsSpan(sourceRow + left, width).CopyTo(line);
                continue;
            }

            for (var column = 0; column < width; column++)
            {
                line[column] = job.Plane[sourceRow + Math.Clamp(left + column, 0, job.PlaneWidth - 1)];
            }
        }
    }

    /// <summary>Copies whole samples scaled to the intermediate precision.</summary>
    /// <param name="reference">The work area.</param>
    /// <param name="stride">The work area's row length.</param>
    /// <param name="before">The margin before the block.</param>
    /// <param name="destination">Receives the samples.</param>
    /// <param name="width">The block width.</param>
    /// <param name="height">The block height.</param>
    /// <param name="shift">The left shift.</param>
    private static void CopyWhole(ReadOnlySpan<ushort> reference, int stride, int before, Span<short> destination, int width, int height, int shift)
    {
        for (var y = 0; y < height; y++)
        {
            var source = ((y + before) * stride) + before;
            var x = 0;
            if (Vector128.IsHardwareAccelerated)
            {
                for (; x + Lanes <= width; x += Lanes)
                {
                    Store(Vector128.ShiftLeft(Widen(reference, source + x), shift), destination, (y * width) + x);
                }
            }

            for (; x < width; x++)
            {
                destination[(y * width) + x] = (short)(reference[source + x] << shift);
            }
        }
    }

    /// <summary>Filters along rows.</summary>
    /// <param name="reference">The work area.</param>
    /// <param name="area">The row length of the work area, and the width and number of the rows to filter.</param>
    /// <param name="first">The work area row to start at.</param>
    /// <param name="taps">The filter taps.</param>
    /// <param name="destination">Receives the filtered samples.</param>
    /// <param name="shift">The right shift.</param>
    private static void FilterRows(ReadOnlySpan<ushort> reference, in H265FilterArea area, int first, ReadOnlySpan<sbyte> taps, Span<short> destination, int shift)
    {
        var width = area.Width;
        for (var y = 0; y < area.Height; y++)
        {
            var source = (first + y) * area.Stride;
            var x = 0;
            if (Vector128.IsHardwareAccelerated)
            {
                for (; x + Lanes <= width; x += Lanes)
                {
                    var sum = Vector128<int>.Zero;
                    for (var k = 0; k < taps.Length; k++)
                    {
                        sum += Vector128.Create((int)taps[k]) * Widen(reference, source + x + k);
                    }

                    Store(Vector128.ShiftRightArithmetic(sum, shift), destination, (y * width) + x);
                }
            }

            for (; x < width; x++)
            {
                var sum = 0;
                for (var k = 0; k < taps.Length; k++)
                {
                    sum += taps[k] * reference[source + x + k];
                }

                destination[(y * width) + x] = (short)(sum >> shift);
            }
        }
    }

    /// <summary>
    /// Filters along columns, either of the work area (unsigned samples) or of the intermediate rows produced by the horizontal stage (signed values).
    /// </summary>
    /// <typeparam name="TSample">The sample type, <see cref="ushort"/> for the work area or <see cref="short"/> for the intermediate rows.</typeparam>
    /// <param name="source">The samples, starting at the first sample of the first column to filter.</param>
    /// <param name="stride">The row length of the source.</param>
    /// <param name="taps">The filter taps.</param>
    /// <param name="destination">Receives the filtered samples.</param>
    /// <param name="width">The block width.</param>
    /// <param name="height">The block height.</param>
    /// <param name="shift">The right shift.</param>
    private static void FilterColumns<TSample>(ReadOnlySpan<TSample> source, int stride, ReadOnlySpan<sbyte> taps, Span<short> destination, int width, int height, int shift)
        where TSample : unmanaged, IBinaryInteger<TSample>
    {
        for (var y = 0; y < height; y++)
        {
            var x = 0;
            if (Vector128.IsHardwareAccelerated)
            {
                for (; x + Lanes <= width; x += Lanes)
                {
                    var sum = Vector128<int>.Zero;
                    for (var k = 0; k < taps.Length; k++)
                    {
                        sum += Vector128.Create((int)taps[k]) * WidenSamples(source, ((y + k) * stride) + x);
                    }

                    Store(Vector128.ShiftRightArithmetic(sum, shift), destination, (y * width) + x);
                }
            }

            for (; x < width; x++)
            {
                var sum = 0;
                for (var k = 0; k < taps.Length; k++)
                {
                    sum += taps[k] * int.CreateTruncating(source[((y + k) * stride) + x]);
                }

                destination[(y * width) + x] = (short)(sum >> shift);
            }
        }
    }

    /// <summary>Loads four samples of either type as 32 bit values.</summary>
    /// <typeparam name="TSample">The sample type, <see cref="ushort"/> or <see cref="short"/>.</typeparam>
    /// <param name="samples">The samples.</param>
    /// <param name="index">The index of the first of the four.</param>
    /// <returns>The values.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector128<int> WidenSamples<TSample>(ReadOnlySpan<TSample> samples, int index)
        where TSample : unmanaged =>
        typeof(TSample) == typeof(ushort)
            ? Widen(MemoryMarshal.Cast<TSample, ushort>(samples), index)
            : Widen(MemoryMarshal.Cast<TSample, short>(samples), index);

    /// <summary>Loads four unsigned samples as 32 bit values.</summary>
    /// <param name="samples">The samples.</param>
    /// <param name="index">The index of the first of the four.</param>
    /// <returns>The values.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector128<int> Widen(ReadOnlySpan<ushort> samples, int index) =>
        Vector128.WidenLower(Vector64.Create(samples.Slice(index, Lanes)).ToVector128Unsafe()).AsInt32();

    /// <summary>Loads four signed samples as 32 bit values.</summary>
    /// <param name="samples">The samples.</param>
    /// <param name="index">The index of the first of the four.</param>
    /// <returns>The values.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector128<int> Widen(ReadOnlySpan<short> samples, int index) =>
        Vector128.WidenLower(Vector64.Create(samples.Slice(index, Lanes)).ToVector128Unsafe());

    /// <summary>Stores four values as 16 bit samples, keeping the low 16 bits of each.</summary>
    /// <param name="values">The values.</param>
    /// <param name="destination">Receives the samples.</param>
    /// <param name="index">The index of the first of the four.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Store(Vector128<int> values, Span<short> destination, int index) =>
        Vector128.Narrow(values, values).GetLower().CopyTo(destination.Slice(index, Lanes));
}
