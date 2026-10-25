// Copyright (c) 2026 Glenn Watson. All rights reserved.
// Glenn Watson licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Diagnostics;

namespace HyperCodecs.H264;

/// <summary>A slice header. One header is read into again for each slice, so its lists keep their storage and reading a header allocates nothing.</summary>
[DebuggerDisplay("H264SliceHeader: first {FirstMb}, type {SliceType}, frame {Picture.FrameNum}")]
internal sealed class H264SliceHeader
{
    /// <summary>The slice type of a P slice.</summary>
    internal const int P = 0;

    /// <summary>The slice type of a B slice.</summary>
    internal const int B = 1;

    /// <summary>The slice type of an I slice.</summary>
    internal const int I = 2;

    /// <summary>The slice type of an SP slice.</summary>
    internal const int Sp = 3;

    /// <summary>The slice type of an SI slice.</summary>
    internal const int Si = 4;

    /// <summary>Gets or sets the nal_ref_idc of the slice's NAL unit.</summary>
    internal int NalRefIdc { get; set; }

    /// <summary>Gets or sets a value indicating whether the slice is part of an IDR picture.</summary>
    internal bool IsIdr { get; set; }

    /// <summary>Gets or sets the address of the slice's first macroblock.</summary>
    internal int FirstMb { get; set; }

    /// <summary>Gets or sets the slice type, 0 to 4.</summary>
    internal int SliceType { get; set; }

    /// <summary>Gets or sets the number of the picture parameter set the slice uses.</summary>
    internal int PpsId { get; set; }

    /// <summary>Gets or sets the part of the header that names the picture.</summary>
    internal H264PictureId Picture { get; set; }

    /// <summary>Gets the part of the header that says which pictures the slice refers to.</summary>
    internal H264RefSetup Refs { get; } = new();

    /// <summary>Gets how the picture marks the reference pictures.</summary>
    internal H264Marking Marking { get; } = new();

    /// <summary>Gets or sets the end of the header.</summary>
    internal H264SliceTail Tail { get; set; }

    /// <summary>Gets a value indicating whether the slice is an intra slice.</summary>
    internal bool IsIntra => SliceType is I or Si;

    /// <summary>Gets a value indicating whether the slice is a P slice.</summary>
    internal bool IsP => SliceType is P or Sp;

    /// <summary>Gets a value indicating whether the slice is a B slice.</summary>
    internal bool IsB => SliceType == B;

    /// <summary>Makes the header the same as another.</summary>
    /// <param name="source">The header to copy.</param>
    internal void CopyFrom(H264SliceHeader source)
    {
        NalRefIdc = source.NalRefIdc;
        IsIdr = source.IsIdr;
        FirstMb = source.FirstMb;
        SliceType = source.SliceType;
        PpsId = source.PpsId;
        Picture = source.Picture;
        Tail = source.Tail;
        Refs.CopyFrom(source.Refs);
        Marking.CopyFrom(source.Marking);
    }

    /// <summary>Empties the header's lists, ready to be read into.</summary>
    internal void Clear()
    {
        Refs.Clear();
        Marking.Clear();
    }
}
