// Copyright (c) 2026 Glenn Watson. All rights reserved.
// Glenn Watson licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Buffers;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace HyperCodecs.Mp4;

/// <summary>
/// Every sample of every track of a file, as parallel arrays so that a million samples cost a few arrays and not a million objects.
/// Samples are added track by track; <see cref="SortByOffset"/> then puts them in the order the file stores them.
/// </summary>
[DebuggerDisplay("Mp4SampleList: {Count} samples")]
internal sealed class Mp4SampleList
{
    /// <summary>The samples the arrays hold at first.</summary>
    private const int InitialCapacity = 1024;

    /// <summary>How many times bigger the arrays become when they are full.</summary>
    private const int GrowthFactor = 2;

    /// <summary>The file offset of each sample.</summary>
    private long[] _offsets = ArrayPool<long>.Shared.Rent(InitialCapacity);

    /// <summary>The size of each sample.</summary>
    private int[] _sizes = ArrayPool<int>.Shared.Rent(InitialCapacity);

    /// <summary>The track of each sample.</summary>
    private int[] _tracks = ArrayPool<int>.Shared.Rent(InitialCapacity);

    /// <summary>Whether decoding can start at each sample.</summary>
    private bool[] _keys = ArrayPool<bool>.Shared.Rent(InitialCapacity);

    /// <summary>When each sample is shown, in seconds.</summary>
    private double[] _times = ArrayPool<double>.Shared.Rent(InitialCapacity);

    /// <summary>When each sample is decoded, in seconds.</summary>
    private double[] _decodeTimes = ArrayPool<double>.Shared.Rent(InitialCapacity);

    /// <summary>How long each sample lasts, in seconds.</summary>
    private double[] _durations = ArrayPool<double>.Shared.Rent(InitialCapacity);

    /// <summary>Gets the number of samples.</summary>
    internal int Count { get; private set; }

    /// <summary>Gets the file offset of a sample.</summary>
    /// <param name="index">The sample.</param>
    /// <returns>The offset.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal long Offset(int index) => _offsets[index];

    /// <summary>Gets the size of a sample.</summary>
    /// <param name="index">The sample.</param>
    /// <returns>The size in bytes.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal int Size(int index) => _sizes[index];

    /// <summary>Gets the track of a sample.</summary>
    /// <param name="index">The sample.</param>
    /// <returns>The track's number.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal int Track(int index) => _tracks[index];

    /// <summary>Gets whether decoding can start at a sample.</summary>
    /// <param name="index">The sample.</param>
    /// <returns><see langword="true"/> for a key sample.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal bool IsKey(int index) => _keys[index];

    /// <summary>Gets when a sample is shown.</summary>
    /// <param name="index">The sample.</param>
    /// <returns>The time in seconds.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal double Time(int index) => _times[index];

    /// <summary>Gets when a sample is decoded.</summary>
    /// <param name="index">The sample.</param>
    /// <returns>The time in seconds.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal double DecodeTime(int index) => _decodeTimes[index];

    /// <summary>Gets how long a sample lasts.</summary>
    /// <param name="index">The sample.</param>
    /// <returns>The duration in seconds.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal double Duration(int index) => _durations[index];

    /// <summary>Adds a sample.</summary>
    /// <param name="offset">The file offset.</param>
    /// <param name="size">The size in bytes.</param>
    /// <param name="track">The track's number.</param>
    /// <param name="isKey">Whether decoding can start here.</param>
    /// <param name="time">When the sample is shown, in seconds.</param>
    /// <param name="decodeTime">When the sample is decoded, in seconds.</param>
    /// <param name="duration">How long it lasts, in seconds.</param>
    internal void Add(long offset, int size, int track, bool isKey, double time, double decodeTime, double duration)
    {
        if (Count == _offsets.Length)
        {
            Grow();
        }

        _offsets[Count] = offset;
        _sizes[Count] = size;
        _tracks[Count] = track;
        _keys[Count] = isKey;
        _times[Count] = time;
        _decodeTimes[Count] = decodeTime;
        _durations[Count] = duration;
        Count++;
    }

    /// <summary>Puts the samples in the order of their offsets in the file, which is the order they are read in.</summary>
    internal void SortByOffset()
    {
        var order = ArrayPool<int>.Shared.Rent(Count);
        var keys = ArrayPool<long>.Shared.Rent(Count);
        for (var index = 0; index < Count; index++)
        {
            order[index] = index;
            keys[index] = _offsets[index];
        }

        Array.Sort(keys, order, 0, Count);
        ArrayPool<long>.Shared.Return(keys);
        Permute(ref _offsets, order, Count);
        Permute(ref _sizes, order, Count);
        Permute(ref _tracks, order, Count);
        Permute(ref _keys, order, Count);
        Permute(ref _times, order, Count);
        Permute(ref _decodeTimes, order, Count);
        Permute(ref _durations, order, Count);
        ArrayPool<int>.Shared.Return(order);
    }

    /// <summary>Gives the arrays back to the pool; the list is not used afterwards.</summary>
    internal void Release()
    {
        ArrayPool<long>.Shared.Return(_offsets);
        ArrayPool<int>.Shared.Return(_sizes);
        ArrayPool<int>.Shared.Return(_tracks);
        ArrayPool<bool>.Shared.Return(_keys);
        ArrayPool<double>.Shared.Return(_times);
        ArrayPool<double>.Shared.Return(_decodeTimes);
        ArrayPool<double>.Shared.Return(_durations);
        _offsets = [];
        _sizes = [];
        _tracks = [];
        _keys = [];
        _times = [];
        _decodeTimes = [];
        _durations = [];
        Count = 0;
    }

    /// <summary>Finds the first sample at or after a file offset.</summary>
    /// <param name="offset">The offset.</param>
    /// <returns>The index, which is <see cref="Count"/> when every sample is before the offset.</returns>
    internal int LowerBound(long offset)
    {
        var low = 0;
        var high = Count;
        while (low < high)
        {
            var middle = low + ((high - low) >> 1);
            if (_offsets[middle] < offset)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        return low;
    }

    /// <summary>Puts the first items of an array in a new order, using a second array from the pool and giving the first back.</summary>
    /// <typeparam name="T">The item type.</typeparam>
    /// <param name="array">The array, which is replaced.</param>
    /// <param name="order">For each place in the result, the item of the array that goes there.</param>
    /// <param name="count">The items to put in order.</param>
    private static void Permute<T>(ref T[] array, int[] order, int count)
    {
        var result = ArrayPool<T>.Shared.Rent(array.Length);
        for (var index = 0; index < count; index++)
        {
            result[index] = array[order[index]];
        }

        ArrayPool<T>.Shared.Return(array);
        array = result;
    }

    /// <summary>Grows an array, using the pool.</summary>
    /// <typeparam name="T">The item type.</typeparam>
    /// <param name="array">The array, which is replaced.</param>
    /// <param name="used">The items to keep.</param>
    private static void Enlarge<T>(ref T[] array, int used)
    {
        var larger = ArrayPool<T>.Shared.Rent(array.Length * GrowthFactor);
        Array.Copy(array, larger, used);
        ArrayPool<T>.Shared.Return(array);
        array = larger;
    }

    /// <summary>Makes the arrays bigger.</summary>
    private void Grow()
    {
        var used = Count;
        Enlarge(ref _offsets, used);
        Enlarge(ref _sizes, used);
        Enlarge(ref _tracks, used);
        Enlarge(ref _keys, used);
        Enlarge(ref _times, used);
        Enlarge(ref _decodeTimes, used);
        Enlarge(ref _durations, used);
    }
}
