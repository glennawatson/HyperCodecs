// Copyright (c) 2026 Glenn Watson. All rights reserved.
// Glenn Watson licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Diagnostics;

namespace HyperCodecs.H264;

/// <summary>
/// Everything one slice needs to be decoded on another thread: its payload, its header, its parameter sets, and its own copy of the reference lists, which
/// the decoder builds in arrays it reuses for the next slice. The slice jobs of a picture job are kept and filled again for the next picture, with their
/// arrays, so a slice does not allocate once the arrays are big enough.
/// </summary>
[DebuggerDisplay("H264SliceJob: slice {Number}, {Length} bytes")]
internal sealed class H264SliceJob
{
    /// <summary>The bytes the payload buffer starts with.</summary>
    private const int InitialSize = 1_024;

    /// <summary>The most pictures a reference list holds.</summary>
    private const int MaxListLength = 32;

    /// <summary>The copies of list 0 made so far, by length, so the next slice with a list of that length makes none.</summary>
    private readonly H264RefPicture[]?[] _kept0 = new H264RefPicture[]?[MaxListLength + 1];

    /// <summary>The copies of list 1 made so far, by length.</summary>
    private readonly H264RefPicture[]?[] _kept1 = new H264RefPicture[]?[MaxListLength + 1];

    /// <summary>Gets the payload of the slice's NAL unit without its header or emulation prevention bytes; only the first <see cref="Length"/> bytes are the payload.</summary>
    internal byte[] Rbsp { get; private set; } = new byte[InitialSize];

    /// <summary>Gets the length of the payload.</summary>
    internal int Length { get; private set; }

    /// <summary>Gets the slice header.</summary>
    internal H264SliceHeader Header { get; } = new();

    /// <summary>Gets the parameter sets the slice uses.</summary>
    internal H264SliceSets Sets { get; private set; }

    /// <summary>Gets the level scale factors of the slice's picture parameter set.</summary>
    internal H264LevelScale Scale { get; private set; } = null!;

    /// <summary>Gets the bit index where the slice data starts.</summary>
    internal long DataBit { get; private set; }

    /// <summary>Gets the slice's number in its picture, counting from 1.</summary>
    internal int Number { get; private set; }

    /// <summary>Gets the slice's copy of reference list 0.</summary>
    internal H264RefPicture[] List0 { get; private set; } = [];

    /// <summary>Gets the slice's copy of reference list 1, which a P slice leaves empty.</summary>
    internal H264RefPicture[] List1 { get; private set; } = [];

    /// <summary>Fills the job for a slice.</summary>
    /// <param name="payload">The payload of the slice's NAL unit without its header or emulation prevention bytes.</param>
    /// <param name="header">The slice header.</param>
    /// <param name="sets">The parameter sets the slice uses.</param>
    /// <param name="scale">The level scale factors of the picture parameter set.</param>
    /// <param name="dataBit">The bit index where the slice data starts.</param>
    /// <param name="number">The slice's number in its picture, counting from 1.</param>
    internal void Fill(ReadOnlySpan<byte> payload, H264SliceHeader header, H264SliceSets sets, H264LevelScale scale, long dataBit, int number)
    {
        if (Rbsp.Length < payload.Length)
        {
            Rbsp = new byte[payload.Length];
        }

        payload.CopyTo(Rbsp);
        Length = payload.Length;
        Header.CopyFrom(header);
        Sets = sets;
        Scale = scale;
        DataBit = dataBit;
        Number = number;
    }

    /// <summary>Takes a copy of the reference lists of the slice, so building the lists for the next slice does not change them.</summary>
    /// <param name="first">List 0.</param>
    /// <param name="second">List 1, or <see langword="null"/> for a P slice.</param>
    internal void CopyLists(H264RefPicture[] first, H264RefPicture[]? second)
    {
        List0 = Copy(_kept0, first);
        List1 = second is null ? [] : Copy(_kept1, second);
    }

    /// <summary>Copies the entries of a list into the entries kept from earlier slices, which are used again for a list of the same length.</summary>
    /// <param name="kept">The copies kept from earlier slices, by length.</param>
    /// <param name="source">The list to copy.</param>
    /// <returns>The copy.</returns>
    private static H264RefPicture[] Copy(H264RefPicture[]?[] kept, H264RefPicture[] source)
    {
        var copy = kept[source.Length] ??= new H264RefPicture[source.Length];
        for (var index = 0; index < source.Length; index++)
        {
            var entry = source[index];
            var target = copy[index] ??= new(entry.Picture, entry.FrameNum);
            target.Assign(entry);
        }

        return copy;
    }
}
