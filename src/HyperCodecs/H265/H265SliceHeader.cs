// Copyright (c) 2026 Glenn Watson. All rights reserved.
// Glenn Watson licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Diagnostics;
using System.Runtime.InteropServices;

namespace HyperCodecs.H265;

/// <summary>
/// A slice segment header (7.3.6.1). A dependent segment keeps the settings of the slice before it. One header is read into again for each segment, so its
/// lists keep their storage and reading a header allocates nothing.
/// </summary>
[DebuggerDisplay("H265SliceHeader: type {SliceType} at {Segment.Address}")]
internal sealed class H265SliceHeader
{
    /// <summary>The short-term set the header owns, which holds the set when the segment wrote it out.</summary>
    private readonly H265ShortTermRps _ownedShortTerm = new();

    /// <summary>The long-term reference pictures.</summary>
    private readonly List<H265LongTermRef> _longTerm = [];

    /// <summary>The explicit prediction weights, which count only when <see cref="HasWeights"/> is set.</summary>
    private readonly H265PredWeights _weights = new();

    /// <summary>Initializes a new instance of the <see cref="H265SliceHeader"/> class.</summary>
    internal H265SliceHeader() => ShortTerm = _ownedShortTerm;

    /// <summary>Gets where the segment sits.</summary>
    internal H265SliceSegment Segment { get; } = new();

    /// <summary>Gets or sets the slice type: 0 for B, 1 for P and 2 for I.</summary>
    internal int SliceType { get; set; }

    /// <summary>Gets or sets a value indicating whether the picture is output.</summary>
    internal bool PicOutput { get; set; }

    /// <summary>Gets or sets the low bits of the picture order count.</summary>
    internal int PocLsb { get; set; }

    /// <summary>Gets the short-term reference picture set: one of the sequence parameter set's, or the header's own.</summary>
    internal H265ShortTermRps ShortTerm { get; private set; }

    /// <summary>Gets or sets a value indicating whether temporal motion vector prediction may be used.</summary>
    internal bool TemporalMvp { get; set; }

    /// <summary>Gets or sets a value indicating whether luma sample adaptive offset is on.</summary>
    internal bool SaoLuma { get; set; }

    /// <summary>Gets or sets a value indicating whether chroma sample adaptive offset is on.</summary>
    internal bool SaoChroma { get; set; }

    /// <summary>Gets the reference list settings.</summary>
    internal H265RefSettings Refs { get; } = new();

    /// <summary>Gets or sets a value indicating whether the segment sends explicit prediction weights.</summary>
    internal bool HasWeights { get; set; }

    /// <summary>Gets or sets the quantiser settings.</summary>
    internal H265SliceQuant Quant { get; set; }

    /// <summary>Gets or sets the loop filter settings.</summary>
    internal H265SliceDeblocking Deblocking { get; set; }

    /// <summary>Gets the explicit prediction weights, or <see langword="null"/>.</summary>
    internal H265PredWeights? Weights => HasWeights ? _weights : null;

    /// <summary>Gets the object that holds the weights, which the reader fills after setting <see cref="HasWeights"/>.</summary>
    internal H265PredWeights WeightStorage => _weights;

    /// <summary>Gets the set the header owns, which the reader fills before calling <see cref="UseOwnedShortTerm"/>.</summary>
    internal H265ShortTermRps OwnedShortTerm => _ownedShortTerm;

    /// <summary>Gets the long-term reference pictures, which the reader fills.</summary>
    internal List<H265LongTermRef> LongTermList => _longTerm;

    /// <summary>Gets the long-term reference pictures.</summary>
    internal ReadOnlySpan<H265LongTermRef> LongTerm => CollectionsMarshal.AsSpan(_longTerm);

    /// <summary>Gets the pictures the current picture refers to: those of the sets that are marked used.</summary>
    internal int NumPicTotalCurr
    {
        get
        {
            var count = ShortTerm.UsedCount;
            foreach (var entry in LongTerm)
            {
                if (entry.Used)
                {
                    count++;
                }
            }

            return count;
        }
    }

    /// <summary>Makes the header use a set from the sequence parameter set.</summary>
    /// <param name="set">The set.</param>
    internal void UseSharedShortTerm(H265ShortTermRps set) => ShortTerm = set;

    /// <summary>Makes the header use the set it owns.</summary>
    internal void UseOwnedShortTerm() => ShortTerm = _ownedShortTerm;

    /// <summary>Empties the header, ready to be read into.</summary>
    internal void Clear()
    {
        _ownedShortTerm.Clear();
        ShortTerm = _ownedShortTerm;
        _longTerm.Clear();
        HasWeights = false;
        PocLsb = 0;
        TemporalMvp = false;
        Refs.Clear();
        Quant = default;
        Deblocking = default;
    }

    /// <summary>Makes the header the same as another, with the set held by the header itself.</summary>
    /// <param name="source">The header to copy.</param>
    internal void CopyFrom(H265SliceHeader source)
    {
        Segment.CopyFrom(source.Segment);
        SliceType = source.SliceType;
        PicOutput = source.PicOutput;
        PocLsb = source.PocLsb;
        _ownedShortTerm.CopyFrom(source.ShortTerm);
        ShortTerm = _ownedShortTerm;
        _longTerm.Clear();
        _longTerm.AddRange(source.LongTerm);
        TemporalMvp = source.TemporalMvp;
        SaoLuma = source.SaoLuma;
        SaoChroma = source.SaoChroma;
        Refs.CopyFrom(source.Refs);
        HasWeights = source.HasWeights;
        if (source.HasWeights)
        {
            _weights.CopyFrom(source._weights);
        }

        Quant = source.Quant;
        Deblocking = source.Deblocking;
    }
}
