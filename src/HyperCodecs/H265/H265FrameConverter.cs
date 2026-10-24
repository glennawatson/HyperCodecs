// Copyright (c) 2026 Glenn Watson. All rights reserved.
// Glenn Watson licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace HyperCodecs.H265;

/// <summary>Turns a decoded picture into the 8 bit 4:2:0 frame the video player shows: cropped, reduced to 8 bits and, for other chroma formats, resampled.</summary>
internal static class H265FrameConverter
{
    /// <summary>The value of a chroma sample of a monochrome picture.</summary>
    private const byte NeutralChroma = 128;

    /// <summary>The subsampling of the chroma planes of the output frame along each axis.</summary>
    private const int OutputSubsampling = 2;

    /// <summary>The bit depth of the output frame.</summary>
    private const int OutputBits = 8;

    /// <summary>Gets the size of the shown picture.</summary>
    /// <param name="sps">The sequence parameter set.</param>
    /// <returns>The width and height in luma samples.</returns>
    internal static BlockRect ShownSize(H265Sps sps) =>
        new(sps.Crop.Left, sps.Crop.Top, sps.Width - sps.Crop.Left - sps.Crop.Right, sps.Height - sps.Crop.Top - sps.Crop.Bottom);

    /// <summary>Converts a picture.</summary>
    /// <param name="picture">The picture.</param>
    /// <param name="sps">The sequence parameter set the picture was decoded with.</param>
    /// <param name="frame">The frame to fill, of the shown size; every sample is written.</param>
    /// <returns>The frame.</returns>
    internal static VideoFrame Convert(H265Picture picture, H265Sps sps, VideoFrame frame)
    {
        var shown = ShownSize(sps);
        var luma = new H265ConvertPlane(picture.Luma, picture.Width, shown, 1, 1, frame.Y, sps.BitDepthLuma);
        CopyRows(luma);
        if (sps.ChromaFormat == ChromaSampling.Monochrome)
        {
            Array.Fill(frame.U, NeutralChroma);
            Array.Fill(frame.V, NeutralChroma);
            return frame;
        }

        var subW = sps.SubWidthC;
        var subH = sps.SubHeightC;
        var area = new BlockRect(shown.X / subW, shown.Y / subH, frame.ChromaWidth, frame.ChromaHeight);
        var blue = new H265ConvertPlane(picture.Cb, picture.ChromaWidth, area, OutputSubsampling / subW, OutputSubsampling / subH, frame.U, sps.BitDepthChroma);
        CopyRows(blue);
        CopyRows(blue with { Plane = picture.Cr, Target = frame.V });
        return frame;
    }

    /// <summary>Copies the rows of an area of a plane, reducing the samples to 8 bits.</summary>
    /// <param name="copy">The plane, the area to copy, its steps and the output.</param>
    private static void CopyRows(in H265ConvertPlane copy)
    {
        var (plane, stride, area, stepX, stepY, target, bitDepth) = copy;
        var shift = bitDepth - OutputBits;
        var round = shift > 0 ? 1 << (shift - 1) : 0;
        for (var row = 0; row < area.Height; row++)
        {
            var source = ((area.Y + (row * stepY)) * stride) + area.X;
            var destination = row * area.Width;
            if (shift == 0 && stepX == 1)
            {
                NarrowRow(plane.AsSpan(source, area.Width), target.AsSpan(destination, area.Width));
                continue;
            }

            for (var column = 0; column < area.Width; column++)
            {
                var value = (plane[source + (column * stepX)] + round) >> shift;
                target[destination + column] = (byte)Math.Min(value, byte.MaxValue);
            }
        }
    }

    /// <summary>Narrows a row of samples that already fit in 8 bits.</summary>
    /// <param name="source">The samples, none above 255.</param>
    /// <param name="target">Receives the bytes; the same length.</param>
    private static void NarrowRow(ReadOnlySpan<ushort> source, Span<byte> target)
    {
        var index = 0;
        if (Vector128.IsHardwareAccelerated)
        {
            var step = Vector128<ushort>.Count;
            var both = step + step;
            ref var from = ref MemoryMarshal.GetReference(source);
            ref var to = ref MemoryMarshal.GetReference(target);
            for (; index <= source.Length - both; index += both)
            {
                var low = Vector128.LoadUnsafe(ref from, (nuint)index);
                var high = Vector128.LoadUnsafe(ref from, (nuint)(index + step));
                Vector128.Narrow(low, high).StoreUnsafe(ref to, (nuint)index);
            }
        }

        for (; index < source.Length; index++)
        {
            target[index] = (byte)source[index];
        }
    }
}
