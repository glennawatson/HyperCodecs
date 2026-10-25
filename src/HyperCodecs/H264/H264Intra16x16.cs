// Copyright (c) 2026 Glenn Watson. All rights reserved.
// Glenn Watson licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace HyperCodecs.H264;

/// <summary>The four Intra 16x16 prediction modes of clause 8.3.3 of ITU-T H.264, for a luma block of 16x16 samples.</summary>
internal static class H264Intra16x16
{
    /// <summary>The vertical mode.</summary>
    internal const int Vertical = 0;

    /// <summary>The horizontal mode.</summary>
    internal const int Horizontal = 1;

    /// <summary>The DC mode.</summary>
    internal const int Dc = 2;

    /// <summary>The plane mode.</summary>
    internal const int Plane = 3;

    /// <summary>The size of the block.</summary>
    private const int Size = 16;

    /// <summary>The prediction of a block with no neighbours.</summary>
    private const int Mid = 128;

    /// <summary>Half the size of the block.</summary>
    private const int Half = 8;

    /// <summary>The shift of the mean of 32 neighbours.</summary>
    private const int BothShift = 5;

    /// <summary>The shift of the mean of 16 neighbours.</summary>
    private const int OneShift = 4;

    /// <summary>The multiplier of the plane gradients.</summary>
    private const int PlaneScale = 5;

    /// <summary>Predicts a 16x16 block into the picture.</summary>
    /// <param name="plane">The luma plane.</param>
    /// <param name="offset">The index of the block's first sample.</param>
    /// <param name="stride">The bytes in a row.</param>
    /// <param name="mode">The prediction mode.</param>
    /// <param name="neighbours">Which neighbours may be used.</param>
    internal static void Predict(byte[] plane, int offset, int stride, int mode, H264Neighbours neighbours)
    {
        switch (mode)
        {
            case Vertical:
            {
                H264IntraBlocks.CopyAbove(plane, offset, stride, Size);
                break;
            }

            case Horizontal:
            {
                H264IntraBlocks.ReplicateLeft(plane, offset, stride, Size);
                break;
            }

            case Dc:
            {
                var value = DcValue(plane, offset, stride, neighbours);
                for (var y = 0; y < Size; y++)
                {
                    plane.AsSpan(offset + (y * stride), Size).Fill((byte)value);
                }

                break;
            }

            default:
            {
                H264IntraBlocks.PredictPlane(plane, offset, stride, Size, PlaneScale);
                break;
            }
        }
    }

    /// <summary>Works out the DC prediction.</summary>
    /// <param name="plane">The luma plane.</param>
    /// <param name="offset">The index of the block's first sample.</param>
    /// <param name="stride">The bytes in a row.</param>
    /// <param name="neighbours">Which neighbours may be used.</param>
    /// <returns>The value every sample takes.</returns>
    private static int DcValue(byte[] plane, int offset, int stride, H264Neighbours neighbours)
    {
        var top = 0;
        var left = 0;
        for (var index = 0; index < Size; index++)
        {
            top += neighbours.Top ? plane[offset - stride + index] : 0;
            left += neighbours.Left ? plane[offset + (index * stride) - 1] : 0;
        }

        if (neighbours.Top && neighbours.Left)
        {
            return (top + left + Size) >> BothShift;
        }

        if (neighbours.Left)
        {
            return (left + Half) >> OneShift;
        }

        return neighbours.Top ? (top + Half) >> OneShift : Mid;
    }
}
