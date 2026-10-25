// Copyright (c) 2026 Glenn Watson. All rights reserved.
// Glenn Watson licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Diagnostics;
using System.Runtime.InteropServices;

namespace HyperCodecs.H265;

/// <summary>Where a slice segment sits in its picture and in its payload. It is read into again for each segment, so its list keeps its storage.</summary>
[DebuggerDisplay("H265SliceSegment: address {Address}")]
internal sealed class H265SliceSegment
{
    /// <summary>The size in bytes of each subset of slice data but the last.</summary>
    private readonly List<int> _entryPoints = [];

    /// <summary>Gets or sets a value indicating whether it is the first segment of the picture.</summary>
    internal bool First { get; set; }

    /// <summary>Gets or sets a value indicating whether pictures waiting for output are dropped when this IRAP picture starts.</summary>
    internal bool NoOutputOfPriorPics { get; set; }

    /// <summary>Gets or sets the number of the picture parameter set in use.</summary>
    internal int PpsId { get; set; }

    /// <summary>Gets or sets a value indicating whether it continues the slice before it and takes the settings of that slice.</summary>
    internal bool Dependent { get; set; }

    /// <summary>Gets or sets the raster scan address of its first coding tree block.</summary>
    internal int Address { get; set; }

    /// <summary>Gets or sets the index in the payload of the first byte of slice data.</summary>
    internal int DataOffset { get; set; }

    /// <summary>Gets the size in bytes of each subset of slice data but the last, counting emulation prevention bytes, which the reader fills.</summary>
    internal List<int> EntryPointList => _entryPoints;

    /// <summary>Gets the size in bytes of each subset of slice data but the last, counting emulation prevention bytes.</summary>
    internal ReadOnlySpan<int> EntryPoints => CollectionsMarshal.AsSpan(_entryPoints);

    /// <summary>Makes the segment the same as another.</summary>
    /// <param name="source">The segment to copy.</param>
    internal void CopyFrom(H265SliceSegment source)
    {
        First = source.First;
        NoOutputOfPriorPics = source.NoOutputOfPriorPics;
        PpsId = source.PpsId;
        Dependent = source.Dependent;
        Address = source.Address;
        DataOffset = source.DataOffset;
        _entryPoints.Clear();
        _entryPoints.AddRange(source.EntryPoints);
    }
}
