// Copyright (c) 2026 Glenn Watson. All rights reserved.
// Glenn Watson licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace HyperCodecs.H265;

/// <summary>Works out a predicted short-term reference picture set from the set it refers to (equations 7-61 and 7-62).</summary>
internal readonly ref struct H265RpsPrediction
{
    /// <summary>The set that is predicted from.</summary>
    private readonly H265ShortTermRps _source;

    /// <summary>The offset added to every distance of the source.</summary>
    private readonly int _deltaRps;

    /// <summary>The used_by_curr_pic_flag of each source entry, then of the offset itself.</summary>
    private readonly ReadOnlySpan<bool> _used;

    /// <summary>The use_delta_flag of each source entry, then of the offset itself.</summary>
    private readonly ReadOnlySpan<bool> _useDelta;

    /// <summary>Initializes a new instance of the <see cref="H265RpsPrediction"/> struct.</summary>
    /// <param name="source">The set that is predicted from.</param>
    /// <param name="deltaRps">The offset added to every distance of the source.</param>
    /// <param name="used">The used_by_curr_pic_flag of each source entry, then of the offset itself.</param>
    /// <param name="useDelta">The use_delta_flag of each source entry, then of the offset itself.</param>
    internal H265RpsPrediction(H265ShortTermRps source, int deltaRps, ReadOnlySpan<bool> used, ReadOnlySpan<bool> useDelta)
    {
        _source = source;
        _deltaRps = deltaRps;
        _used = used;
        _useDelta = useDelta;
    }

    /// <summary>Builds the predicted set.</summary>
    /// <param name="target">Receives the set; what it held is replaced.</param>
    /// <exception cref="InvalidDataException">The set is larger than a decoded picture buffer.</exception>
    internal void Derive(H265ShortTermRps target)
    {
        target.Clear();
        var earlier = _source.EarlierCount;
        var later = _source.LaterCount;
        var self = earlier + later;

        for (var j = later - 1; j >= 0; j--)
        {
            AddEarlier(_source.DeltaPocS1[j] + _deltaRps, earlier + j, target);
        }

        AddEarlier(_deltaRps, self, target);
        for (var j = 0; j < earlier; j++)
        {
            AddEarlier(_source.DeltaPocS0[j] + _deltaRps, j, target);
        }

        for (var j = earlier - 1; j >= 0; j--)
        {
            AddLater(_source.DeltaPocS0[j] + _deltaRps, j, target);
        }

        AddLater(_deltaRps, self, target);
        for (var j = 0; j < later; j++)
        {
            AddLater(_source.DeltaPocS1[j] + _deltaRps, earlier + j, target);
        }
    }

    /// <summary>Adds an entry to the earlier pictures when it is before the current picture and is kept.</summary>
    /// <param name="distance">The entry's distance after the offset.</param>
    /// <param name="flagIndex">The index of the entry's flags.</param>
    /// <param name="target">The set being built.</param>
    private void AddEarlier(int distance, int flagIndex, H265ShortTermRps target)
    {
        if (_useDelta[flagIndex] && distance < 0)
        {
            target.AddEarlier(distance, _used[flagIndex]);
        }
    }

    /// <summary>Adds an entry to the later pictures when it is after the current picture and is kept.</summary>
    /// <param name="distance">The entry's distance after the offset.</param>
    /// <param name="flagIndex">The index of the entry's flags.</param>
    /// <param name="target">The set being built.</param>
    private void AddLater(int distance, int flagIndex, H265ShortTermRps target)
    {
        if (_useDelta[flagIndex] && distance > 0)
        {
            target.AddLater(distance, _used[flagIndex]);
        }
    }
}
