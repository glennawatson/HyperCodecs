// Copyright (c) 2026 Glenn Watson. All rights reserved.
// Glenn Watson licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace HyperCodecs.H264;

/// <summary>Reads the macroblocks of P slices coded with CABAC and has them reconstructed.</summary>
[DebuggerDisplay("H264CabacPSliceDecoder")]
internal sealed class H264CabacPSliceDecoder : IH264CabacMacroblocks
{
    /// <summary>The 4x4 blocks along a macroblock side.</summary>
    private const int Side = 4;

    /// <summary>The 4x4 blocks along the side of an 8x8 partition.</summary>
    private const int Half = 2;

    /// <summary>The partitions of a 16x8 or 8x16 macroblock.</summary>
    private const int TwoPartitions = 2;

    /// <summary>The 8x8 partitions of a macroblock.</summary>
    private const int Groups = 4;

    /// <summary>The first context of the rest of mb_type for an intra macroblock in a P slice.</summary>
    private const int PIntraBase = 17;

    /// <summary>Predicts the samples of inter blocks.</summary>
    private readonly H264InterPredictor _predictor;

    /// <summary>Makes the samples of the macroblocks.</summary>
    private readonly H264IntraReconstructor _reconstructor = new();

    /// <summary>Reads the parts of macroblocks that intra and inter macroblocks share.</summary>
    private readonly H264CabacIntraSliceDecoder _intra = new();

    /// <summary>The macroblock being decoded.</summary>
    private readonly H264CurrentMb _mb;

    /// <summary>The state of each context.</summary>
    private readonly byte[] _contexts;

    /// <summary>Predicts motion vectors for the macroblock being decoded.</summary>
    private readonly H264MotionPrediction _motion = new();

    /// <summary>The picture being decoded.</summary>
    private H264Picture _picture = null!;

    /// <summary>The frames the slice predicts from, by reference index.</summary>
    private H264RefPicture[] _refs = null!;

    /// <summary>The number of the slice being decoded.</summary>
    private int _slice;

    /// <summary>The slice's cabac_init_idc.</summary>
    private int _initIdc;

    /// <summary>The quantiser the slice starts from.</summary>
    private int _startQp;

    /// <summary>Whether every partition of the macroblock being decoded is at least 8x8, so it may use the 8x8 transform.</summary>
    private bool _allow8x8;

    /// <summary>Initializes a new instance of the <see cref="H264CabacPSliceDecoder"/> class.</summary>
    /// <param name="work">The decoder's working memory.</param>
    internal H264CabacPSliceDecoder(H264SliceWork work)
    {
        _mb = work.Mb;
        _contexts = work.Contexts;
        _predictor = new(work);
    }

    /// <summary>Points the decoder at a P slice, so one object serves every slice of a worker.</summary>
    /// <param name="picture">The picture to decode into.</param>
    /// <param name="pps">The picture parameter set.</param>
    /// <param name="scale">The level scale factors.</param>
    /// <param name="slice">The number of the slice.</param>
    /// <param name="header">The slice header, which gives the starting quantiser, the context table and the weights.</param>
    /// <param name="refs">Reference list 0.</param>
    internal void Bind(H264Picture picture, H264Pps pps, H264LevelScale scale, int slice, H264SliceHeader header, H264RefPicture[] refs)
    {
        _startQp = pps.PicInitQp + header.Tail.QpDelta;
        _picture = picture;
        _refs = refs;
        _predictor.Bind(picture, pps.WeightedPred ? H264InterPredictor.Explicit : H264InterPredictor.Unweighted, header.Refs.Weights);
        _reconstructor.Bind(picture, pps, scale);
        _intra.Bind(picture, pps, scale, slice, _startQp, _mb, _contexts);
        _slice = slice;
        _initIdc = header.Tail.CabacInitIdc;
        _motion.Bind(picture, _mb, slice, picture.Field0);
    }

    /// <summary>Decodes the macroblocks of a slice.</summary>
    /// <param name="rbsp">The payload of the slice's NAL unit.</param>
    /// <param name="dataBit">The bit index where the slice data starts (before the alignment bits).</param>
    /// <param name="firstMb">The address of the slice's first macroblock.</param>
    /// <exception cref="InvalidDataException">The slice data is damaged.</exception>
    /// <exception cref="NotSupportedException">The slice uses a feature this decoder does not read.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void Decode(ReadOnlySpan<byte> rbsp, long dataBit, int firstMb) =>
        H264CabacSliceLoop.Run(this, _picture, new(_contexts, false, _initIdc, _startQp), rbsp, dataBit, firstMb);

    /// <summary>Reads and reconstructs one macroblock.</summary>
    /// <param name="engine">The arithmetic decoder.</param>
    /// <param name="rbsp">The slice's payload, for macroblocks of raw samples.</param>
    /// <param name="address">The macroblock's address.</param>
    void IH264CabacMacroblocks.DecodeMacroblock(ref CabacEngine engine, ReadOnlySpan<byte> rbsp, int address)
    {
        BeginInter(address);
        var left = H264CabacNeighbours.Left(_picture, _mb, _slice);
        var top = H264CabacNeighbours.Top(_picture, _mb, _slice);
        if (H264CabacInter.ReadSkip(ref engine, _picture, left, top))
        {
            SkipMacroblock(address);
            return;
        }

        var type = H264CabacInter.ReadMbType(ref engine);
        if (type == H264CabacInter.IntraType)
        {
            _intra.DecodeIntraInInterSlice(ref engine, rbsp, address, PIntraBase);
            return;
        }

        if (type == H264CabacInter.Type8x8)
        {
            ReadSubMacroblocks(ref engine);
        }
        else
        {
            ReadPartitions(ref engine, type);
        }

        _intra.ReadInterResidual(ref engine, _allow8x8);
        if (_mb.CbpLuma != 0 || _mb.CbpChroma != 0)
        {
            _reconstructor.AddInterResidual(_mb, _intra.Qp);
        }
    }

    /// <summary>Sets up the macroblock being decoded as an inter macroblock with no motion yet.</summary>
    /// <param name="address">The macroblock's address.</param>
    private void BeginInter(int address)
    {
        H264InterMacroblock.BeginCabac(_mb, _picture, _slice, address);
        _allow8x8 = true;
        _motion.Begin();
    }

    /// <summary>Reconstructs a P_Skip macroblock: the prediction from the first reference with the predicted vector.</summary>
    /// <param name="address">The macroblock's address.</param>
    private void SkipMacroblock(int address)
    {
        _picture.Skipped[address] = true;
        var whole = H264PartitionGeometry.Macroblock(H264BTypes.Shape16x16, 0);
        var vector = _motion.Skip();
        SetMotion(whole, 0, vector, default);
        Compensate(whole, 0, vector);
        _picture.Qp[address] = (sbyte)_intra.Qp;
        _intra.PreviousHadQpDelta = false;
    }

    /// <summary>Reads the reference indices and motion vectors of a 16x16, 16x8 or 8x16 macroblock.</summary>
    /// <param name="engine">The arithmetic decoder.</param>
    /// <param name="type">The mb_type, 0 to 2.</param>
    private void ReadPartitions(ref CabacEngine engine, int type)
    {
        var count = type == H264CabacInter.Type16x16 ? 1 : TwoPartitions;
        Span<int> refIdx = stackalloc int[TwoPartitions];
        for (var index = 0; index < count; index++)
        {
            var partition = H264PartitionGeometry.Macroblock(type, index);
            refIdx[index] = ReadRefIdx(ref engine, partition);
            StoreRefIdx(partition, refIdx[index]);
        }

        for (var index = 0; index < count; index++)
        {
            ReadVector(ref engine, H264PartitionGeometry.Macroblock(type, index), refIdx[index]);
        }
    }

    /// <summary>Reads the reference indices and motion vectors of a macroblock of four 8x8 partitions.</summary>
    /// <param name="engine">The arithmetic decoder.</param>
    private void ReadSubMacroblocks(ref CabacEngine engine)
    {
        Span<int> subTypes = stackalloc int[Groups];
        Span<int> refIdx = stackalloc int[Groups];
        for (var group = 0; group < Groups; group++)
        {
            subTypes[group] = H264CabacInter.ReadSubType(ref engine);
            _allow8x8 &= subTypes[group] == 0;
        }

        for (var group = 0; group < Groups; group++)
        {
            var whole = H264PartitionGeometry.Group(group);
            refIdx[group] = ReadRefIdx(ref engine, whole);
            StoreRefIdx(whole, refIdx[group]);
        }

        for (var group = 0; group < Groups; group++)
        {
            for (var part = 0; part < H264PartitionGeometry.SubPartitionCount(subTypes[group]); part++)
            {
                ReadVector(ref engine, H264PartitionGeometry.InGroup(group, subTypes[group], part), refIdx[group]);
            }
        }
    }

    /// <summary>Reads ref_idx_l0 of a partition, or gives 0 when the slice has one reference.</summary>
    /// <param name="engine">The arithmetic decoder.</param>
    /// <param name="partition">The partition.</param>
    /// <returns>The reference index.</returns>
    /// <exception cref="InvalidDataException">The index names a frame that is not in the list.</exception>
    private int ReadRefIdx(ref CabacEngine engine, H264Partition partition)
    {
        if (_refs.Length == 1)
        {
            return 0;
        }

        var inc = H264CabacInter.RefIdxInc(NeighbourRefIdx(partition.Column - 1, partition.Row), NeighbourRefIdx(partition.Column, partition.Row - 1));
        var index = H264CabacInter.ReadRefIdx(ref engine, inc);
        return index < _refs.Length ? index : throw new InvalidDataException("An H.264 reference index is not valid.");
    }

    /// <summary>Gets the reference index of the block at a position, or -1 when it is not available or not inter coded.</summary>
    /// <param name="column">The block's column relative to the current macroblock.</param>
    /// <param name="row">The block's row relative to the current macroblock.</param>
    /// <returns>The reference index.</returns>
    private int NeighbourRefIdx(int column, int row)
    {
        var address = _motion.MacroblockAt(column, row);
        var group = ((((row + Side) & (Side - 1)) >> 1) * Half) + (((column + Side) & (Side - 1)) >> 1);
        return address < 0 ? -1 : _picture.RefIdx[(address * H264Picture.GroupsPerMb) + group];
    }

    /// <summary>Records the reference index of a partition as soon as it is read, because the indices of later partitions use it as context.</summary>
    /// <param name="partition">The partition.</param>
    /// <param name="refIdx">The reference index.</param>
    private void StoreRefIdx(H264Partition partition, int refIdx)
    {
        for (var r = partition.Row; r < partition.Row + partition.Height; r++)
        {
            for (var c = partition.Column; c < partition.Column + partition.Width; c++)
            {
                _picture.RefIdx[(_mb.Address * H264Picture.GroupsPerMb) + ((r >> 1) * Half) + (c >> 1)] = (sbyte)refIdx;
            }
        }
    }

    /// <summary>Reads a motion vector difference, adds it to the prediction, and predicts the samples.</summary>
    /// <param name="engine">The arithmetic decoder.</param>
    /// <param name="partition">The partition.</param>
    /// <param name="refIdx">The partition's reference index.</param>
    private void ReadVector(ref CabacEngine engine, H264Partition partition, int refIdx)
    {
        var predicted = _motion.Predict(partition, refIdx);
        var differenceX = H264CabacInter.ReadMvd(ref engine, false, NeighbourMvd(_picture.MvdX, partition));
        var differenceY = H264CabacInter.ReadMvd(ref engine, true, NeighbourMvd(_picture.MvdY, partition));
        var vector = new MotionVector(predicted.X + differenceX, predicted.Y + differenceY);
        SetMotion(partition, refIdx, vector, new(differenceX, differenceY));
        Compensate(partition, refIdx, vector);
    }

    /// <summary>Adds the sizes of the motion vector differences of the blocks to the left of and above a partition.</summary>
    /// <param name="differences">The stored sizes of one component.</param>
    /// <param name="partition">The partition.</param>
    /// <returns>The sum.</returns>
    private int NeighbourMvd(byte[] differences, H264Partition partition) =>
        MvdAt(differences, partition.Column - 1, partition.Row) + MvdAt(differences, partition.Column, partition.Row - 1);

    /// <summary>Gets the stored size of the motion vector difference of the block at a position.</summary>
    /// <param name="differences">The stored sizes of one component.</param>
    /// <param name="column">The block's column relative to the current macroblock.</param>
    /// <param name="row">The block's row relative to the current macroblock.</param>
    /// <returns>The size, 0 when the block is not available.</returns>
    private int MvdAt(byte[] differences, int column, int row)
    {
        var address = _motion.MacroblockAt(column, row);
        return address < 0 ? 0 : differences[(address * H264Picture.BlocksPerMb) + (((row + Side) & (Side - 1)) * Side) + ((column + Side) & (Side - 1))];
    }

    /// <summary>Records the motion of a partition so the partitions and macroblocks that follow can predict from it.</summary>
    /// <param name="partition">The partition.</param>
    /// <param name="refIdx">The reference index.</param>
    /// <param name="vector">The motion vector.</param>
    /// <param name="difference">The motion vector difference that was read.</param>
    private void SetMotion(H264Partition partition, int refIdx, MotionVector vector, MotionVector difference)
    {
        H264PMotionStore.Store(_picture, _mb.Address, partition, refIdx, _refs[refIdx].Picture.Uid, vector);
        H264PMotionStore.StoreDifference(_picture, _mb.Address, partition, difference);
        _motion.MarkDecoded(partition);
    }

    /// <summary>Predicts the samples of a partition from its reference frame.</summary>
    /// <param name="partition">The partition.</param>
    /// <param name="refIdx">The reference index.</param>
    /// <param name="vector">The motion vector.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void Compensate(H264Partition partition, int refIdx, MotionVector vector) =>
        _predictor.Compensate(_mb, partition, new(new(_refs[refIdx], refIdx, vector), default));
}
