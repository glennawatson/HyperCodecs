// Copyright (c) 2026 Glenn Watson. All rights reserved.
// Glenn Watson licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Diagnostics;
using System.Runtime.InteropServices;

namespace HyperCodecs.H264;

/// <summary>How a picture marks the reference pictures. It is filled again for each slice, so its list keeps its storage.</summary>
[DebuggerDisplay("H264Marking: adaptive {Adaptive}")]
internal sealed class H264Marking
{
    /// <summary>The memory management control operations.</summary>
    private readonly List<H264Mmco> _operations = [];

    /// <summary>Gets or sets a value indicating whether an IDR picture drops the pictures that were not yet output.</summary>
    internal bool NoOutputOfPriorPics { get; set; }

    /// <summary>Gets or sets a value indicating whether an IDR picture is kept as a long-term reference.</summary>
    internal bool LongTermReference { get; set; }

    /// <summary>Gets or sets a value indicating whether the reference pictures are marked by operations rather than by the sliding window.</summary>
    internal bool Adaptive { get; set; }

    /// <summary>Gets the memory management control operations, which the caller fills.</summary>
    internal List<H264Mmco> OperationList => _operations;

    /// <summary>Gets the memory management control operations.</summary>
    internal ReadOnlySpan<H264Mmco> Operations => CollectionsMarshal.AsSpan(_operations);

    /// <summary>Makes the marking the same as another.</summary>
    /// <param name="source">The marking to copy.</param>
    internal void CopyFrom(H264Marking source)
    {
        NoOutputOfPriorPics = source.NoOutputOfPriorPics;
        LongTermReference = source.LongTermReference;
        Adaptive = source.Adaptive;
        _operations.Clear();
        _operations.AddRange(source.Operations);
    }

    /// <summary>Makes the marking mark nothing, ready to be read into.</summary>
    internal void Clear()
    {
        NoOutputOfPriorPics = false;
        LongTermReference = false;
        Adaptive = false;
        _operations.Clear();
    }
}
