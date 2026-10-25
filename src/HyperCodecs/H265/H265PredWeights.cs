// Copyright (c) 2026 Glenn Watson. All rights reserved.
// Glenn Watson licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Diagnostics;

namespace HyperCodecs.H265;

/// <summary>The explicit weighted prediction parameters of a slice (7.3.6.3). The weights are read into again for each segment, so they keep their arrays.</summary>
[DebuggerDisplay("H265PredWeights: luma denominator {LumaLog2Denom}")]
internal sealed class H265PredWeights
{
    /// <summary>The weights of the pictures in list 1.</summary>
    private readonly H265WeightList _list1 = new();

    /// <summary>Gets or sets the base 2 logarithm of the denominator of the luma weights.</summary>
    internal int LumaLog2Denom { get; set; }

    /// <summary>Gets or sets the base 2 logarithm of the denominator of the chroma weights.</summary>
    internal int ChromaLog2Denom { get; set; }

    /// <summary>Gets or sets a value indicating whether the slice has weights for list 1, which a P slice does not.</summary>
    internal bool HasList1 { get; set; }

    /// <summary>Gets the weights of the pictures in list 0.</summary>
    internal H265WeightList List0 { get; } = new();

    /// <summary>Gets the weights of the pictures in list 1, or <see langword="null"/> in a P slice.</summary>
    internal H265WeightList? List1 => HasList1 ? _list1 : null;

    /// <summary>Gets the list that holds the weights of list 1, which the reader fills after setting <see cref="HasList1"/>.</summary>
    internal H265WeightList List1Storage => _list1;

    /// <summary>Makes the weights the same as another's.</summary>
    /// <param name="source">The weights to copy.</param>
    internal void CopyFrom(H265PredWeights source)
    {
        LumaLog2Denom = source.LumaLog2Denom;
        ChromaLog2Denom = source.ChromaLog2Denom;
        HasList1 = source.HasList1;
        List0.CopyFrom(source.List0);
        if (source.HasList1)
        {
            _list1.CopyFrom(source._list1);
        }
    }
}
