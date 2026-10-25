// Copyright (c) 2026 Glenn Watson. All rights reserved.
// Glenn Watson licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace HyperCodecs.H264;

/// <summary>Derives and predicts the direct and sub-partitioned 8x8 groups of B macroblocks, which CAVLC and CABAC slices handle alike.</summary>
internal static class H264BDirect
{
    /// <summary>The 8x8 partitions of a macroblock.</summary>
    private const int Groups = 4;

    /// <summary>Derives and predicts all four 8x8 groups of the macroblock directly.</summary>
    /// <param name="motion">The motion of the macroblock being decoded.</param>
    /// <param name="inference">Whether direct prediction works on 8x8 blocks.</param>
    internal static void Predict(H264BMotion motion, bool inference)
    {
        for (var group = 0; group < Groups; group++)
        {
            motion.Direct(group);
            motion.MarkAbsent(0, H264PartitionGeometry.Group(group));
            motion.MarkAbsent(1, H264PartitionGeometry.Group(group));
        }

        if (motion.CompensateUniform())
        {
            return;
        }

        for (var group = 0; group < Groups; group++)
        {
            CompensateDirect(motion, group, inference);
        }
    }

    /// <summary>Predicts the samples of an 8x8 group after its motion has been read.</summary>
    /// <param name="motion">The motion of the macroblock being decoded.</param>
    /// <param name="group">The group, 0 to 3.</param>
    /// <param name="subType">The group's sub_mb_type.</param>
    /// <param name="inference">Whether direct prediction works on 8x8 blocks.</param>
    internal static void CompensateGroup(H264BMotion motion, int group, int subType, bool inference)
    {
        if (subType == H264BTypes.SubDirect)
        {
            CompensateDirect(motion, group, inference);
            return;
        }

        var shape = H264BTypes.SubShapeOf(subType);
        for (var part = 0; part < H264PartitionGeometry.SubPartitionCount(shape); part++)
        {
            motion.Compensate(H264PartitionGeometry.InGroup(group, shape, part));
        }
    }

    /// <summary>Predicts the samples of a direct 8x8 group, in 8x8 blocks or 4x4 blocks according to the sequence.</summary>
    /// <param name="motion">The motion of the macroblock being decoded.</param>
    /// <param name="group">The group, 0 to 3.</param>
    /// <param name="inference">Whether direct prediction works on 8x8 blocks.</param>
    private static void CompensateDirect(H264BMotion motion, int group, bool inference)
    {
        var whole = H264PartitionGeometry.Group(group);
        if (inference)
        {
            motion.Compensate(whole);
            return;
        }

        for (var part = 0; part < Groups; part++)
        {
            motion.Compensate(new(whole.Column + (part & 1), whole.Row + (part >> 1), 1, 1, H264MotionPrediction.Plain));
        }
    }
}
