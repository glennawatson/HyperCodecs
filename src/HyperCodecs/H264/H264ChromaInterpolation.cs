// Copyright (c) 2026 Glenn Watson. All rights reserved.
// Glenn Watson licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace HyperCodecs.H264;

/// <summary>
/// Predicts a chroma block of one component from a reference plane with the bilinear interpolation of clause 8.4.2.2.2 of ITU-T H.264. Every sum is at most
/// 64 times 255, so it works eight samples at a time in 16 bit lanes, whatever the width of the block. A block that reaches outside the plane is predicted
/// from a small copy of its reference samples in which the edge samples are repeated.
/// </summary>
internal static class H264ChromaInterpolation
{
    /// <summary>The samples predicted at a time.</summary>
    private const int Lanes = 8;

    /// <summary>The largest block, in samples along a side.</summary>
    private const int MaxBlock = 8;

    /// <summary>The width of a block that is stored four samples at a time.</summary>
    private const int NarrowBlock = 4;

    /// <summary>The samples in a row of the copy of the reference samples of a block that reaches outside the plane.</summary>
    private const int WindowStride = 16;

    /// <summary>The rows of that copy: the largest block and the row below it.</summary>
    private const int WindowRows = MaxBlock + 1;

    /// <summary>The shift of a chroma motion vector to whole samples.</summary>
    private const int FractionBits = 3;

    /// <summary>The mask of the fractional part of a chroma motion vector.</summary>
    private const int FractionMask = 7;

    /// <summary>The sum of the weights along one axis of the interpolation.</summary>
    private const int Scale = 8;

    /// <summary>The rounding added before the sum is shifted.</summary>
    private const int RoundBy = 32;

    /// <summary>The shift after the sum.</summary>
    private const int Shift = 6;

    /// <summary>Predicts a chroma block.</summary>
    /// <param name="reference">The reference plane.</param>
    /// <param name="stride">The samples in a row of the reference plane, which is also its width.</param>
    /// <param name="planeHeight">The rows in the reference plane.</param>
    /// <param name="target">Where the predicted samples go: a plane, its stride and the place of the block's first sample.</param>
    /// <param name="block">The block's position and size in the reference plane.</param>
    /// <param name="vector">The luma motion vector, which is in eighths of a chroma sample.</param>
    internal static void Predict(byte[] reference, int stride, int planeHeight, H264PlaneTarget target, BlockRect block, MotionVector vector)
    {
        var source = new BlockRect(block.X + (vector.X >> FractionBits), block.Y + (vector.Y >> FractionBits), block.Width, block.Height);
        var fraction = new SamplePlace(vector.X & FractionMask, vector.Y & FractionMask);

        // The block is read eight samples at a time and the sample beyond each one is read too, so a narrow block is checked as if it were eight wide.
        if (source.X >= 0 && source.Y >= 0 && source.X + Math.Max(source.Width, Lanes) < stride && source.Y + source.Height < planeHeight)
        {
            PredictFrom(ref MemoryMarshal.GetArrayDataReference(reference), stride, target, source, fraction);
            return;
        }

        PredictOutside(reference, stride, planeHeight, target, source, fraction);
    }

    /// <summary>Predicts a block that reaches outside the plane from a copy of its reference samples in which the samples at the edge are repeated.</summary>
    /// <param name="reference">The reference plane.</param>
    /// <param name="stride">The samples in a row of the plane, which is also its width.</param>
    /// <param name="planeHeight">The rows in the plane.</param>
    /// <param name="target">Where the samples go.</param>
    /// <param name="source">The block's first sample in the plane and its size.</param>
    /// <param name="fraction">The eighth-sample part of the vector: the column fraction as the column and the row fraction as the row.</param>
    [SkipLocalsInit]
    private static void PredictOutside(byte[] reference, int stride, int planeHeight, H264PlaneTarget target, BlockRect source, SamplePlace fraction)
    {
        Span<byte> window = stackalloc byte[WindowStride * WindowRows];
        var columns = source.Width + 1;
        for (var row = 0; row <= source.Height; row++)
        {
            var line = reference.AsSpan(Math.Clamp(source.Y + row, 0, planeHeight - 1) * stride, stride);
            var destination = window.Slice(row * WindowStride, columns);
            for (var column = 0; column < columns; column++)
            {
                destination[column] = line[Math.Clamp(source.X + column, 0, stride - 1)];
            }
        }

        PredictFrom(ref MemoryMarshal.GetReference(window), WindowStride, target, new(0, 0, source.Width, source.Height), fraction);
    }

    /// <summary>Predicts a block whose reference samples, and the one beyond each, all lie inside the plane that <paramref name="origin"/> starts.</summary>
    /// <param name="origin">The first sample of the plane.</param>
    /// <param name="stride">The samples in a row of the plane.</param>
    /// <param name="target">Where the samples go.</param>
    /// <param name="source">The block's first sample in the plane and its size.</param>
    /// <param name="fraction">The eighth-sample part of the vector: the column fraction as the column and the row fraction as the row.</param>
    private static void PredictFrom(ref byte origin, int stride, H264PlaneTarget target, BlockRect source, SamplePlace fraction)
    {
        var (fractionX, fractionY) = fraction;
        var whole = (fractionX | fractionY) == 0;
        var topLeft = Vector128.Create((ushort)((Scale - fractionX) * (Scale - fractionY)));
        var topRight = Vector128.Create((ushort)(fractionX * (Scale - fractionY)));
        var bottomLeft = Vector128.Create((ushort)((Scale - fractionX) * fractionY));
        var bottomRight = Vector128.Create((ushort)(fractionX * fractionY));
        var round = Vector128.Create((ushort)RoundBy);
        for (var row = 0; row < source.Height; row++)
        {
            ref var upper = ref Unsafe.Add(ref origin, ((source.Y + row) * stride) + source.X);
            ref var lower = ref Unsafe.Add(ref upper, stride);
            var sum = Load(ref upper);
            if (!whole)
            {
                sum = Vector128.ShiftRightLogical(
                    (sum * topLeft) + (Load(ref Unsafe.Add(ref upper, 1)) * topRight) + (Load(ref lower) * bottomLeft) + (Load(ref Unsafe.Add(ref lower, 1)) * bottomRight) + round,
                    Shift);
            }

            Store(Vector128.Narrow(sum, sum), target.Plane.AsSpan(((target.Y + row) * target.Stride) + target.X, source.Width));
        }
    }

    /// <summary>Loads eight samples as 16 bit values.</summary>
    /// <param name="first">The first of the eight samples.</param>
    /// <returns>The values.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector128<ushort> Load(ref byte first) =>
        Vector128.WidenLower(Vector128.CreateScalarUnsafe(Unsafe.ReadUnaligned<ulong>(ref first)).AsByte());

    /// <summary>Stores the first samples of a row of predicted bytes: as many as the span is long, which is 8, 4 or 2.</summary>
    /// <param name="bytes">The predicted samples, eight of which are valid in the low lanes.</param>
    /// <param name="destination">Where the samples go, as long as the block is wide.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Store(Vector128<byte> bytes, Span<byte> destination)
    {
        ref var to = ref MemoryMarshal.GetReference(destination);
        if (destination.Length == Lanes)
        {
            Unsafe.WriteUnaligned(ref to, bytes.AsUInt64().ToScalar());
            return;
        }

        if (destination.Length == NarrowBlock)
        {
            Unsafe.WriteUnaligned(ref to, bytes.AsUInt32().ToScalar());
            return;
        }

        Unsafe.WriteUnaligned(ref to, bytes.AsUInt16().ToScalar());
    }
}
