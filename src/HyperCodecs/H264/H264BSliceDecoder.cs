// Copyright (c) 2026 Glenn Watson. All rights reserved.
// Glenn Watson licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Diagnostics;
using System.Runtime.CompilerServices;
using HyperCodecs.Bits;

namespace HyperCodecs.H264;

/// <summary>Reads the macroblocks of B slices coded with CAVLC and has them reconstructed.</summary>
[DebuggerDisplay("H264BSliceDecoder")]
internal sealed class H264BSliceDecoder : IH264CavlcMacroblocks
{
    /// <summary>The partitions of a 16x8 or 8x16 macroblock.</summary>
    private const int TwoPartitions = 2;

    /// <summary>The 8x8 partitions of a macroblock.</summary>
    private const int Groups = 4;

    /// <summary>The reference lists B slices use.</summary>
    private const int Lists = 2;

    /// <summary>The weighted_bipred_idc value that sends weights.</summary>
    private const int ExplicitWeights = 1;

    /// <summary>The weighted_bipred_idc value that works weights out from picture order counts.</summary>
    private const int ImplicitWeights = 2;

    /// <summary>The largest sub_mb_type.</summary>
    private const int MaxSubType = 12;

    /// <summary>The largest mb_type of a B slice, counting the intra types.</summary>
    private const int MaxMbType = 48;

    /// <summary>Reads the intra macroblocks of the slice.</summary>
    private readonly H264IntraSliceDecoder _intra = new();

    /// <summary>Reads the residual of inter macroblocks.</summary>
    private readonly H264CavlcInterResidual _residual = new();

    /// <summary>Makes the samples of the inter macroblocks.</summary>
    private readonly H264IntraReconstructor _reconstructor = new();

    /// <summary>The macroblock being decoded.</summary>
    private readonly H264CurrentMb _mb;

    /// <summary>The work area that holds the macroblock the intra decoder works on.</summary>
    private readonly H264SliceWork _work;

    /// <summary>Predicts the samples of inter blocks.</summary>
    private readonly H264InterPredictor _predictor;

    /// <summary>Keeps the motion of the macroblock being decoded.</summary>
    private readonly H264BMotion _motion = new();

    /// <summary>The picture being decoded.</summary>
    private H264Picture _picture = null!;

    /// <summary>The two reference lists.</summary>
    private H264RefLists _lists;

    /// <summary>The number of the slice being decoded.</summary>
    private int _slice;

    /// <summary>Whether direct prediction works on 8x8 blocks.</summary>
    private bool _inference;

    /// <summary>Whether every partition of the macroblock being decoded is at least 8x8, so it may use the 8x8 transform.</summary>
    private bool _allow8x8;

    /// <summary>Initializes a new instance of the <see cref="H264BSliceDecoder"/> class.</summary>
    /// <param name="work">The decoder's working memory.</param>
    internal H264BSliceDecoder(H264SliceWork work)
    {
        _mb = work.Mb;
        _work = work;
        _predictor = new(work);
    }

    /// <summary>Points the decoder at a B slice, so one object serves every slice of a worker.</summary>
    /// <param name="picture">The picture to decode into.</param>
    /// <param name="sets">The sequence and picture parameter sets.</param>
    /// <param name="scale">The level scale factors.</param>
    /// <param name="slice">The number of the slice.</param>
    /// <param name="header">The slice header, which gives the starting quantiser, the weights and the direct mode.</param>
    /// <param name="lists">The two reference lists.</param>
    internal void Bind(H264Picture picture, H264SliceSets sets, H264LevelScale scale, int slice, H264SliceHeader header, H264RefLists lists)
    {
        var (sps, pps) = sets;
        var startQp = pps.PicInitQp + header.Tail.QpDelta;
        _picture = picture;
        _lists = lists;
        _inference = sps.Direct8x8Inference;
        _reconstructor.Bind(picture, pps, scale);
        _intra.Bind(picture, pps, scale, slice, startQp, _work.IntraMb);
        _residual.Bind(picture, _mb, slice, _reconstructor, pps.Transform8x8);
        _residual.Qp = startQp;
        _slice = slice;
        var mode = pps.WeightedBipredIdc switch
        {
            ExplicitWeights => H264InterPredictor.Explicit,
            ImplicitWeights => H264InterPredictor.Implicit,
            _ => H264InterPredictor.Unweighted,
        };
        _predictor.Bind(picture, mode, header.Refs.Weights);
        _motion.Bind(picture, _mb, slice, lists, _predictor, new(header.Refs.DirectSpatialMvPred, sps.Direct8x8Inference));
    }

    /// <summary>Decodes the macroblocks of a slice.</summary>
    /// <param name="rbsp">The payload of the slice's NAL unit.</param>
    /// <param name="dataBit">The bit index where the slice data starts.</param>
    /// <param name="firstMb">The address of the slice's first macroblock.</param>
    /// <exception cref="InvalidDataException">The slice data is damaged.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void Decode(ReadOnlySpan<byte> rbsp, long dataBit, int firstMb) => H264CavlcSliceLoop.Run(this, _picture, rbsp, dataBit, firstMb);

    /// <summary>Gets a partition of a macroblock with one or two partitions.</summary>
    /// <param name="type">The mb_type, 1 to 21.</param>
    /// <param name="index">The partition's number.</param>
    /// <returns>The partition.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static H264Partition PartitionOf(int type, int index) => H264PartitionGeometry.Macroblock(H264BTypes.ShapeOf(type), index);

    /// <summary>Sets up the macroblock being decoded as an inter macroblock with no motion yet.</summary>
    /// <param name="address">The macroblock's address.</param>
    private void BeginInter(int address)
    {
        H264InterMacroblock.Begin(_mb, _picture, _slice, address);
        _allow8x8 = true;
        _motion.Begin();
    }

    /// <summary>Reconstructs a B_Skip macroblock: direct prediction with no residual.</summary>
    /// <param name="address">The macroblock's address.</param>
    void IH264CavlcMacroblocks.SkipMacroblock(int address)
    {
        BeginInter(address);
        _picture.Skipped[address] = true;
        H264BDirect.Predict(_motion, _inference);
        _picture.Qp[address] = (sbyte)_residual.Qp;
    }

    /// <summary>Reads and reconstructs one macroblock.</summary>
    /// <param name="reader">The bits.</param>
    /// <param name="address">The macroblock's address.</param>
    /// <exception cref="InvalidDataException">The macroblock is damaged.</exception>
    void IH264CavlcMacroblocks.DecodeMacroblock(ref MsbBitReader reader, int address)
    {
        var type = (int)ExpGolomb.ReadUe(ref reader);
        if (type > MaxMbType)
        {
            throw new InvalidDataException("An H.264 macroblock type is not valid for a B slice.");
        }

        if (type >= H264BTypes.IntraOffset)
        {
            _intra.Qp = _residual.Qp;
            _intra.DecodeMacroblock(ref reader, address, type - H264BTypes.IntraOffset);
            _residual.Qp = _intra.Qp;
            return;
        }

        BeginInter(address);
        if (type == H264BTypes.Direct16x16)
        {
            _allow8x8 = _inference;
            H264BDirect.Predict(_motion, _inference);
        }
        else if (type == H264BTypes.Partitions8x8)
        {
            ReadSubMacroblocks(ref reader);
        }
        else
        {
            ReadPartitions(ref reader, type);
        }

        _residual.Read(ref reader, _allow8x8);
    }

    /// <summary>Reads the reference indices and motion vectors of a macroblock with one or two partitions.</summary>
    /// <param name="reader">The bits.</param>
    /// <param name="type">The mb_type, 1 to 21.</param>
    private void ReadPartitions(ref MsbBitReader reader, int type)
    {
        var count = H264BTypes.HasTwoPartitions(type) ? TwoPartitions : 1;
        Span<int> refIdx = stackalloc int[Lists * TwoPartitions];
        refIdx.Fill(-1);
        for (var list = 0; list < Lists; list++)
        {
            for (var index = 0; index < count; index++)
            {
                if ((H264BTypes.ModeOf(type, index) & (1 << list)) != 0)
                {
                    refIdx[(list * TwoPartitions) + index] = ReadRefIdx(ref reader, list);
                }
            }
        }

        for (var list = 0; list < Lists; list++)
        {
            for (var index = 0; index < count; index++)
            {
                var partition = PartitionOf(type, index);
                var reference = refIdx[(list * TwoPartitions) + index];
                if (reference >= 0)
                {
                    ReadVector(ref reader, list, partition, reference);
                }
                else
                {
                    _motion.MarkAbsent(list, partition);
                }
            }
        }

        for (var index = 0; index < count; index++)
        {
            _motion.Compensate(PartitionOf(type, index));
        }
    }

    /// <summary>Reads the reference indices and motion vectors of a macroblock of four 8x8 partitions.</summary>
    /// <param name="reader">The bits.</param>
    /// <exception cref="InvalidDataException">A sub_mb_type is not valid.</exception>
    private void ReadSubMacroblocks(ref MsbBitReader reader)
    {
        Span<int> subTypes = stackalloc int[Groups];
        ReadSubTypes(ref reader, subTypes);
        Span<int> refIdx = stackalloc int[Lists * Groups];
        refIdx.Fill(-1);
        for (var list = 0; list < Lists; list++)
        {
            for (var group = 0; group < Groups; group++)
            {
                if ((H264BTypes.SubModeOf(subTypes[group]) & (1 << list)) != 0)
                {
                    refIdx[(list * Groups) + group] = ReadRefIdx(ref reader, list);
                }
            }
        }

        for (var list = 0; list < Lists; list++)
        {
            for (var group = 0; group < Groups; group++)
            {
                ReadGroupVectors(ref reader, list, group, subTypes[group], refIdx[(list * Groups) + group]);
            }
        }

        for (var group = 0; group < Groups; group++)
        {
            H264BDirect.CompensateGroup(_motion, group, subTypes[group], _inference);
        }
    }

    /// <summary>Reads the four sub_mb_types, works out whether the 8x8 transform is still allowed, and derives the motion of the direct groups.</summary>
    /// <param name="reader">The bits.</param>
    /// <param name="subTypes">Receives the four sub_mb_types.</param>
    /// <exception cref="InvalidDataException">A sub_mb_type is not valid.</exception>
    private void ReadSubTypes(ref MsbBitReader reader, scoped Span<int> subTypes)
    {
        for (var group = 0; group < Groups; group++)
        {
            var subType = (int)ExpGolomb.ReadUe(ref reader);
            if (subType > MaxSubType)
            {
                throw new InvalidDataException("An H.264 sub-macroblock type is not valid.");
            }

            subTypes[group] = subType;
            _allow8x8 &= subType == H264BTypes.SubDirect ? _inference : H264BTypes.SubShapeOf(subType) == 0;
            if (subType == H264BTypes.SubDirect)
            {
                _motion.Direct(group);
            }
        }
    }

    /// <summary>Reads the motion vectors of the sub-partitions of an 8x8 group for one list.</summary>
    /// <param name="reader">The bits.</param>
    /// <param name="list">0 or 1.</param>
    /// <param name="group">The group, 0 to 3.</param>
    /// <param name="subType">The group's sub_mb_type.</param>
    /// <param name="reference">The group's reference index for the list, or -1 when it does not use the list.</param>
    private void ReadGroupVectors(ref MsbBitReader reader, int list, int group, int subType, int reference)
    {
        if (reference < 0)
        {
            _motion.MarkAbsent(list, H264PartitionGeometry.Group(group));
            return;
        }

        var shape = H264BTypes.SubShapeOf(subType);
        for (var part = 0; part < H264PartitionGeometry.SubPartitionCount(shape); part++)
        {
            ReadVector(ref reader, list, H264PartitionGeometry.InGroup(group, shape, part), reference);
        }
    }

    /// <summary>Reads ref_idx for a list.</summary>
    /// <param name="reader">The bits.</param>
    /// <param name="list">0 or 1.</param>
    /// <returns>The reference index.</returns>
    /// <exception cref="InvalidDataException">The index names a frame that is not in the list.</exception>
    private int ReadRefIdx(ref MsbBitReader reader, int list)
    {
        var count = list == 0 ? _lists.L0.Length : _lists.L1.Length;
        if (count == 1)
        {
            return 0;
        }

        var index = ExpGolomb.ReadTe(ref reader, count - 1);
        return index < count ? index : throw new InvalidDataException("An H.264 reference index is not valid.");
    }

    /// <summary>Reads a motion vector difference for a list, adds it to the prediction, and records the result.</summary>
    /// <param name="reader">The bits.</param>
    /// <param name="list">0 or 1.</param>
    /// <param name="partition">The partition.</param>
    /// <param name="reference">The partition's reference index for the list.</param>
    private void ReadVector(ref MsbBitReader reader, int list, H264Partition partition, int reference)
    {
        var predicted = _motion.Predict(list, partition, reference);
        var difference = new MotionVector(ExpGolomb.ReadSe(ref reader), ExpGolomb.ReadSe(ref reader));
        _motion.Store(list, partition, reference, new(new(predicted.X + difference.X, predicted.Y + difference.Y), difference));
    }
}
