// Copyright (c) 2026 Glenn Watson. All rights reserved.
// Glenn Watson licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Diagnostics;
using System.Runtime.InteropServices;

namespace HyperCodecs.H264;

/// <summary>The explicit prediction weights of a slice. The table is filled again for each slice that sends weights, so the lists keep their storage.</summary>
[DebuggerDisplay("H264WeightTable: luma denominator {LumaLog2Denom}")]
internal sealed class H264WeightTable
{
    /// <summary>The weights of reference list 0.</summary>
    private readonly List<H264Weight> _l0 = [];

    /// <summary>The weights of reference list 1.</summary>
    private readonly List<H264Weight> _l1 = [];

    /// <summary>Gets or sets the log2 of the denominator of the luma weights.</summary>
    internal int LumaLog2Denom { get; set; }

    /// <summary>Gets or sets the log2 of the denominator of the chroma weights.</summary>
    internal int ChromaLog2Denom { get; set; }

    /// <summary>Gets the weights of reference list 0, which the caller fills.</summary>
    internal List<H264Weight> List0 => _l0;

    /// <summary>Gets the weights of reference list 1, which the caller fills.</summary>
    internal List<H264Weight> List1 => _l1;

    /// <summary>Gets the weights of reference list 0.</summary>
    internal ReadOnlySpan<H264Weight> L0 => CollectionsMarshal.AsSpan(_l0);

    /// <summary>Gets the weights of reference list 1.</summary>
    internal ReadOnlySpan<H264Weight> L1 => CollectionsMarshal.AsSpan(_l1);

    /// <summary>Empties the table, ready to be filled.</summary>
    internal void Clear()
    {
        _l0.Clear();
        _l1.Clear();
    }

    /// <summary>Makes the table the same as another.</summary>
    /// <param name="source">The table to copy.</param>
    internal void CopyFrom(H264WeightTable source)
    {
        LumaLog2Denom = source.LumaLog2Denom;
        ChromaLog2Denom = source.ChromaLog2Denom;
        _l0.Clear();
        _l0.AddRange(source.L0);
        _l1.Clear();
        _l1.AddRange(source.L1);
    }
}
