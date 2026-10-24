// Copyright (c) 2026 Glenn Watson. All rights reserved.
// Glenn Watson licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Runtime.CompilerServices;

namespace HyperCodecs.H264;

/// <summary>
/// Works out the boundary strength of every edge segment of an inter macroblock (clause 8.7.2.1 of ITU-T H.264): 4 next to an intra macroblock, 2 next to
/// coefficients, otherwise 1 where the motion differs. The motion of the macroblock and of the blocks beside it is read once, and the coefficient flags are
/// tested as bits of one mask.
/// </summary>
internal static class H264InterStrength
{
    /// <summary>The 4x4 blocks along a macroblock side.</summary>
    private const int Side = 4;

    /// <summary>The 4x4 blocks along the side of an 8x8 group.</summary>
    private const int Half = 2;

    /// <summary>The 4x4 blocks of a macroblock.</summary>
    private const int Blocks = Side * Side;

    /// <summary>The bits of the four 4x4 blocks of an 8x8 block in a coefficient mask.</summary>
    private const int EightByEightBits = 0x33;

    /// <summary>The bits of a block and the one to its right.</summary>
    private const int NextBlock = 0x3;

    /// <summary>The bits of a block and the one below it.</summary>
    private const int BlockBelow = 0x11;

    /// <summary>The strength next to an intra macroblock.</summary>
    private const int IntraMacroblockEdge = 4;

    /// <summary>The strength next to coefficients.</summary>
    private const int CoefficientEdge = 2;

    /// <summary>The blocks read from the neighbours: the right column of the left macroblock then the bottom row of the one above.</summary>
    private const int NeighbourBlocks = Side * 2;

    /// <summary>Works out the strengths of an inter macroblock.</summary>
    /// <param name="picture">The frame.</param>
    /// <param name="address">The macroblock.</param>
    /// <param name="filtered">Whether its left and top macroblock edges are filtered.</param>
    /// <param name="strength">Receives 16 strengths for the vertical edges then 16 for the horizontal edges, each by edge then segment.</param>
    [SkipLocalsInit]
    internal static void Fill(H264Picture picture, int address, H264EdgeFlags filtered, Span<int> strength)
    {
        var self = new H264EdgeBlocks(address, CodedMask(picture, address), H264MotionCompare.IsUniform(picture, address));
        H264EdgeBlocks? left = filtered.Left ? Neighbour(picture, address - 1) : null;
        H264EdgeBlocks? top = filtered.Top ? Neighbour(picture, address - picture.MbWidth) : null;
        Span<H264BlockMotion> own = stackalloc H264BlockMotion[Blocks];
        Span<H264BlockMotion> across = stackalloc H264BlockMotion[NeighbourBlocks];
        GatherOwn(picture, self, own);
        GatherAcross(picture, left, top, across);

        // A macroblock whose blocks all move alike and have no coefficients has no strength inside it.
        var edges = self.Uniform && self.Coded == 0 ? 1 : Side;
        if (edges == 1)
        {
            strength.Clear();
        }

        var inner = picture.Transform8x8[address] ? Half : 1;
        VerticalEdges(self, left, own, across, new(edges, inner), strength);
        HorizontalEdges(self, top, own, across, new(edges, inner), strength[Blocks..]);
    }

    /// <summary>Finds which 4x4 blocks of a macroblock are in a transform block with non-zero coefficients: the block, or its 8x8 block with the 8x8 transform.</summary>
    /// <param name="picture">The frame.</param>
    /// <param name="address">The macroblock.</param>
    /// <returns>One bit for each block, row after row.</returns>
    private static int CodedMask(H264Picture picture, int address)
    {
        var counts = picture.Counts.AsSpan(address * H264Picture.CountsPerMb, Blocks);
        if (!picture.Transform8x8[address])
        {
            var mask = 0;
            for (var bit = 0; bit < counts.Length; bit++)
            {
                mask |= counts[bit] != 0 ? 1 << bit : 0;
            }

            return mask;
        }

        var coded = 0;
        for (var group = 0; group < Side; group++)
        {
            var first = ((group >> 1) * Half * Side) + ((group & 1) * Half);
            if ((counts[first] | counts[first + 1] | counts[first + Side] | counts[first + Side + 1]) != 0)
            {
                coded |= EightByEightBits << first;
            }
        }

        return coded;
    }

    /// <summary>Gathers what the strength of an edge between macroblocks needs to know about the macroblock on the far side.</summary>
    /// <param name="picture">The frame.</param>
    /// <param name="address">The neighbouring macroblock.</param>
    /// <returns>Its address and coefficient flags; an intra macroblock has flags of -1.</returns>
    private static H264EdgeBlocks Neighbour(H264Picture picture, int address) =>
        picture.Kinds[address] == H264MbKind.Inter ? new(address, CodedMask(picture, address), false) : new(address, -1, false);

    /// <summary>Reads the motion of the 16 blocks of the macroblock; when they all move alike only the first is read.</summary>
    /// <param name="picture">The frame.</param>
    /// <param name="self">The macroblock.</param>
    /// <param name="own">Receives the motion of each block, row after row.</param>
    private static void GatherOwn(H264Picture picture, H264EdgeBlocks self, Span<H264BlockMotion> own)
    {
        if (self.Uniform)
        {
            own.Fill(H264MotionCompare.Motion(picture, new(self.Address, 0, 0)));
            return;
        }

        for (var row = 0; row < Side; row++)
        {
            for (var column = 0; column < Side; column++)
            {
                own[(row * Side) + column] = H264MotionCompare.Motion(picture, new(self.Address, column, row));
            }
        }
    }

    /// <summary>Reads the motion of the blocks beside the left and top edges, for the neighbours that are inter coded.</summary>
    /// <param name="picture">The frame.</param>
    /// <param name="left">The macroblock on the left, or <see langword="null"/>.</param>
    /// <param name="top">The macroblock above, or <see langword="null"/>.</param>
    /// <param name="across">Receives the right column of the left macroblock then the bottom row of the one above.</param>
    private static void GatherAcross(H264Picture picture, H264EdgeBlocks? left, H264EdgeBlocks? top, Span<H264BlockMotion> across)
    {
        if (left is { Coded: >= 0 } leftBlocks)
        {
            for (var row = 0; row < Side; row++)
            {
                across[row] = H264MotionCompare.Motion(picture, new(leftBlocks.Address, Side - 1, row));
            }
        }

        if (top is { Coded: >= 0 } topBlocks)
        {
            for (var column = 0; column < Side; column++)
            {
                across[Side + column] = H264MotionCompare.Motion(picture, new(topBlocks.Address, column, Side - 1));
            }
        }
    }

    /// <summary>Works out the strengths of the vertical edges.</summary>
    /// <param name="self">The macroblock.</param>
    /// <param name="left">The macroblock on the left, or <see langword="null"/> when its edge is not filtered.</param>
    /// <param name="own">The motion of the blocks of the macroblock.</param>
    /// <param name="across">The motion of the blocks beside its edges.</param>
    /// <param name="shape">How many edges to work out and the step between the edges that are, which is 2 with the 8x8 transform.</param>
    /// <param name="strength">Receives the strengths, by edge then segment.</param>
    private static void VerticalEdges(H264EdgeBlocks self, H264EdgeBlocks? left, ReadOnlySpan<H264BlockMotion> own, ReadOnlySpan<H264BlockMotion> across, H264EdgeShape shape, Span<int> strength)
    {
        for (var segment = 0; segment < Side; segment++)
        {
            var row = segment * Side;
            strength[segment] = left is { } other
                ? MacroblockEdge(self.Coded >> row, other.Coded >> (row + Side - 1), own[row], across[segment])
                : 0;
            for (var edge = 1; edge < shape.Edges; edge++)
            {
                strength[(edge * Side) + segment] = edge % shape.Step != 0
                    ? 0
                    : InnerEdge(self.Coded >> (row + edge - 1), NextBlock, own[row + edge], own[row + edge - 1]);
            }
        }
    }

    /// <summary>Works out the strengths of the horizontal edges.</summary>
    /// <param name="self">The macroblock.</param>
    /// <param name="top">The macroblock above, or <see langword="null"/> when its edge is not filtered.</param>
    /// <param name="own">The motion of the blocks of the macroblock.</param>
    /// <param name="across">The motion of the blocks beside its edges.</param>
    /// <param name="shape">How many edges to work out and the step between the edges that are, which is 2 with the 8x8 transform.</param>
    /// <param name="strength">Receives the strengths, by edge then segment.</param>
    private static void HorizontalEdges(H264EdgeBlocks self, H264EdgeBlocks? top, ReadOnlySpan<H264BlockMotion> own, ReadOnlySpan<H264BlockMotion> across, H264EdgeShape shape, Span<int> strength)
    {
        for (var segment = 0; segment < Side; segment++)
        {
            strength[segment] = top is { } other
                ? MacroblockEdge(self.Coded >> segment, other.Coded >> (segment + (Blocks - Side)), own[segment], across[Side + segment])
                : 0;
            for (var edge = 1; edge < shape.Edges; edge++)
            {
                var after = (edge * Side) + segment;
                strength[after] = edge % shape.Step != 0
                    ? 0
                    : InnerEdge(self.Coded >> (after - Side), BlockBelow, own[after], own[after - Side]);
            }
        }
    }

    /// <summary>Works out the strength of a segment of an edge between two blocks of one macroblock.</summary>
    /// <param name="flags">The coefficient flags shifted so bit 0 is the block before the edge.</param>
    /// <param name="pair">The bits of the two blocks beside the edge.</param>
    /// <param name="after">The motion of the block on the right of or below the edge.</param>
    /// <param name="before">The motion of the block on the left of or above the edge.</param>
    /// <returns>The strength, 0 to 2.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int InnerEdge(int flags, int pair, in H264BlockMotion after, in H264BlockMotion before) => Resolve((flags & pair) != 0, after, before);

    /// <summary>Works out the strength of a segment of an edge between two macroblocks.</summary>
    /// <param name="selfCoded">The macroblock's flags shifted so bit 0 is the block beside the edge.</param>
    /// <param name="otherCoded">The other macroblock's flags shifted so bit 0 is the block beside the edge; negative for an intra macroblock.</param>
    /// <param name="after">The motion of the macroblock's block.</param>
    /// <param name="before">The motion of the other macroblock's block.</param>
    /// <returns>The strength, 0 to 4.</returns>
    private static int MacroblockEdge(int selfCoded, int otherCoded, in H264BlockMotion after, in H264BlockMotion before) =>
        otherCoded < 0 ? IntraMacroblockEdge : Resolve(((selfCoded | otherCoded) & 1) != 0, after, before);

    /// <summary>Gives 2 where either block has coefficients, otherwise 1 where the motion differs.</summary>
    /// <param name="coded">Whether either block has coefficients.</param>
    /// <param name="after">One block's motion.</param>
    /// <param name="before">The other block's motion.</param>
    /// <returns>The strength, 0 to 2.</returns>
    private static int Resolve(bool coded, in H264BlockMotion after, in H264BlockMotion before) =>
        coded ? CoefficientEdge : MotionStrength(after, before);

    /// <summary>Gives 1 where the motion of two blocks differs.</summary>
    /// <param name="after">One block's motion.</param>
    /// <param name="before">The other block's motion.</param>
    /// <returns>1 or 0.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int MotionStrength(in H264BlockMotion after, in H264BlockMotion before) => H264MotionCompare.Differs(after, before) ? 1 : 0;
}
