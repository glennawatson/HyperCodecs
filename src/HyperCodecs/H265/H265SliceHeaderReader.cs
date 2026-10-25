// Copyright (c) 2026 Glenn Watson. All rights reserved.
// Glenn Watson licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using HyperCodecs.Bits;

namespace HyperCodecs.H265;

/// <summary>Reads slice segment headers (7.3.6.1).</summary>
internal static class H265SliceHeaderReader
{
    /// <summary>The most reference pictures in a list.</summary>
    private const int MaxRefIdx = 15;

    /// <summary>The number of merge candidates when none are removed.</summary>
    private const int MaxMergeCandidates = 5;

    /// <summary>The most long-term pictures a slice header may name.</summary>
    private const int MaxLongTerm = 32;

    /// <summary>The most entry points a slice header may list.</summary>
    private const int MaxEntryPoints = 4_096;

    /// <summary>Reads a slice segment header.</summary>
    /// <param name="rbsp">The payload of the NAL unit, without its header and emulation prevention bytes.</param>
    /// <param name="nalType">The NAL unit type.</param>
    /// <param name="sets">The parameter sets received so far.</param>
    /// <param name="previous">The header of the slice before this segment, which a dependent segment continues.</param>
    /// <param name="header">Receives the header; what it held is replaced. It must not be <paramref name="previous"/>.</param>
    /// <exception cref="InvalidDataException">The header is damaged or names a parameter set that was not sent.</exception>
    internal static void Read(ReadOnlySpan<byte> rbsp, int nalType, H265ParameterSets sets, H265SliceHeader? previous, H265SliceHeader header)
    {
        var reader = new MsbBitReader(rbsp);
        var first = reader.ReadBit();
        var noOutput = H265Nal.IsIrap(nalType) && reader.ReadBit();
        var ppsId = (int)ExpGolomb.ReadUe(ref reader);
        if (!sets.TryFind(ppsId, out var pps, out var sps))
        {
            throw new InvalidDataException("A slice names a parameter set that was not sent.");
        }

        var dependent = false;
        var address = 0;
        if (!first)
        {
            dependent = pps.Flags.DependentSlices && reader.ReadBit();
            address = (int)reader.Read(BitHelpers.CeilLog2(sps.WidthInCtbs * sps.HeightInCtbs));
        }

        if (dependent)
        {
            header.CopyFrom(previous ?? throw new InvalidDataException("A dependent slice segment has no slice before it."));
        }
        else
        {
            header.Clear();
            ReadIndependent(ref reader, nalType, pps, sps, header);
        }

        var segment = header.Segment;
        ReadEntryPoints(ref reader, pps, segment.EntryPointList);
        SkipExtension(ref reader, pps);
        reader.Skip(1);
        reader.Align();
        if (reader.IsOverrun)
        {
            throw new InvalidDataException("The H.265 slice header is damaged.");
        }

        segment.First = first;
        segment.NoOutputOfPriorPics = noOutput;
        segment.PpsId = ppsId;
        segment.Dependent = dependent;
        segment.Address = address;
        segment.DataOffset = (int)(reader.BitPosition >> 3);
    }

    /// <summary>Reads the part of the header that a dependent segment does not repeat.</summary>
    /// <param name="reader">The bits.</param>
    /// <param name="nalType">The NAL unit type.</param>
    /// <param name="pps">The picture parameter set.</param>
    /// <param name="sps">The sequence parameter set.</param>
    /// <param name="header">Receives what is read; the caller fills in the segment.</param>
    private static void ReadIndependent(ref MsbBitReader reader, int nalType, H265Pps pps, H265Sps sps, H265SliceHeader header)
    {
        BitHelpers.Skip(ref reader, pps.ExtraSliceHeaderBits);
        header.SliceType = (int)ExpGolomb.ReadUe(ref reader);
        header.PicOutput = !pps.Flags.OutputFlagPresent || reader.ReadBit();
        if (sps.SeparatePlanes)
        {
            reader.Skip(H265SliceHeaderBits.ColourPlane);
        }

        if (!H265Nal.IsIdr(nalType))
        {
            header.PocLsb = (int)reader.Read(sps.Log2MaxPocLsb);
            ReadShortTerm(ref reader, sps, header);
            ReadLongTerm(ref reader, sps, header.LongTermList);
            header.TemporalMvp = sps.Tools.TemporalMvpEnabled && reader.ReadBit();
        }

        header.SaoLuma = sps.Tools.SaoEnabled && reader.ReadBit();
        header.SaoChroma = sps.Tools.SaoEnabled && sps.ChromaArrayType != ChromaSampling.Monochrome && reader.ReadBit();
        ReadInterAndQuant(ref reader, header, pps, sps);
    }

    /// <summary>Reads the reference list settings, weights, quantiser and loop filter settings.</summary>
    /// <param name="reader">The bits.</param>
    /// <param name="header">The header so far, which receives the rest.</param>
    /// <param name="pps">The picture parameter set.</param>
    /// <param name="sps">The sequence parameter set.</param>
    private static void ReadInterAndQuant(ref MsbBitReader reader, H265SliceHeader header, H265Pps pps, H265Sps sps)
    {
        if (header.SliceType != H265SliceType.I)
        {
            ReadRefs(ref reader, header, pps);
            var weighted = header.SliceType == H265SliceType.P ? pps.Flags.WeightedPred : pps.Flags.WeightedBipred;
            if (weighted)
            {
                header.HasWeights = true;
                var counts = new ReferenceListCounts(header.Refs.NumRefIdxL0, header.SliceType == H265SliceType.B ? header.Refs.NumRefIdxL1 : 0);
                H265PredWeightReader.Read(ref reader, sps, counts, header.WeightStorage);
            }

            header.Refs.MaxMergeCand = MaxMergeCandidates - (int)ExpGolomb.ReadUe(ref reader);
        }

        header.Quant = ReadQuant(ref reader, pps);
        header.Deblocking = ReadDeblocking(ref reader, pps, header.SaoLuma || header.SaoChroma);
    }

    /// <summary>Reads the short-term reference picture set choice.</summary>
    /// <param name="reader">The bits.</param>
    /// <param name="sps">The sequence parameter set.</param>
    /// <param name="header">Receives the set.</param>
    /// <exception cref="InvalidDataException">The index is out of range.</exception>
    private static void ReadShortTerm(ref MsbBitReader reader, H265Sps sps, H265SliceHeader header)
    {
        var count = sps.ShortTermSets.Count;
        if (!reader.ReadBit())
        {
            var start = reader.BitPosition;
            var set = header.OwnedShortTerm;
            H265ShortTermRpsReader.Read(ref reader, count, count, sps.ShortTermSets, set);
            set.HeaderBits = (int)(reader.BitPosition - start);
            header.UseOwnedShortTerm();
            return;
        }

        var index = count > 1 ? (int)reader.Read(BitHelpers.CeilLog2(count)) : 0;
        header.UseSharedShortTerm(index < count ? sps.ShortTermSets[index] : throw new InvalidDataException("A slice picks a short-term reference picture set that does not exist."));
    }

    /// <summary>Reads the long-term reference pictures of the slice.</summary>
    /// <param name="reader">The bits.</param>
    /// <param name="sps">The sequence parameter set.</param>
    /// <param name="entries">Receives the pictures.</param>
    /// <exception cref="InvalidDataException">The slice names too many pictures.</exception>
    private static void ReadLongTerm(ref MsbBitReader reader, H265Sps sps, List<H265LongTermRef> entries)
    {
        if (!sps.LongTerm.Present)
        {
            return;
        }

        var candidates = sps.LongTerm.PocLsb.Length;
        var fromSps = candidates > 0 ? (int)ExpGolomb.ReadUe(ref reader) : 0;
        var total = fromSps + (int)ExpGolomb.ReadUe(ref reader);
        if (total > MaxLongTerm || fromSps > candidates)
        {
            throw new InvalidDataException("A slice names too many long-term reference pictures.");
        }

        var cycle = 0;
        for (var i = 0; i < total; i++)
        {
            var named = ReadLongTermIdentity(ref reader, sps, i < fromSps);
            var present = reader.ReadBit();
            var delta = present ? (int)ExpGolomb.ReadUe(ref reader) : 0;
            cycle = i == 0 || i == fromSps ? delta : cycle + delta;
            entries.Add(named with { MsbPresent = present, DeltaPocMsbCycle = cycle });
        }
    }

    /// <summary>Reads which long-term picture an entry names.</summary>
    /// <param name="reader">The bits.</param>
    /// <param name="sps">The sequence parameter set.</param>
    /// <param name="fromSps">Whether the entry picks from the candidates in the sequence parameter set.</param>
    /// <returns>The entry with its low order count bits and used flag; the high order count bits are filled in by the caller.</returns>
    private static H265LongTermRef ReadLongTermIdentity(ref MsbBitReader reader, H265Sps sps, bool fromSps)
    {
        if (!fromSps)
        {
            return new((int)reader.Read(sps.Log2MaxPocLsb), reader.ReadBit(), false, 0);
        }

        var candidates = sps.LongTerm.PocLsb.Length;
        var index = candidates > 1 ? (int)reader.Read(BitHelpers.CeilLog2(candidates)) : 0;
        return new(sps.LongTerm.PocLsb[index], sps.LongTerm.Used[index], false, 0);
    }

    /// <summary>Reads the reference list settings.</summary>
    /// <param name="reader">The bits.</param>
    /// <param name="header">The header so far, which receives the settings; the merge candidate count is still to be read.</param>
    /// <param name="pps">The picture parameter set.</param>
    /// <exception cref="InvalidDataException">A list is too long.</exception>
    private static void ReadRefs(ref MsbBitReader reader, H265SliceHeader header, H265Pps pps)
    {
        var isB = header.SliceType == H265SliceType.B;
        var sizes = ReadListSizes(ref reader, isB, pps);
        var total = header.NumPicTotalCurr;
        var refs = header.Refs;
        refs.NumRefIdxL0 = sizes.L0;
        refs.NumRefIdxL1 = sizes.L1;
        if (pps.Flags.ListsModificationPresent && total > 1)
        {
            refs.HasModificationL0 = ReadModification(ref reader, sizes.L0, total, refs.ModificationStorageL0);
            refs.HasModificationL1 = isB && ReadModification(ref reader, sizes.L1, total, refs.ModificationStorageL1);
        }

        refs.MvdL1Zero = isB && reader.ReadBit();
        refs.CabacInit = pps.Flags.CabacInitPresent && reader.ReadBit();
        if (!header.TemporalMvp)
        {
            return;
        }

        refs.CollocatedFromL0 = !isB || reader.ReadBit();
        refs.CollocatedRefIdx = (refs.CollocatedFromL0 ? sizes.L0 : sizes.L1) > 1 ? (int)ExpGolomb.ReadUe(ref reader) : 0;
    }

    /// <summary>Reads the number of pictures in each reference list, which a slice may override.</summary>
    /// <param name="reader">The bits.</param>
    /// <param name="isB">Whether the slice is a B slice and so has a second list.</param>
    /// <param name="pps">The picture parameter set.</param>
    /// <returns>The sizes.</returns>
    /// <exception cref="InvalidDataException">A list is too long.</exception>
    private static ReferenceListCounts ReadListSizes(ref MsbBitReader reader, bool isB, H265Pps pps)
    {
        var l0 = pps.NumRefIdxL0Default;
        var l1 = isB ? pps.NumRefIdxL1Default : 0;
        if (reader.ReadBit())
        {
            l0 = 1 + (int)ExpGolomb.ReadUe(ref reader);
            l1 = isB ? 1 + (int)ExpGolomb.ReadUe(ref reader) : 0;
        }

        if (l0 > MaxRefIdx || l1 > MaxRefIdx)
        {
            throw new InvalidDataException("A reference picture list is too long.");
        }

        return new(l0, l1);
    }

    /// <summary>Reads the modification of one list: a flag, then an entry for each picture.</summary>
    /// <param name="reader">The bits.</param>
    /// <param name="count">The pictures in the list.</param>
    /// <param name="total">The pictures the slice refers to.</param>
    /// <param name="entries">Receives the entries, at least <paramref name="count"/> long.</param>
    /// <returns><see langword="true"/> when the flag is on and the entries were read.</returns>
    private static bool ReadModification(ref MsbBitReader reader, int count, int total, int[] entries)
    {
        if (!reader.ReadBit())
        {
            return false;
        }

        var bits = BitHelpers.CeilLog2(total);
        for (var i = 0; i < count; i++)
        {
            entries[i] = (int)reader.Read(bits);
        }

        return true;
    }

    /// <summary>Reads the quantiser settings.</summary>
    /// <param name="reader">The bits.</param>
    /// <param name="pps">The picture parameter set.</param>
    /// <returns>The settings.</returns>
    private static H265SliceQuant ReadQuant(ref MsbBitReader reader, H265Pps pps)
    {
        var qp = pps.Quant.InitQp + ExpGolomb.ReadSe(ref reader);
        var cb = 0;
        var cr = 0;
        if (pps.Flags.SliceChromaQpOffsetsPresent)
        {
            cb = ExpGolomb.ReadSe(ref reader);
            cr = ExpGolomb.ReadSe(ref reader);
        }

        var chromaOffsetEnabled = pps.Range.ChromaQpOffsetListEnabled && reader.ReadBit();
        return new(qp, cb, cr, chromaOffsetEnabled);
    }

    /// <summary>Reads the loop filter settings.</summary>
    /// <param name="reader">The bits.</param>
    /// <param name="pps">The picture parameter set.</param>
    /// <param name="sao">Whether sample adaptive offset is on in the slice.</param>
    /// <returns>The settings.</returns>
    private static H265SliceDeblocking ReadDeblocking(ref MsbBitReader reader, H265Pps pps, bool sao)
    {
        var disabled = pps.Deblocking.Disabled;
        var beta = pps.Deblocking.BetaOffsetDiv2;
        var tc = pps.Deblocking.TcOffsetDiv2;
        if (pps.Deblocking.OverrideEnabled && reader.ReadBit())
        {
            disabled = reader.ReadBit();
            beta = 0;
            tc = 0;
            if (!disabled)
            {
                beta = ExpGolomb.ReadSe(ref reader);
                tc = ExpGolomb.ReadSe(ref reader);
            }
        }

        var across = pps.Flags.LoopFilterAcrossSlices;
        if (across && (sao || !disabled))
        {
            across = reader.ReadBit();
        }

        return new(disabled, beta, tc, across);
    }

    /// <summary>Reads the sizes of the subsets of slice data.</summary>
    /// <param name="reader">The bits.</param>
    /// <param name="pps">The picture parameter set.</param>
    /// <param name="entries">Receives the size in bytes of each subset but the last.</param>
    /// <exception cref="InvalidDataException">There are too many entry points.</exception>
    private static void ReadEntryPoints(ref MsbBitReader reader, H265Pps pps, List<int> entries)
    {
        entries.Clear();
        if (!pps.Tiles.Enabled && !pps.Flags.EntropyCodingSync)
        {
            return;
        }

        var count = (int)ExpGolomb.ReadUe(ref reader);
        if (count > MaxEntryPoints)
        {
            throw new InvalidDataException("An H.265 slice has too many entry points.");
        }

        if (count == 0)
        {
            return;
        }

        var bits = 1 + (int)ExpGolomb.ReadUe(ref reader);
        for (var i = 0; i < count; i++)
        {
            entries.Add(1 + (int)reader.Read(bits));
        }
    }

    /// <summary>Skips the slice header extension bytes.</summary>
    /// <param name="reader">The bits.</param>
    /// <param name="pps">The picture parameter set.</param>
    private static void SkipExtension(ref MsbBitReader reader, H265Pps pps)
    {
        if (!pps.Flags.SliceHeaderExtensionPresent)
        {
            return;
        }

        var length = (int)ExpGolomb.ReadUe(ref reader);
        BitHelpers.Skip(ref reader, length * H265SliceHeaderBits.Byte);
    }
}
