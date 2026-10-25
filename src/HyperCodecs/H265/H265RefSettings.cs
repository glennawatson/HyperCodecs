// Copyright (c) 2026 Glenn Watson. All rights reserved.
// Glenn Watson licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Diagnostics;

namespace HyperCodecs.H265;

/// <summary>How a P or B slice builds and uses its reference lists. It is read into again for each segment, so it keeps its arrays.</summary>
[DebuggerDisplay("H265RefSettings: {NumRefIdxL0} and {NumRefIdxL1} pictures")]
internal sealed class H265RefSettings
{
    /// <summary>The most pictures in a reference list.</summary>
    internal const int MaxPictures = 16;

    /// <summary>The entries picked for list 0, which count only when <see cref="HasModificationL0"/> is set.</summary>
    private readonly int[] _modificationL0 = new int[MaxPictures];

    /// <summary>The entries picked for list 1, which count only when <see cref="HasModificationL1"/> is set.</summary>
    private readonly int[] _modificationL1 = new int[MaxPictures];

    /// <summary>Gets or sets the pictures in list 0.</summary>
    internal int NumRefIdxL0 { get; set; }

    /// <summary>Gets or sets the pictures in list 1.</summary>
    internal int NumRefIdxL1 { get; set; }

    /// <summary>Gets or sets a value indicating whether list 0 is picked entry by entry.</summary>
    internal bool HasModificationL0 { get; set; }

    /// <summary>Gets or sets a value indicating whether list 1 is picked entry by entry.</summary>
    internal bool HasModificationL1 { get; set; }

    /// <summary>Gets or sets a value indicating whether the motion vector difference of list 1 prediction is zero and not coded.</summary>
    internal bool MvdL1Zero { get; set; }

    /// <summary>Gets or sets a value indicating whether the CABAC initialisation tables of P and B slices are swapped.</summary>
    internal bool CabacInit { get; set; }

    /// <summary>Gets or sets a value indicating whether the co-located picture is picked from list 0.</summary>
    internal bool CollocatedFromL0 { get; set; } = true;

    /// <summary>Gets or sets the index of the co-located picture in its list.</summary>
    internal int CollocatedRefIdx { get; set; }

    /// <summary>Gets or sets the most merge candidates.</summary>
    internal int MaxMergeCand { get; set; }

    /// <summary>Gets the entries picked for list 0, or <see langword="null"/> to use the default order; the array may be longer than the list.</summary>
    internal int[]? ModificationL0 => HasModificationL0 ? _modificationL0 : null;

    /// <summary>Gets the entries picked for list 1, or <see langword="null"/> to use the default order; the array may be longer than the list.</summary>
    internal int[]? ModificationL1 => HasModificationL1 ? _modificationL1 : null;

    /// <summary>Gets the array the reader fills with the entries of list 0.</summary>
    internal int[] ModificationStorageL0 => _modificationL0;

    /// <summary>Gets the array the reader fills with the entries of list 1.</summary>
    internal int[] ModificationStorageL1 => _modificationL1;

    /// <summary>Sets the settings of a slice that has no reference lists.</summary>
    internal void Clear()
    {
        NumRefIdxL0 = 0;
        NumRefIdxL1 = 0;
        HasModificationL0 = false;
        HasModificationL1 = false;
        MvdL1Zero = false;
        CabacInit = false;
        CollocatedFromL0 = true;
        CollocatedRefIdx = 0;
        MaxMergeCand = 0;
    }

    /// <summary>Makes the settings the same as another's.</summary>
    /// <param name="source">The settings to copy.</param>
    internal void CopyFrom(H265RefSettings source)
    {
        NumRefIdxL0 = source.NumRefIdxL0;
        NumRefIdxL1 = source.NumRefIdxL1;
        HasModificationL0 = source.HasModificationL0;
        HasModificationL1 = source.HasModificationL1;
        MvdL1Zero = source.MvdL1Zero;
        CabacInit = source.CabacInit;
        CollocatedFromL0 = source.CollocatedFromL0;
        CollocatedRefIdx = source.CollocatedRefIdx;
        MaxMergeCand = source.MaxMergeCand;
        source._modificationL0.CopyTo(_modificationL0, 0);
        source._modificationL1.CopyTo(_modificationL1, 0);
    }
}
