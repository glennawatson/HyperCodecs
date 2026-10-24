// Copyright (c) 2026 Glenn Watson. All rights reserved.
// Glenn Watson licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Runtime.CompilerServices;

namespace HyperCodecs.H264;

/// <summary>Tells whether the motion of two inter coded 4x4 blocks differs enough for the deblocking filter to give their edge strength 1 (clause 8.7.2.1 of ITU-T H.264).</summary>
internal static class H264MotionCompare
{
    /// <summary>The difference in a motion vector component, in quarter samples, that counts as different motion.</summary>
    private const int MotionLimit = 4;

    /// <summary>The 4x4 blocks along a macroblock side.</summary>
    private const int Side = 4;

    /// <summary>The 4x4 blocks along the side of an 8x8 group.</summary>
    private const int Half = 2;

    /// <summary>Tells whether every block of a macroblock has the same reference frames and vectors, so no edge inside it separates different motion.</summary>
    /// <param name="picture">The frame.</param>
    /// <param name="address">The macroblock.</param>
    /// <returns><see langword="true"/> when the reference frames and vectors of all its blocks are equal.</returns>
    internal static bool IsUniform(H264Picture picture, int address)
    {
        var vectors = address * H264Picture.BlocksPerMb;
        var groups = address * H264Picture.GroupsPerMb;
        return AllEqual(picture.Field0.MvX.AsSpan(vectors, H264Picture.BlocksPerMb))
            && AllEqual(picture.Field0.MvY.AsSpan(vectors, H264Picture.BlocksPerMb))
            && AllEqual(picture.Field1.MvX.AsSpan(vectors, H264Picture.BlocksPerMb))
            && AllEqual(picture.Field1.MvY.AsSpan(vectors, H264Picture.BlocksPerMb))
            && AllEqual(picture.Field0.RefUid.AsSpan(groups, H264Picture.GroupsPerMb))
            && AllEqual(picture.Field1.RefUid.AsSpan(groups, H264Picture.GroupsPerMb));
    }

    /// <summary>Tells whether every 8x8 group of a macroblock uses the same reference index in each list.</summary>
    /// <param name="picture">The frame.</param>
    /// <param name="address">The macroblock.</param>
    /// <returns><see langword="true"/> when the indices of the four groups are equal in both lists.</returns>
    internal static bool HasUniformIndices(H264Picture picture, int address)
    {
        var groups = address * H264Picture.GroupsPerMb;
        return AllEqual(picture.Field0.RefIdx.AsSpan(groups, H264Picture.GroupsPerMb)) && AllEqual(picture.Field1.RefIdx.AsSpan(groups, H264Picture.GroupsPerMb));
    }

    /// <summary>Tells whether the motion of two blocks differs.</summary>
    /// <param name="picture">The frame.</param>
    /// <param name="first">One block.</param>
    /// <param name="second">The other block.</param>
    /// <returns><see langword="true"/> when they use different reference frames, a different number of vectors, or vectors that are far apart.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool Differs(H264Picture picture, H264BlockAt first, H264BlockAt second) => Differs(Motion(picture, first), Motion(picture, second));

    /// <summary>Tells whether the motion of two blocks differs.</summary>
    /// <param name="a">One block's motion.</param>
    /// <param name="b">The other block's motion.</param>
    /// <returns><see langword="true"/> when they use different reference frames, a different number of vectors, or vectors that are far apart.</returns>
    internal static bool Differs(in H264BlockMotion a, in H264BlockMotion b)
    {
        if (a.Count != b.Count)
        {
            return true;
        }

        return a.Count == 1 ? a.OnlyUid != b.OnlyUid || Far(a.OnlyVector, b.OnlyVector) : PairDiffers(a, b);
    }

    /// <summary>Reads the motion of a 4x4 block for both lists.</summary>
    /// <param name="picture">The frame.</param>
    /// <param name="block">The block.</param>
    /// <returns>The reference frames and vectors.</returns>
    internal static H264BlockMotion Motion(H264Picture picture, H264BlockAt block)
    {
        var group = (block.Address * H264Picture.GroupsPerMb) + ((block.Row >> 1) * Half) + (block.Column >> 1);
        var index = (block.Address * H264Picture.BlocksPerMb) + (block.Row * Side) + block.Column;
        return new(
            picture.Field0.RefUid[group],
            new(picture.Field0.MvX[index], picture.Field0.MvY[index]),
            picture.Field1.RefUid[group],
            new(picture.Field1.MvX[index], picture.Field1.MvY[index]));
    }

    /// <summary>Compares two blocks that each use two vectors.</summary>
    /// <param name="a">One block's motion.</param>
    /// <param name="b">The other block's motion.</param>
    /// <returns><see langword="true"/> when they differ.</returns>
    private static bool PairDiffers(H264BlockMotion a, H264BlockMotion b)
    {
        var same = a.Uid0 == b.Uid0 && a.Uid1 == b.Uid1;
        if (!same && !(a.Uid0 == b.Uid1 && a.Uid1 == b.Uid0))
        {
            return true;
        }

        var straight = Far(a.Vector0, b.Vector0) || Far(a.Vector1, b.Vector1);
        var crossed = Far(a.Vector0, b.Vector1) || Far(a.Vector1, b.Vector0);
        if (a.Uid0 == a.Uid1)
        {
            return straight && crossed;
        }

        return same ? straight : crossed;
    }

    /// <summary>Tells whether every value of a run is the same as the first.</summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="values">The values.</param>
    /// <returns><see langword="true"/> when they are all equal.</returns>
    private static bool AllEqual<T>(ReadOnlySpan<T> values)
        where T : IEquatable<T> => values.IndexOfAnyExcept(values[0]) < 0;

    /// <summary>Tells whether two vectors are far enough apart to count as different motion.</summary>
    /// <param name="a">One vector.</param>
    /// <param name="b">The other vector.</param>
    /// <returns><see langword="true"/> when a component differs by a whole sample or more.</returns>
    private static bool Far(MotionVector a, MotionVector b) => Math.Abs(a.X - b.X) >= MotionLimit || Math.Abs(a.Y - b.Y) >= MotionLimit;
}
