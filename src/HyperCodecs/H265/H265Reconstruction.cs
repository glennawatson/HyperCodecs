// Copyright (c) 2026 Glenn Watson. All rights reserved.
// Glenn Watson licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace HyperCodecs.H265;

/// <summary>Turns the coefficient levels of a block into residual samples and adds them to the prediction already in the picture (8.6.2, 8.6.5 and 8.6.7).</summary>
internal static class H265Reconstruction
{
    /// <summary>The side of the block that uses the DST for intra luma.</summary>
    private const int DstLog2 = 2;

    /// <summary>The side of the blocks whose skipped transform may be rotated.</summary>
    private const int RotatedSide = 4;

    /// <summary>The bits that bit depth and the final shift add up to.</summary>
    private const int ShiftTotal = 20;

    /// <summary>The shift of a skipped transform block, before the size term.</summary>
    private const int SkipShiftBase = 5;

    /// <summary>Adds the residual of a block to the picture and clears the coefficient array.</summary>
    /// <param name="context">The slice context.</param>
    /// <param name="block">Describes the block.</param>
    /// <param name="result">What reading the residual found.</param>
    /// <param name="x">The column of the block's first sample in its colour plane.</param>
    /// <param name="y">The row of the block's first sample in its colour plane.</param>
    internal static void Add(H265SliceContext context, in H265ResidualBlock block, H265ResidualResult result, int x, int y)
    {
        var side = 1 << block.Log2Size;
        var levels = context.Coefficients.AsSpan(0, side * side);
        var bitDepth = block.CIdx == 0 ? context.Sps.BitDepthLuma : context.Sps.BitDepthChroma;
        var rotate = context.Sps.Range.TransformSkipRotation && side == RotatedSide && block.Intra;
        var residual = levels;
        var transformed = false;
        if (block.Bypass)
        {
            if (rotate)
            {
                levels.Reverse();
            }
        }
        else
        {
            H265Dequantizer.Scale(context, levels, block, result.TransformSkip, result.Extent);
            if (result.TransformSkip)
            {
                SkipTransform(levels, block, rotate, bitDepth);
            }
            else
            {
                // The inverse transform writes the residual to its own buffer, so only the part of the coefficient array that held coefficients needs clearing.
                residual = context.ResidualSamples.AsSpan(0, levels.Length);
                var dst = block.Intra && block.Log2Size == DstLog2 && block.CIdx == 0;
                H265Transform.Inverse(levels, residual, block.Log2Size, dst, bitDepth, result.Extent, context.Scratch);
                transformed = true;
            }
        }

        Accumulate(residual, side, result.Rdpcm);
        AddToPicture(context, residual, block.CIdx, new(x, y, side, side), bitDepth);
        if (transformed)
        {
            ClearExtent(levels, side, result.Extent);
        }
        else
        {
            levels.Clear();
        }
    }

    /// <summary>Clears the part of a coefficient array that can hold coefficients.</summary>
    /// <param name="levels">The coefficients, row after row.</param>
    /// <param name="side">The block side.</param>
    /// <param name="extent">The columns and rows that can hold coefficients.</param>
    private static void ClearExtent(Span<int> levels, int side, H265BlockExtent extent)
    {
        for (var row = 0; row <= extent.LastRow; row++)
        {
            levels.Slice(row * side, extent.LastColumn + 1).Clear();
        }
    }

    /// <summary>Applies the shift that replaces the transform for a block that skips it.</summary>
    /// <param name="levels">The scaled coefficients, replaced by residual samples.</param>
    /// <param name="block">Describes the block.</param>
    /// <param name="rotate">Whether the block is rotated by half a turn.</param>
    /// <param name="bitDepth">The bit depth of the colour component.</param>
    private static void SkipTransform(Span<int> levels, in H265ResidualBlock block, bool rotate, int bitDepth)
    {
        if (rotate)
        {
            levels.Reverse();
        }

        var shift = ShiftTotal - bitDepth;
        var up = SkipShiftBase + block.Log2Size;
        var round = 1 << (shift - 1);
        for (var i = 0; i < levels.Length; i++)
        {
            levels[i] = ((levels[i] << up) + round) >> shift;
        }
    }

    /// <summary>Accumulates residual samples along rows or columns for residual DPCM (8.6.5).</summary>
    /// <param name="residual">The residual samples.</param>
    /// <param name="side">The block side.</param>
    /// <param name="mode">The direction.</param>
    private static void Accumulate(Span<int> residual, int side, H265RdpcmMode mode)
    {
        if (mode == H265RdpcmMode.Horizontal)
        {
            for (var y = 0; y < side; y++)
            {
                for (var x = 1; x < side; x++)
                {
                    residual[(y * side) + x] += residual[(y * side) + x - 1];
                }
            }
        }
        else if (mode == H265RdpcmMode.Vertical)
        {
            for (var y = 1; y < side; y++)
            {
                for (var x = 0; x < side; x++)
                {
                    residual[(y * side) + x] += residual[((y - 1) * side) + x];
                }
            }
        }
    }

    /// <summary>Adds residual samples to the prediction in the picture and clips the sums to the sample range.</summary>
    /// <param name="context">The slice context.</param>
    /// <param name="residual">The residual samples.</param>
    /// <param name="component">The colour component.</param>
    /// <param name="area">The area of the colour plane.</param>
    /// <param name="bitDepth">The bit depth of the colour component.</param>
    private static void AddToPicture(H265SliceContext context, ReadOnlySpan<int> residual, int component, BlockRect area, int bitDepth)
    {
        var picture = context.Picture;
        var plane = picture.PlaneOf(component);
        var stride = picture.StrideOf(component);
        var maximum = (1 << bitDepth) - 1;
        for (var row = 0; row < area.Height; row++)
        {
            var offset = ((area.Y + row) * stride) + area.X;
            for (var column = 0; column < area.Width; column++)
            {
                plane[offset + column] = (ushort)Math.Clamp(plane[offset + column] + residual[(row * area.Width) + column], 0, maximum);
            }
        }
    }
}
