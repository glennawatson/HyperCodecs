// Copyright (c) 2026 Glenn Watson. All rights reserved.
// Glenn Watson licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Diagnostics;
using System.Runtime.InteropServices;

namespace HyperCodecs.H264;

/// <summary>The part of a slice header that says which pictures the slice refers to and how it weights them. It is filled again for each slice, so its lists keep their storage.</summary>
[DebuggerDisplay("H264RefSetup: {NumRefIdxL0} and {NumRefIdxL1} pictures")]
internal sealed class H264RefSetup
{
    /// <summary>The operations that reorder reference list 0.</summary>
    private readonly List<H264RefModification> _modificationsL0 = [];

    /// <summary>The operations that reorder reference list 1.</summary>
    private readonly List<H264RefModification> _modificationsL1 = [];

    /// <summary>The explicit weights, which count only when <see cref="HasWeights"/> is set.</summary>
    private readonly H264WeightTable _table = new();

    /// <summary>Gets or sets a value indicating whether direct prediction in a B slice works from neighbours in space rather than in time.</summary>
    internal bool DirectSpatialMvPred { get; set; }

    /// <summary>Gets or sets the number of pictures in reference list 0.</summary>
    internal int NumRefIdxL0 { get; set; }

    /// <summary>Gets or sets the number of pictures in reference list 1.</summary>
    internal int NumRefIdxL1 { get; set; }

    /// <summary>Gets or sets a value indicating whether the slice sends explicit weights.</summary>
    internal bool HasWeights { get; set; }

    /// <summary>Gets the operations that reorder reference list 0, which the caller fills.</summary>
    internal List<H264RefModification> ListModificationsL0 => _modificationsL0;

    /// <summary>Gets the operations that reorder reference list 1, which the caller fills.</summary>
    internal List<H264RefModification> ListModificationsL1 => _modificationsL1;

    /// <summary>Gets the operations that reorder reference list 0.</summary>
    internal ReadOnlySpan<H264RefModification> ModificationsL0 => CollectionsMarshal.AsSpan(_modificationsL0);

    /// <summary>Gets the operations that reorder reference list 1.</summary>
    internal ReadOnlySpan<H264RefModification> ModificationsL1 => CollectionsMarshal.AsSpan(_modificationsL1);

    /// <summary>Gets the explicit weights, or <see langword="null"/> when the slice sends none.</summary>
    internal H264WeightTable? Weights => HasWeights ? _table : null;

    /// <summary>Gets the table that holds the weights, which the caller fills after setting <see cref="HasWeights"/>.</summary>
    internal H264WeightTable WeightStorage => _table;

    /// <summary>Makes the setup the same as another.</summary>
    /// <param name="source">The setup to copy.</param>
    internal void CopyFrom(H264RefSetup source)
    {
        DirectSpatialMvPred = source.DirectSpatialMvPred;
        NumRefIdxL0 = source.NumRefIdxL0;
        NumRefIdxL1 = source.NumRefIdxL1;
        HasWeights = source.HasWeights;
        _modificationsL0.Clear();
        _modificationsL0.AddRange(source.ModificationsL0);
        _modificationsL1.Clear();
        _modificationsL1.AddRange(source.ModificationsL1);
        if (source.HasWeights)
        {
            _table.CopyFrom(source._table);
        }
    }

    /// <summary>Empties the setup, ready to be read into.</summary>
    internal void Clear()
    {
        DirectSpatialMvPred = false;
        NumRefIdxL0 = 0;
        NumRefIdxL1 = 0;
        HasWeights = false;
        _modificationsL0.Clear();
        _modificationsL1.Clear();
        _table.Clear();
    }
}
