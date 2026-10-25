// Copyright (c) 2026 Glenn Watson. All rights reserved.
// Glenn Watson licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Diagnostics;

namespace HyperCodecs.H265;

/// <summary>
/// A short-term reference picture set: the distances in picture order count of the pictures before and after the current one, and whether the current
/// picture refers to each. A set read into a slice segment header is filled again for each segment, so it keeps its arrays.
/// </summary>
[DebuggerDisplay("H265ShortTermRps: {EarlierCount} earlier, {LaterCount} later")]
internal sealed class H265ShortTermRps
{
    /// <summary>The most pictures a set may hold on each side: the largest decoded picture buffer.</summary>
    internal const int MaxPictures = 16;

    /// <summary>The distances of the earlier pictures.</summary>
    private readonly int[] _deltaS0 = new int[MaxPictures];

    /// <summary>The used flags of the earlier pictures.</summary>
    private readonly bool[] _usedS0 = new bool[MaxPictures];

    /// <summary>The distances of the later pictures.</summary>
    private readonly int[] _deltaS1 = new int[MaxPictures];

    /// <summary>The used flags of the later pictures.</summary>
    private readonly bool[] _usedS1 = new bool[MaxPictures];

    /// <summary>Gets the number of earlier pictures.</summary>
    internal int EarlierCount { get; private set; }

    /// <summary>Gets the number of later pictures.</summary>
    internal int LaterCount { get; private set; }

    /// <summary>Gets or sets the bits the set took in a slice segment header when it was written there rather than picked from the sequence parameter set, or 0.</summary>
    internal int HeaderBits { get; set; }

    /// <summary>Gets the distances of the earlier pictures, nearest first.</summary>
    internal ReadOnlySpan<int> DeltaPocS0 => _deltaS0.AsSpan(0, EarlierCount);

    /// <summary>Gets whether the current picture refers to each earlier picture.</summary>
    internal ReadOnlySpan<bool> UsedS0 => _usedS0.AsSpan(0, EarlierCount);

    /// <summary>Gets the distances of the later pictures, nearest first.</summary>
    internal ReadOnlySpan<int> DeltaPocS1 => _deltaS1.AsSpan(0, LaterCount);

    /// <summary>Gets whether the current picture refers to each later picture.</summary>
    internal ReadOnlySpan<bool> UsedS1 => _usedS1.AsSpan(0, LaterCount);

    /// <summary>Gets the pictures in the set.</summary>
    internal int Count => EarlierCount + LaterCount;

    /// <summary>Gets the pictures in the set that the current picture refers to.</summary>
    internal int UsedCount => CountUsed(UsedS0) + CountUsed(UsedS1);

    /// <summary>Empties the set.</summary>
    internal void Clear()
    {
        EarlierCount = 0;
        LaterCount = 0;
        HeaderBits = 0;
    }

    /// <summary>Sets how many pictures the set holds on each side, for the caller to fill.</summary>
    /// <param name="earlier">The earlier pictures.</param>
    /// <param name="later">The later pictures.</param>
    /// <exception cref="InvalidDataException">The set is larger than a decoded picture buffer.</exception>
    internal void Resize(int earlier, int later)
    {
        if (earlier > MaxPictures || later > MaxPictures)
        {
            throw new InvalidDataException("A short-term reference picture set is too large.");
        }

        EarlierCount = earlier;
        LaterCount = later;
    }

    /// <summary>Sets one earlier picture.</summary>
    /// <param name="index">The picture's place.</param>
    /// <param name="delta">The distance.</param>
    /// <param name="used">Whether the current picture refers to it.</param>
    internal void SetEarlier(int index, int delta, bool used)
    {
        _deltaS0[index] = delta;
        _usedS0[index] = used;
    }

    /// <summary>Sets one later picture.</summary>
    /// <param name="index">The picture's place.</param>
    /// <param name="delta">The distance.</param>
    /// <param name="used">Whether the current picture refers to it.</param>
    internal void SetLater(int index, int delta, bool used)
    {
        _deltaS1[index] = delta;
        _usedS1[index] = used;
    }

    /// <summary>Adds an earlier picture after the ones already there.</summary>
    /// <param name="delta">The distance.</param>
    /// <param name="used">Whether the current picture refers to it.</param>
    /// <exception cref="InvalidDataException">The set is full.</exception>
    internal void AddEarlier(int delta, bool used)
    {
        var index = EarlierCount;
        Resize(index + 1, LaterCount);
        SetEarlier(index, delta, used);
    }

    /// <summary>Adds a later picture after the ones already there.</summary>
    /// <param name="delta">The distance.</param>
    /// <param name="used">Whether the current picture refers to it.</param>
    /// <exception cref="InvalidDataException">The set is full.</exception>
    internal void AddLater(int delta, bool used)
    {
        var index = LaterCount;
        Resize(EarlierCount, index + 1);
        SetLater(index, delta, used);
    }

    /// <summary>Makes the set the same as another.</summary>
    /// <param name="source">The set to copy.</param>
    internal void CopyFrom(H265ShortTermRps source)
    {
        EarlierCount = source.EarlierCount;
        LaterCount = source.LaterCount;
        HeaderBits = source.HeaderBits;
        source.DeltaPocS0.CopyTo(_deltaS0);
        source.UsedS0.CopyTo(_usedS0);
        source.DeltaPocS1.CopyTo(_deltaS1);
        source.UsedS1.CopyTo(_usedS1);
    }

    /// <summary>Counts the true flags.</summary>
    /// <param name="flags">The flags.</param>
    /// <returns>The count.</returns>
    private static int CountUsed(ReadOnlySpan<bool> flags)
    {
        var count = 0;
        foreach (var flag in flags)
        {
            if (flag)
            {
                count++;
            }
        }

        return count;
    }
}
