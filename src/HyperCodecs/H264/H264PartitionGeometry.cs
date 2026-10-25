// Copyright (c) 2026 Glenn Watson. All rights reserved.
// Glenn Watson licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace HyperCodecs.H264;

/// <summary>Where the partitions and sub-partitions of an inter macroblock sit, in 4x4 blocks from the macroblock's top left corner.</summary>
internal static class H264PartitionGeometry
{
    /// <summary>The 4x4 blocks along a macroblock side.</summary>
    private const int Side = 4;

    /// <summary>The 4x4 blocks along the side of an 8x8 partition.</summary>
    private const int Half = 2;

    /// <summary>The shape code of a sub-macroblock split into two 8x4 blocks.</summary>
    private const int Shape8x4 = 1;

    /// <summary>The shape code of a sub-macroblock split into two 4x8 blocks.</summary>
    private const int Shape4x8 = 2;

    /// <summary>The shape code of a sub-macroblock split into four 4x4 blocks.</summary>
    private const int Shape4x4 = 3;

    /// <summary>The sub-partitions of a sub-macroblock split in two.</summary>
    private const int TwoParts = 2;

    /// <summary>The sub-partitions of a sub-macroblock split in four.</summary>
    private const int FourParts = 4;

    /// <summary>Gets a partition of a macroblock with one or two partitions.</summary>
    /// <param name="layout">The layout: 0 for 16x16, 1 for 16x8, 2 for 8x16.</param>
    /// <param name="index">The partition's number.</param>
    /// <returns>The partition.</returns>
    internal static H264Partition Macroblock(int layout, int index)
    {
        if (layout == H264BTypes.Shape16x16)
        {
            return new(0, 0, Side, Side, H264MotionPrediction.Plain);
        }

        if (layout == H264BTypes.Shape16x8)
        {
            return new(0, index * Half, Side, Half, index == 0 ? H264MotionPrediction.Upper16x8 : H264MotionPrediction.Lower16x8);
        }

        return new(index * Half, 0, Half, Side, index == 0 ? H264MotionPrediction.Left8x16 : H264MotionPrediction.Right8x16);
    }

    /// <summary>Gets the position of a sub-partition inside its 8x8 partition, in 4x4 blocks.</summary>
    /// <param name="shape">The shape: 0 for 8x8, 1 for 8x4, 2 for 4x8, 3 for 4x4.</param>
    /// <param name="index">The sub-partition's number.</param>
    /// <returns>The sub-partition.</returns>
    internal static H264Partition SubPartition(int shape, int index) => shape switch
    {
        Shape8x4 => new(0, index, Half, 1, H264MotionPrediction.Plain),
        Shape4x8 => new(index, 0, 1, Half, H264MotionPrediction.Plain),
        Shape4x4 => new(index & 1, index >> 1, 1, 1, H264MotionPrediction.Plain),
        _ => new(0, 0, Half, Half, H264MotionPrediction.Plain),
    };

    /// <summary>Gets how many sub-partitions a shape has.</summary>
    /// <param name="shape">The shape: 0 for 8x8, 1 for 8x4, 2 for 4x8, 3 for 4x4.</param>
    /// <returns>1, 2 or 4.</returns>
    internal static int SubPartitionCount(int shape) => shape switch
    {
        Shape8x4 or Shape4x8 => TwoParts,
        Shape4x4 => FourParts,
        _ => 1,
    };

    /// <summary>Gets the partition of an 8x8 group.</summary>
    /// <param name="group">The group, 0 to 3.</param>
    /// <returns>The partition.</returns>
    internal static H264Partition Group(int group) => new((group & 1) * Half, (group >> 1) * Half, Half, Half, H264MotionPrediction.Plain);

    /// <summary>Gets a sub-partition of an 8x8 group, in 4x4 blocks from the macroblock's top left corner.</summary>
    /// <param name="group">The group, 0 to 3.</param>
    /// <param name="shape">The shape: 0 for 8x8, 1 for 8x4, 2 for 4x8, 3 for 4x4.</param>
    /// <param name="index">The sub-partition's number.</param>
    /// <returns>The sub-partition.</returns>
    internal static H264Partition InGroup(int group, int shape, int index)
    {
        var whole = Group(group);
        var sub = SubPartition(shape, index);
        return sub with { Column = whole.Column + sub.Column, Row = whole.Row + sub.Row };
    }
}
