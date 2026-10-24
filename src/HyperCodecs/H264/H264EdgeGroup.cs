// Copyright (c) 2026 Glenn Watson. All rights reserved.
// Glenn Watson licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace HyperCodecs.H264;

/// <summary>What the vector deblocking filter needs to know about eight samples along an edge: the strengths of the edge's segments, which group of eight this is, and the thresholds.</summary>
internal readonly ref struct H264EdgeGroup
{
    /// <summary>Initializes a new instance of the <see cref="H264EdgeGroup"/> struct.</summary>
    /// <param name="strengths">The boundary strengths of the four segments of the edge.</param>
    /// <param name="index">Which group of eight samples along the edge this is.</param>
    /// <param name="limits">The thresholds of the edge.</param>
    /// <param name="chroma">Whether the plane is chroma.</param>
    internal H264EdgeGroup(ReadOnlySpan<int> strengths, int index, H264EdgeLimits limits, bool chroma)
    {
        Strengths = strengths;
        Index = index;
        Limits = limits;
        Chroma = chroma;
    }

    /// <summary>Gets the boundary strengths of the four segments of the edge.</summary>
    internal ReadOnlySpan<int> Strengths { get; }

    /// <summary>Gets which group of eight samples along the edge this is.</summary>
    internal int Index { get; }

    /// <summary>Gets the thresholds of the edge.</summary>
    internal H264EdgeLimits Limits { get; }

    /// <summary>Gets a value indicating whether the plane is chroma.</summary>
    internal bool Chroma { get; }
}
