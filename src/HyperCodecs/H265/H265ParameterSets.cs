// Copyright (c) 2026 Glenn Watson. All rights reserved.
// Glenn Watson licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace HyperCodecs.H265;

/// <summary>The sequence and picture parameter sets a stream has sent so far, by number.</summary>
internal sealed class H265ParameterSets
{
    /// <summary>The sequence parameter sets.</summary>
    private readonly Dictionary<int, H265Sps> _sequence = [];

    /// <summary>The picture parameter sets.</summary>
    private readonly Dictionary<int, H265Pps> _picture = [];

    /// <summary>The NAL units the sequence parameter sets were read from, by number.</summary>
    private readonly Dictionary<int, byte[]> _sequenceBytes = [];

    /// <summary>The NAL units the picture parameter sets were read from, by number.</summary>
    private readonly Dictionary<int, byte[]> _pictureBytes = [];

    /// <summary>Stores a sequence parameter set, replacing any with the same number.</summary>
    /// <param name="sps">The set.</param>
    /// <param name="nal">The NAL unit it was read from, which is kept.</param>
    internal void Add(H265Sps sps, byte[] nal)
    {
        _sequence[sps.Id] = sps;
        _sequenceBytes[sps.Id] = nal;
    }

    /// <summary>Stores a picture parameter set, replacing any with the same number.</summary>
    /// <param name="pps">The set.</param>
    /// <param name="nal">The NAL unit it was read from, which is kept.</param>
    internal void Add(H265Pps pps, byte[] nal)
    {
        _picture[pps.Id] = pps;
        _pictureBytes[pps.Id] = nal;
    }

    /// <summary>Finds whether a NAL unit is the one a stored parameter set was read from, so reading it again would give the same set.</summary>
    /// <param name="isSequence"><see langword="true"/> for a sequence parameter set, <see langword="false"/> for a picture parameter set.</param>
    /// <param name="nal">The NAL unit including its header.</param>
    /// <returns><see langword="true"/> when the set is already stored.</returns>
    internal bool IsStored(bool isSequence, ReadOnlySpan<byte> nal)
    {
        foreach (var stored in (isSequence ? _sequenceBytes : _pictureBytes).Values)
        {
            if (nal.SequenceEqual(stored))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Gets the first sequence parameter set that was added.</summary>
    /// <returns>The set, or <see langword="null"/> when none was added.</returns>
    internal H265Sps? FirstSps()
    {
        using var enumerator = _sequence.Values.GetEnumerator();
        return enumerator.MoveNext() ? enumerator.Current : null;
    }

    /// <summary>Finds the sets a slice uses.</summary>
    /// <param name="ppsId">The picture parameter set number from the slice header.</param>
    /// <param name="pps">Receives the picture parameter set.</param>
    /// <param name="sps">Receives the sequence parameter set it names.</param>
    /// <returns><see langword="true"/> when both sets have been sent.</returns>
    internal bool TryFind(int ppsId, out H265Pps pps, out H265Sps sps)
    {
        sps = null!;
        return _picture.TryGetValue(ppsId, out pps!) && _sequence.TryGetValue(pps.SpsId, out sps!);
    }
}
