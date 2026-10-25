// Copyright (c) 2026 Glenn Watson. All rights reserved.
// Glenn Watson licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace HyperCodecs.H264;

/// <summary>Reads the macroblocks of B slices coded with CABAC and has them reconstructed.</summary>
[DebuggerDisplay("H264CabacBSliceDecoder")]
internal sealed class H264CabacBSliceDecoder : IH264CabacMacroblocks
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

    /// <summary>The first context of the rest of mb_type for an intra macroblock in a B slice.</summary>
    private const int IntraBase = 32;

    /// <summary>Makes the samples of the macroblocks.</summary>
    private readonly H264IntraReconstructor _reconstructor = new();

    /// <summary>Reads the parts of macroblocks that intra and inter macroblocks share.</summary>
    private readonly H264CabacIntraSliceDecoder _intra = new();

    /// <summary>The macroblock being decoded.</summary>
    private readonly H264CurrentMb _mb;

    /// <summary>The state of each context.</summary>
    private readonly byte[] _contexts;

    /// <summary>Keeps the motion of the macroblock being decoded.</summary>
    private readonly H264BMotion _motion = new();

    /// <summary>Predicts the samples of inter blocks.</summary>
    private readonly H264InterPredictor _predictor;

    /// <summary>The picture being decoded.</summary>
    private H264Picture _picture = null!;

    /// <summary>The two reference lists.</summary>
    private H264RefLists _lists;

    /// <summary>The number of the slice being decoded.</summary>
    private int _slice;

    /// <summary>Whether direct prediction works on 8x8 blocks.</summary>
    private bool _inference;

    /// <summary>The slice's cabac_init_idc.</summary>
    private int _initIdc;

    /// <summary>The quantiser the slice starts from.</summary>
    private int _startQp;

    /// <summary>Whether every partition of the macroblock being decoded is at least 8x8, so it may use the 8x8 transform.</summary>
    private bool _allow8x8;

    /// <summary>Initializes a new instance of the <see cref="H264CabacBSliceDecoder"/> class.</summary>
    /// <param name="work">The decoder's working memory.</param>
    internal H264CabacBSliceDecoder(H264SliceWork work)
    {
        _mb = work.Mb;
        _contexts = work.Contexts;
        _predictor = new(work);
    }

    /// <summary>Points the decoder at a B slice, so one object serves every slice of a worker.</summary>
    /// <param name="picture">The picture to decode into.</param>
    /// <param name="sets">The sequence and picture parameter sets.</param>
    /// <param name="scale">The level scale factors.</param>
    /// <param name="slice">The number of the slice.</param>
    /// <param name="header">The slice header, which gives the starting quantiser, the context table, the weights and the direct mode.</param>
    /// <param name="lists">The two reference lists.</param>
    internal void Bind(H264Picture picture, H264SliceSets sets, H264LevelScale scale, int slice, H264SliceHeader header, H264RefLists lists)
    {
        var (sps, pps) = sets;
        _startQp = pps.PicInitQp + header.Tail.QpDelta;
        _picture = picture;
        _lists = lists;
        _inference = sps.Direct8x8Inference;
        _reconstructor.Bind(picture, pps, scale);
        _intra.Bind(picture, pps, scale, slice, _startQp, _mb, _contexts);
        _slice = slice;
        _initIdc = header.Tail.CabacInitIdc;
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
    /// <param name="dataBit">The bit index where the slice data starts (before the alignment bits).</param>
    /// <param name="firstMb">The address of the slice's first macroblock.</param>
    /// <exception cref="InvalidDataException">The slice data is damaged.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void Decode(ReadOnlySpan<byte> rbsp, long dataBit, int firstMb) =>
        H264CabacSliceLoop.Run(this, _picture, new(_contexts, false, _initIdc, _startQp), rbsp, dataBit, firstMb);

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
        H264InterMacroblock.BeginCabac(_mb, _picture, _slice, address);
        _allow8x8 = true;
        _motion.Begin();
    }

    /// <summary>Reads and reconstructs one macroblock.</summary>
    /// <param name="engine">The arithmetic decoder.</param>
    /// <param name="rbsp">The slice's payload, for macroblocks of raw samples.</param>
    /// <param name="address">The macroblock's address.</param>
    void IH264CabacMacroblocks.DecodeMacroblock(ref CabacEngine engine, ReadOnlySpan<byte> rbsp, int address)
    {
        BeginInter(address);
        var left = H264CabacNeighbours.Left(_picture, _mb, _slice);
        var top = H264CabacNeighbours.Top(_picture, _mb, _slice);
        if (H264CabacInter.ReadSkipB(ref engine, _picture, left, top))
        {
            _picture.Skipped[address] = true;
            _picture.DirectOnly[address] = true;
            H264BDirect.Predict(_motion, _inference);
            _picture.Qp[address] = (sbyte)_intra.Qp;
            _intra.PreviousHadQpDelta = false;
            return;
        }

        var type = H264CabacInter.ReadBMbType(ref engine, _picture, left, top);
        if (type == H264CabacInter.IntraType)
        {
            _intra.DecodeIntraInInterSlice(ref engine, rbsp, address, IntraBase);
            return;
        }

        ReadMotion(ref engine, type);
        _intra.ReadInterResidual(ref engine, _allow8x8);
        if (_mb.CbpLuma != 0 || _mb.CbpChroma != 0)
        {
            _reconstructor.AddInterResidual(_mb, _intra.Qp);
        }
    }

    /// <summary>Reads the motion of an inter macroblock of a given type and predicts its samples.</summary>
    /// <param name="engine">The arithmetic decoder.</param>
    /// <param name="type">The mb_type, 0 to 22.</param>
    private void ReadMotion(ref CabacEngine engine, int type)
    {
        if (type == H264BTypes.Direct16x16)
        {
            _picture.DirectOnly[_mb.Address] = true;
            _allow8x8 = _inference;
            H264BDirect.Predict(_motion, _inference);
        }
        else if (type == H264BTypes.Partitions8x8)
        {
            ReadSubMacroblocks(ref engine);
        }
        else
        {
            ReadPartitions(ref engine, type);
        }
    }

    /// <summary>Reads the reference indices and motion vectors of a macroblock with one or two partitions.</summary>
    /// <param name="engine">The arithmetic decoder.</param>
    /// <param name="type">The mb_type, 1 to 21.</param>
    private void ReadPartitions(ref CabacEngine engine, int type)
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
                    var partition = PartitionOf(type, index);
                    refIdx[(list * TwoPartitions) + index] = ReadRefIdx(ref engine, list, partition);
                    _motion.StoreRefIdx(list, partition, refIdx[(list * TwoPartitions) + index]);
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
                    ReadVector(ref engine, list, partition, reference);
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
    /// <param name="engine">The arithmetic decoder.</param>
    private void ReadSubMacroblocks(ref CabacEngine engine)
    {
        Span<int> subTypes = stackalloc int[Groups];
        ReadSubTypes(ref engine, subTypes);
        Span<int> refIdx = stackalloc int[Lists * Groups];
        refIdx.Fill(-1);
        for (var list = 0; list < Lists; list++)
        {
            for (var group = 0; group < Groups; group++)
            {
                refIdx[(list * Groups) + group] = ReadGroupRefIdx(ref engine, list, group, subTypes[group]);
            }
        }

        for (var list = 0; list < Lists; list++)
        {
            for (var group = 0; group < Groups; group++)
            {
                ReadGroupVectors(ref engine, list, group, subTypes[group], refIdx[(list * Groups) + group]);
            }
        }

        for (var group = 0; group < Groups; group++)
        {
            H264BDirect.CompensateGroup(_motion, group, subTypes[group], _inference);
        }
    }

    /// <summary>Reads the reference index of an 8x8 group for a list, if the group's sub_mb_type uses the list.</summary>
    /// <param name="engine">The arithmetic decoder.</param>
    /// <param name="list">0 or 1.</param>
    /// <param name="group">The group, 0 to 3.</param>
    /// <param name="subType">The group's sub_mb_type.</param>
    /// <returns>The reference index, or -1 when the group does not use the list.</returns>
    private int ReadGroupRefIdx(ref CabacEngine engine, int list, int group, int subType)
    {
        if ((H264BTypes.SubModeOf(subType) & (1 << list)) == 0)
        {
            return -1;
        }

        var whole = H264PartitionGeometry.Group(group);
        var reference = ReadRefIdx(ref engine, list, whole);
        _motion.StoreRefIdx(list, whole, reference);
        return reference;
    }

    /// <summary>Reads the four sub_mb_types, works out whether the 8x8 transform is still allowed, and derives the motion of the direct groups.</summary>
    /// <param name="engine">The arithmetic decoder.</param>
    /// <param name="subTypes">Receives the four sub_mb_types.</param>
    private void ReadSubTypes(ref CabacEngine engine, scoped Span<int> subTypes)
    {
        for (var group = 0; group < Groups; group++)
        {
            var subType = H264CabacInter.ReadBSubType(ref engine);
            subTypes[group] = subType;
            _allow8x8 &= subType == H264BTypes.SubDirect ? _inference : H264BTypes.SubShapeOf(subType) == 0;
            if (subType != H264BTypes.SubDirect)
            {
                continue;
            }

            _motion.Direct(group);
        }
    }

    /// <summary>Reads the motion vectors of the sub-partitions of an 8x8 group for one list.</summary>
    /// <param name="engine">The arithmetic decoder.</param>
    /// <param name="list">0 or 1.</param>
    /// <param name="group">The group, 0 to 3.</param>
    /// <param name="subType">The group's sub_mb_type.</param>
    /// <param name="reference">The group's reference index for the list, or -1 when it does not use the list.</param>
    private void ReadGroupVectors(ref CabacEngine engine, int list, int group, int subType, int reference)
    {
        if (reference < 0)
        {
            _motion.MarkAbsent(list, H264PartitionGeometry.Group(group));
            return;
        }

        var shape = H264BTypes.SubShapeOf(subType);
        for (var part = 0; part < H264PartitionGeometry.SubPartitionCount(shape); part++)
        {
            ReadVector(ref engine, list, H264PartitionGeometry.InGroup(group, shape, part), reference);
        }
    }

    /// <summary>Reads ref_idx for a list, or gives 0 when the list has one frame.</summary>
    /// <param name="engine">The arithmetic decoder.</param>
    /// <param name="list">0 or 1.</param>
    /// <param name="partition">The partition.</param>
    /// <returns>The reference index.</returns>
    /// <exception cref="InvalidDataException">The index names a frame that is not in the list.</exception>
    private int ReadRefIdx(ref CabacEngine engine, int list, H264Partition partition)
    {
        var count = list == 0 ? _lists.L0.Length : _lists.L1.Length;
        var inc = H264CabacInter.RefIdxInc(_motion.NeighbourRef(list, partition.Column - 1, partition.Row), _motion.NeighbourRef(list, partition.Column, partition.Row - 1));
        var index = count == 1 ? 0 : H264CabacInter.ReadRefIdx(ref engine, inc);
        return index < count ? index : throw new InvalidDataException("An H.264 reference index is not valid.");
    }

    /// <summary>Reads a motion vector difference for a list, adds it to the prediction, and records the result.</summary>
    /// <param name="engine">The arithmetic decoder.</param>
    /// <param name="list">0 or 1.</param>
    /// <param name="partition">The partition.</param>
    /// <param name="reference">The partition's reference index for the list.</param>
    private void ReadVector(ref CabacEngine engine, int list, H264Partition partition, int reference)
    {
        var predicted = _motion.Predict(list, partition, reference);
        var differenceX = H264CabacInter.ReadMvd(ref engine, false, _motion.NeighbourMvd(list, false, partition));
        var differenceY = H264CabacInter.ReadMvd(ref engine, true, _motion.NeighbourMvd(list, true, partition));
        var difference = new MotionVector(differenceX, differenceY);
        _motion.Store(list, partition, reference, new(new(predicted.X + differenceX, predicted.Y + differenceY), difference));
    }
}
