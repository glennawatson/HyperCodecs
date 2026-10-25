// Copyright (c) 2026 Glenn Watson. All rights reserved.
// Glenn Watson licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Runtime.CompilerServices;

namespace HyperCodecs.H264;

/// <summary>The prediction modes the 16x16 luma block and the chroma block share, which differ only in size and in the scale of the plane gradients.</summary>
internal static class H264IntraBlocks
{
    /// <summary>The largest sample value.</summary>
    private const int MaxSample = 255;

    /// <summary>The distance from the last gradient sample to the first one used on the near side.</summary>
    private const int Back = 2;

    /// <summary>The multiplier of the corner samples, and the rounding of the plane prediction.</summary>
    private const int PlaneCorner = 16;

    /// <summary>The shift of the plane prediction.</summary>
    private const int PlaneFinalShift = 5;

    /// <summary>The rounding of the plane gradients.</summary>
    private const int PlaneRound = 32;

    /// <summary>The shift of the plane gradients.</summary>
    private const int PlaneShift = 6;

    /// <summary>Predicts a block by copying the row of samples above it down every row.</summary>
    /// <param name="plane">The plane.</param>
    /// <param name="offset">The index of the block's first sample.</param>
    /// <param name="stride">The bytes in a row.</param>
    /// <param name="size">The side of the block.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static void CopyAbove(byte[] plane, int offset, int stride, int size)
    {
        for (var y = 0; y < size; y++)
        {
            Array.Copy(plane, offset - stride, plane, offset + (y * stride), size);
        }
    }

    /// <summary>Predicts a block by repeating the sample to the left of each row along that row.</summary>
    /// <param name="plane">The plane.</param>
    /// <param name="offset">The index of the block's first sample.</param>
    /// <param name="stride">The bytes in a row.</param>
    /// <param name="size">The side of the block.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static void ReplicateLeft(byte[] plane, int offset, int stride, int size)
    {
        for (var y = 0; y < size; y++)
        {
            plane.AsSpan(offset + (y * stride), size).Fill(plane[offset + (y * stride) - 1]);
        }
    }

    /// <summary>Predicts a block with the plane mode.</summary>
    /// <param name="plane">The plane.</param>
    /// <param name="offset">The index of the block's first sample.</param>
    /// <param name="stride">The bytes in a row.</param>
    /// <param name="size">The side of the block.</param>
    /// <param name="gradientScale">The multiplier of the plane gradients: 5 for the 16x16 luma block, 34 for the chroma block of 4:2:0 video.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static void PredictPlane(byte[] plane, int offset, int stride, int size, int gradientScale)
    {
        var half = size >> 1;
        var horizontal = 0;
        var vertical = 0;
        for (var index = 0; index < half; index++)
        {
            var right = plane[offset - stride + half + index];
            var left = index == half - 1 ? plane[offset - stride - 1] : plane[offset - stride + half - Back - index];
            var bottom = plane[offset + ((half + index) * stride) - 1];
            var upper = index == half - 1 ? plane[offset - stride - 1] : plane[offset + ((half - Back - index) * stride) - 1];
            horizontal += (index + 1) * (right - left);
            vertical += (index + 1) * (bottom - upper);
        }

        var a = PlaneCorner * (plane[offset + ((size - 1) * stride) - 1] + plane[offset - stride + size - 1]);
        var b = ((gradientScale * horizontal) + PlaneRound) >> PlaneShift;
        var c = ((gradientScale * vertical) + PlaneRound) >> PlaneShift;
        var center = half - 1;
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var value = (a + (b * (x - center)) + (c * (y - center)) + PlaneCorner) >> PlaneFinalShift;
                plane[offset + (y * stride) + x] = (byte)Math.Clamp(value, 0, MaxSample);
            }
        }
    }
}
