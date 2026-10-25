// Copyright (c) 2026 Glenn Watson. All rights reserved.
// Glenn Watson licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Diagnostics;

namespace HyperCodecs.H265;

/// <summary>
/// Everything one slice segment needs to be decoded on another thread: its payload, its header, and its own copy of the reference lists, which the decoder
/// builds in lists it reuses for the next segment, with whether each reference was a long-term picture when the segment was read. The slice jobs of a
/// picture job are kept and filled again for the next picture, with their arrays.
/// </summary>
[DebuggerDisplay("H265SliceJob: segment at {Header.Segment.Address}, {Length} bytes")]
internal sealed class H265SliceJob
{
    /// <summary>The bytes the payload buffer starts with.</summary>
    private const int InitialSize = 1_024;

    /// <summary>The copies of list 0 made so far, by length, so the next segment with a list of that length makes none.</summary>
    private readonly H265Picture[]?[] _kept0 = new H265Picture[]?[H265SliceRefs.MaxReferences + 1];

    /// <summary>The copies of list 1 made so far, by length.</summary>
    private readonly H265Picture[]?[] _kept1 = new H265Picture[]?[H265SliceRefs.MaxReferences + 1];

    /// <summary>Gets the payload of the segment's NAL unit without its header or emulation prevention bytes; only the first <see cref="Length"/> bytes are the payload.</summary>
    internal byte[] Rbsp { get; private set; } = new byte[InitialSize];

    /// <summary>Gets the length of the payload.</summary>
    internal int Length { get; private set; }

    /// <summary>Gets the slice segment header.</summary>
    internal H265SliceHeader Header { get; } = new();

    /// <summary>Gets the index of the segment's slice in the picture, which <see cref="H265Picture.SliceRefs"/> is indexed by.</summary>
    internal int SliceIndex { get; private set; }

    /// <summary>Gets a value indicating whether no reference picture of the segment follows the current picture in output order.</summary>
    internal bool NoBackwardPred { get; private set; }

    /// <summary>Gets the pictures of reference list 0.</summary>
    internal H265Picture[] List0 { get; private set; } = [];

    /// <summary>Gets the pictures of reference list 1.</summary>
    internal H265Picture[] List1 { get; private set; } = [];

    /// <summary>Gets whether each picture of list 0 was a long-term picture when the segment was read.</summary>
    internal bool[] LongTerm0 { get; } = new bool[H265SliceRefs.MaxReferences];

    /// <summary>Gets whether each picture of list 1 was a long-term picture when the segment was read.</summary>
    internal bool[] LongTerm1 { get; } = new bool[H265SliceRefs.MaxReferences];

    /// <summary>Fills the job for a slice segment, taking a copy of the lists the context has just built for it.</summary>
    /// <param name="payload">The payload of the segment's NAL unit without its header or emulation prevention bytes.</param>
    /// <param name="header">The segment header.</param>
    /// <param name="context">The context whose reference lists, slice index and backward prediction flag belong to this segment.</param>
    internal void Fill(ReadOnlySpan<byte> payload, H265SliceHeader header, H265SliceContext context)
    {
        if (Rbsp.Length < payload.Length)
        {
            Rbsp = new byte[payload.Length];
        }

        payload.CopyTo(Rbsp);
        Length = payload.Length;
        Header.CopyFrom(header);
        SliceIndex = context.SliceIndex;
        NoBackwardPred = context.NoBackwardPred;
        List0 = Copy(_kept0, context.RefList0, LongTerm0);
        List1 = Copy(_kept1, context.RefList1, LongTerm1);
    }

    /// <summary>Copies a list, and the long-term flags of its pictures.</summary>
    /// <param name="kept">The arrays kept from earlier uses, by length, which are used again for a list of the same length.</param>
    /// <param name="source">The list.</param>
    /// <param name="flags">Receives the long-term flags.</param>
    /// <returns>The copy.</returns>
    private static H265Picture[] Copy(H265Picture[]?[] kept, List<H265Picture> source, bool[] flags)
    {
        var copy = kept[source.Count] ??= new H265Picture[source.Count];
        for (var index = 0; index < source.Count; index++)
        {
            copy[index] = source[index];
            flags[index] = source[index].IsLongTerm;
        }

        return copy;
    }
}
