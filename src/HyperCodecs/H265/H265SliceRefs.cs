// Copyright (c) 2026 Glenn Watson. All rights reserved.
// Glenn Watson licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace HyperCodecs.H265;

/// <summary>
/// The reference picture lists of one slice, as the picture order counts of their pictures and whether each is a long-term picture.
/// A picture keeps its instances and fills them again each time it is reused, so describing a slice allocates nothing.
/// </summary>
internal sealed class H265SliceRefs
{
    /// <summary>The most pictures a reference list holds.</summary>
    private const int MaxReferences = 16;

    /// <summary>Gets the order count of each picture in list 0.</summary>
    internal int[] Poc0 { get; } = new int[MaxReferences];

    /// <summary>Gets whether each picture in list 0 is long-term.</summary>
    internal bool[] LongTerm0 { get; } = new bool[MaxReferences];

    /// <summary>Gets the order count of each picture in list 1.</summary>
    internal int[] Poc1 { get; } = new int[MaxReferences];

    /// <summary>Gets whether each picture in list 1 is long-term.</summary>
    internal bool[] LongTerm1 { get; } = new bool[MaxReferences];

    /// <summary>Fills the instance from the two lists of a slice.</summary>
    /// <param name="list0">The pictures of list 0.</param>
    /// <param name="list1">The pictures of list 1.</param>
    internal void Set(List<H265Picture> list0, List<H265Picture> list1)
    {
        Fill(list0, Poc0, LongTerm0);
        Fill(list1, Poc1, LongTerm1);
    }

    /// <summary>Copies the order counts and long-term flags of a list.</summary>
    /// <param name="list">The list.</param>
    /// <param name="pocs">Receives the order counts.</param>
    /// <param name="flags">Receives the long-term flags.</param>
    private static void Fill(List<H265Picture> list, int[] pocs, bool[] flags)
    {
        for (var i = 0; i < list.Count; i++)
        {
            pocs[i] = list[i].Poc;
            flags[i] = list[i].IsLongTerm;
        }
    }
}
