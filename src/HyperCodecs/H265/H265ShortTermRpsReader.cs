// Copyright (c) 2026 Glenn Watson. All rights reserved.
// Glenn Watson licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using HyperCodecs.Bits;

namespace HyperCodecs.H265;

/// <summary>Reads short-term reference picture sets, st_ref_pic_set(), including the sets predicted from an earlier set.</summary>
internal static class H265ShortTermRpsReader
{
    /// <summary>The flags of a set predicted from the largest set: one for each picture and one for the offset.</summary>
    private const int MaxFlags = (H265ShortTermRps.MaxPictures * 2) + 1;

    /// <summary>Reads one set.</summary>
    /// <param name="reader">The bits.</param>
    /// <param name="index">The number of the set; sets in a slice header use the count of the sets in the sequence parameter set.</param>
    /// <param name="count">The number of sets in the sequence parameter set.</param>
    /// <param name="sets">The sets read so far from the sequence parameter set.</param>
    /// <param name="target">Receives the set; what it held is replaced.</param>
    /// <exception cref="InvalidDataException">The set is damaged.</exception>
    internal static void Read(ref MsbBitReader reader, int index, int count, List<H265ShortTermRps> sets, H265ShortTermRps target)
    {
        target.Clear();
        var predicted = index != 0 && reader.ReadBit();
        if (predicted)
        {
            ReadPredicted(ref reader, index, count, sets, target);
            return;
        }

        ReadExplicit(ref reader, target);
    }

    /// <summary>Reads a set whose distances are coded directly.</summary>
    /// <param name="reader">The bits.</param>
    /// <param name="target">Receives the set.</param>
    /// <exception cref="InvalidDataException">The set is too large.</exception>
    private static void ReadExplicit(ref MsbBitReader reader, H265ShortTermRps target)
    {
        var negative = (int)ExpGolomb.ReadUe(ref reader);
        var positive = (int)ExpGolomb.ReadUe(ref reader);
        if (negative > H265ShortTermRps.MaxPictures || positive > H265ShortTermRps.MaxPictures)
        {
            throw new InvalidDataException("A short-term reference picture set is too large.");
        }

        target.Resize(negative, positive);
        var previous = 0;
        for (var i = 0; i < negative; i++)
        {
            previous -= 1 + (int)ExpGolomb.ReadUe(ref reader);
            target.SetEarlier(i, previous, reader.ReadBit());
        }

        previous = 0;
        for (var i = 0; i < positive; i++)
        {
            previous += 1 + (int)ExpGolomb.ReadUe(ref reader);
            target.SetLater(i, previous, reader.ReadBit());
        }
    }

    /// <summary>Reads a set that is the set it refers to, with every distance moved by one offset.</summary>
    /// <param name="reader">The bits.</param>
    /// <param name="index">The number of the set.</param>
    /// <param name="count">The number of sets in the sequence parameter set.</param>
    /// <param name="sets">The sets read so far.</param>
    /// <param name="target">Receives the set.</param>
    /// <exception cref="InvalidDataException">The set refers to a set that does not exist.</exception>
    private static void ReadPredicted(ref MsbBitReader reader, int index, int count, List<H265ShortTermRps> sets, H265ShortTermRps target)
    {
        var deltaIdx = index == count ? 1 + (int)ExpGolomb.ReadUe(ref reader) : 1;
        var referenceIndex = index - deltaIdx;
        if (referenceIndex < 0 || referenceIndex >= sets.Count)
        {
            throw new InvalidDataException("A short-term reference picture set refers to a set that does not exist.");
        }

        var negativeSign = reader.ReadBit();
        var absolute = 1 + (int)ExpGolomb.ReadUe(ref reader);
        var deltaRps = negativeSign ? -absolute : absolute;
        var source = sets[referenceIndex];
        var entries = source.Count;
        Span<bool> used = stackalloc bool[MaxFlags];
        Span<bool> useDelta = stackalloc bool[MaxFlags];
        for (var j = 0; j <= entries; j++)
        {
            used[j] = reader.ReadBit();
            useDelta[j] = used[j] || reader.ReadBit();
        }

        new H265RpsPrediction(source, deltaRps, used, useDelta).Derive(target);
    }
}
