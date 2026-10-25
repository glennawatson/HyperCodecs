// Copyright (c) 2026 Glenn Watson. All rights reserved.
// Glenn Watson licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Diagnostics;

namespace HyperCodecs.H265;

/// <summary>The prediction weights for the pictures of one reference list. The arrays hold the most pictures a list may have, and <see cref="Count"/> says how many are in use.</summary>
[DebuggerDisplay("H265WeightList: {Count} pictures")]
internal sealed class H265WeightList
{
    /// <summary>The most pictures in a reference list.</summary>
    internal const int MaxPictures = 16;

    /// <summary>The chroma components that have weights.</summary>
    internal const int ChromaComponents = 2;

    /// <summary>Gets the luma weight of each reference picture.</summary>
    internal int[] LumaWeight { get; } = new int[MaxPictures];

    /// <summary>Gets the luma offset of each reference picture.</summary>
    internal int[] LumaOffset { get; } = new int[MaxPictures];

    /// <summary>Gets the Cb then Cr weight of each reference picture.</summary>
    internal int[] ChromaWeight { get; } = new int[MaxPictures * ChromaComponents];

    /// <summary>Gets the Cb then Cr offset of each reference picture.</summary>
    internal int[] ChromaOffset { get; } = new int[MaxPictures * ChromaComponents];

    /// <summary>Gets the pictures in the list.</summary>
    internal int Count { get; private set; }

    /// <summary>Starts a list of a number of pictures, with every offset zero.</summary>
    /// <param name="count">The pictures in the list.</param>
    internal void Begin(int count)
    {
        Count = count;
        Array.Clear(LumaOffset);
        Array.Clear(ChromaOffset);
    }

    /// <summary>Makes the list the same as another.</summary>
    /// <param name="source">The list to copy.</param>
    internal void CopyFrom(H265WeightList source)
    {
        Count = source.Count;
        source.LumaWeight.CopyTo(LumaWeight, 0);
        source.LumaOffset.CopyTo(LumaOffset, 0);
        source.ChromaWeight.CopyTo(ChromaWeight, 0);
        source.ChromaOffset.CopyTo(ChromaOffset, 0);
    }
}
