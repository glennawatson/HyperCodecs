// Copyright (c) 2026 Glenn Watson. All rights reserved.
// Glenn Watson licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace HyperCodecs.H264;

/// <summary>Records the motion of P slice partitions in the picture so the partitions and macroblocks that follow can predict from it.</summary>
internal static class H264PMotionStore
{
    /// <summary>The 4x4 blocks along a macroblock side.</summary>
    private const int Side = 4;

    /// <summary>The 4x4 blocks along the side of an 8x8 partition.</summary>
    private const int Half = 2;

    /// <summary>The largest size of a motion vector difference kept for the contexts.</summary>
    private const int MaxStoredMvd = 255;

    /// <summary>Records the vector, reference index and reference picture of a partition.</summary>
    /// <param name="picture">The picture being decoded.</param>
    /// <param name="address">The address of the macroblock.</param>
    /// <param name="partition">The partition.</param>
    /// <param name="refIdx">The reference index.</param>
    /// <param name="refUid">The unique number of the reference picture.</param>
    /// <param name="vector">The motion vector.</param>
    internal static void Store(H264Picture picture, int address, H264Partition partition, int refIdx, int refUid, MotionVector vector)
    {
        for (var r = partition.Row; r < partition.Row + partition.Height; r++)
        {
            for (var c = partition.Column; c < partition.Column + partition.Width; c++)
            {
                var index = (address * H264Picture.BlocksPerMb) + (r * Side) + c;
                picture.MvX[index] = (short)vector.X;
                picture.MvY[index] = (short)vector.Y;
                var group = (address * H264Picture.GroupsPerMb) + ((r >> 1) * Half) + (c >> 1);
                picture.RefIdx[group] = (sbyte)refIdx;
                picture.RefUid[group] = refUid;
            }
        }
    }

    /// <summary>Records the sizes of the motion vector difference of a partition, which the CABAC contexts of later partitions read.</summary>
    /// <param name="picture">The picture being decoded.</param>
    /// <param name="address">The address of the macroblock.</param>
    /// <param name="partition">The partition.</param>
    /// <param name="difference">The motion vector difference that was read.</param>
    internal static void StoreDifference(H264Picture picture, int address, H264Partition partition, MotionVector difference)
    {
        var x = (byte)Math.Min(Math.Abs(difference.X), MaxStoredMvd);
        var y = (byte)Math.Min(Math.Abs(difference.Y), MaxStoredMvd);
        for (var r = partition.Row; r < partition.Row + partition.Height; r++)
        {
            for (var c = partition.Column; c < partition.Column + partition.Width; c++)
            {
                var index = (address * H264Picture.BlocksPerMb) + (r * Side) + c;
                picture.MvdX[index] = x;
                picture.MvdY[index] = y;
            }
        }
    }
}
