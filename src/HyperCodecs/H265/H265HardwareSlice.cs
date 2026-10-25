// Copyright (c) 2026 Glenn Watson. All rights reserved.
// Glenn Watson licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Diagnostics;

namespace HyperCodecs.H265;

/// <summary>A slice segment kept for the hardware: its header, the reference lists built for it and where its NAL unit lies in the picture's bytes. It is filled again for each segment.</summary>
[DebuggerDisplay("H265HardwareSlice: address {Header.Segment.Address}, {Length} bytes")]
internal sealed class H265HardwareSlice
{
    /// <summary>Gets the segment header.</summary>
    internal H265SliceHeader Header { get; } = new();

    /// <summary>Gets the pictures of reference list 0.</summary>
    internal List<H265Picture> List0 { get; } = [];

    /// <summary>Gets the pictures of reference list 1.</summary>
    internal List<H265Picture> List1 { get; } = [];

    /// <summary>Gets the index in the picture's bytes of the segment's NAL unit.</summary>
    internal int Offset { get; private set; }

    /// <summary>Gets the length of the NAL unit.</summary>
    internal int Length { get; private set; }

    /// <summary>Fills the slice for a segment.</summary>
    /// <param name="header">The segment header.</param>
    /// <param name="list0">The pictures of reference list 0.</param>
    /// <param name="list1">The pictures of reference list 1.</param>
    /// <param name="offset">The index in the picture's bytes of the segment's NAL unit.</param>
    /// <param name="length">The length of the NAL unit.</param>
    internal void Fill(H265SliceHeader header, List<H265Picture> list0, List<H265Picture> list1, int offset, int length)
    {
        Header.CopyFrom(header);
        List0.Clear();
        List0.AddRange(list0);
        List1.Clear();
        List1.AddRange(list1);
        Offset = offset;
        Length = length;
    }
}
