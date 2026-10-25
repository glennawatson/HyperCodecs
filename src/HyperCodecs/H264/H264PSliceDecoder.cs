// Copyright (c) 2026 Glenn Watson. All rights reserved.
// Glenn Watson licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Diagnostics;
using System.Runtime.CompilerServices;
using HyperCodecs.Bits;

namespace HyperCodecs.H264;

/// <summary>Reads the macroblocks of P slices coded with CAVLC and has them reconstructed.</summary>
[DebuggerDisplay("H264PSliceDecoder")]
internal sealed class H264PSliceDecoder : IH264CavlcMacroblocks
{
    /// <summary>The first mb_type of the intra macroblocks in a P slice.</summary>
    private const int IntraOffset = 5;

    /// <summary>The mb_type of a macroblock of four 8x8 partitions.</summary>
    private const int Partitions8x8 = 3;

    /// <summary>The mb_type of a macroblock of four 8x8 partitions that all use the first reference.</summary>
    private const int Partitions8x8FirstRef = 4;

    /// <summary>The largest sub_mb_type.</summary>
    private const int MaxSubType = 3;

    /// <summary>The partitions of a 16x8 or 8x16 macroblock.</summary>
    private const int TwoPartitions = 2;

    /// <summary>The 8x8 partitions of a macroblock.</summary>
    private const int Groups = 4;

    /// <summary>Predicts the samples of inter blocks.</summary>
    private readonly H264InterPredictor _predictor;

    /// <summary>Makes the samples of the macroblocks.</summary>
    private readonly H264IntraReconstructor _reconstructor = new();

    /// <summary>Reads the intra macroblocks of the slice.</summary>
    private readonly H264IntraSliceDecoder _intra = new();

    /// <summary>The macroblock being decoded.</summary>
    private readonly H264CurrentMb _mb;

    /// <summary>The work area that holds the macroblock the intra decoder works on.</summary>
    private readonly H264SliceWork _work;

    /// <summary>Predicts motion vectors for the macroblock being decoded.</summary>
    private readonly H264MotionPrediction _motion = new();

    /// <summary>The picture being decoded.</summary>
    private H264Picture _picture = null!;

    /// <summary>The frames the slice predicts from, by reference index.</summary>
    private H264RefPicture[] _refs = null!;

    /// <summary>Whether the pictures may use the 8x8 transform.</summary>
    private bool _transform8x8Allowed;

    /// <summary>The number of the slice being decoded.</summary>
    private int _slice;

    /// <summary>The luma quantiser of the macroblock before.</summary>
    private int _qp;

    /// <summary>Whether every partition of the macroblock being decoded is at least 8x8, so it may use the 8x8 transform.</summary>
    private bool _allow8x8;

    /// <summary>Initializes a new instance of the <see cref="H264PSliceDecoder"/> class.</summary>
    /// <param name="work">The decoder's working memory.</param>
    internal H264PSliceDecoder(H264SliceWork work)
    {
        _mb = work.Mb;
        _work = work;
        _predictor = new(work);
    }

    /// <summary>Points the decoder at a P slice, so one object serves every slice of a worker.</summary>
    /// <param name="picture">The picture to decode into.</param>
    /// <param name="pps">The picture parameter set.</param>
    /// <param name="scale">The level scale factors.</param>
    /// <param name="slice">The number of the slice.</param>
    /// <param name="header">The slice header, which gives the starting quantiser and the weights.</param>
    /// <param name="refs">Reference list 0.</param>
    internal void Bind(H264Picture picture, H264Pps pps, H264LevelScale scale, int slice, H264SliceHeader header, H264RefPicture[] refs)
    {
        var startQp = pps.PicInitQp + header.Tail.QpDelta;
        _picture = picture;
        _refs = refs;
        _predictor.Bind(picture, pps.WeightedPred ? H264InterPredictor.Explicit : H264InterPredictor.Unweighted, header.Refs.Weights);
        _transform8x8Allowed = pps.Transform8x8;
        _reconstructor.Bind(picture, pps, scale);
        _intra.Bind(picture, pps, scale, slice, startQp, _work.IntraMb);
        _slice = slice;
        _qp = startQp;
        _motion.Bind(picture, _mb, slice, picture.Field0);
    }

    /// <summary>Decodes the macroblocks of a slice.</summary>
    /// <param name="rbsp">The payload of the slice's NAL unit.</param>
    /// <param name="dataBit">The bit index where the slice data starts.</param>
    /// <param name="firstMb">The address of the slice's first macroblock.</param>
    /// <exception cref="InvalidDataException">The slice data is damaged.</exception>
    /// <exception cref="NotSupportedException">The slice uses a feature this decoder does not read.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void Decode(ReadOnlySpan<byte> rbsp, long dataBit, int firstMb) => H264CavlcSliceLoop.Run(this, _picture, rbsp, dataBit, firstMb);

    /// <summary>Sets up the macroblock being decoded as an inter macroblock with no motion yet.</summary>
    /// <param name="address">The macroblock's address.</param>
    private void BeginInter(int address)
    {
        H264InterMacroblock.Begin(_mb, _picture, _slice, address);
        _allow8x8 = true;
        _motion.Begin();
    }

    /// <summary>Reconstructs a P_Skip macroblock: the prediction from the first reference with the predicted vector.</summary>
    /// <param name="address">The macroblock's address.</param>
    void IH264CavlcMacroblocks.SkipMacroblock(int address)
    {
        BeginInter(address);
        var whole = H264PartitionGeometry.Macroblock(H264BTypes.Shape16x16, 0);
        var vector = _motion.Skip();
        SetMotion(whole, 0, vector);
        Compensate(whole, 0, vector);
        _picture.Qp[address] = (sbyte)_qp;
    }

    /// <summary>Reads and reconstructs one macroblock.</summary>
    /// <param name="reader">The bits.</param>
    /// <param name="address">The macroblock's address.</param>
    /// <exception cref="InvalidDataException">The macroblock is damaged.</exception>
    void IH264CavlcMacroblocks.DecodeMacroblock(ref MsbBitReader reader, int address)
    {
        var type = (int)ExpGolomb.ReadUe(ref reader);
        if (type >= IntraOffset)
        {
            _intra.Qp = _qp;
            _intra.DecodeMacroblock(ref reader, address, type - IntraOffset);
            _qp = _intra.Qp;
            return;
        }

        BeginInter(address);
        if (type >= Partitions8x8)
        {
            ReadSubMacroblocks(ref reader, type == Partitions8x8FirstRef);
        }
        else
        {
            ReadPartitions(ref reader, type);
        }

        ReadResidual(ref reader);
        _picture.Qp[address] = (sbyte)_qp;
    }

    /// <summary>Reads the coded block pattern, the quantiser change and the residual of an inter macroblock, and adds the residual to the prediction.</summary>
    /// <param name="reader">The bits.</param>
    /// <exception cref="InvalidDataException">The coded block pattern is not valid.</exception>
    private void ReadResidual(ref MsbBitReader reader)
    {
        var code = (int)ExpGolomb.ReadUe(ref reader);
        if (code > H264CodedBlockPattern.MaxCodeNumber)
        {
            throw new InvalidDataException("An H.264 coded block pattern is not valid.");
        }

        var pattern = H264CodedBlockPattern.Lookup(code, false);
        _mb.CbpLuma = pattern & H264CodedBlockPattern.LumaMask;
        _mb.CbpChroma = pattern >> H264CodedBlockPattern.ChromaShift;
        if (pattern == 0)
        {
            return;
        }

        if (_transform8x8Allowed && _allow8x8 && _mb.CbpLuma != 0)
        {
            _mb.Transform8x8 = reader.ReadBit();
            _picture.Transform8x8[_mb.Address] = _mb.Transform8x8;
        }

        _qp = (_qp + ExpGolomb.ReadSe(ref reader) + H264Layout.QpRange) % H264Layout.QpRange;
        H264CavlcResidual.Read(ref reader, _picture, _mb, _slice);
        _reconstructor.AddInterResidual(_mb, _qp);
    }

    /// <summary>Reads the reference indices and motion vectors of a 16x16, 16x8 or 8x16 macroblock.</summary>
    /// <param name="reader">The bits.</param>
    /// <param name="type">The mb_type, 0 to 2.</param>
    private void ReadPartitions(ref MsbBitReader reader, int type)
    {
        var count = type == 0 ? 1 : TwoPartitions;
        Span<int> refIdx = stackalloc int[TwoPartitions];
        for (var index = 0; index < count; index++)
        {
            refIdx[index] = ReadRefIdx(ref reader);
        }

        for (var index = 0; index < count; index++)
        {
            ReadVector(ref reader, H264PartitionGeometry.Macroblock(type, index), refIdx[index]);
        }
    }

    /// <summary>Reads the reference indices and motion vectors of a macroblock of four 8x8 partitions.</summary>
    /// <param name="reader">The bits.</param>
    /// <param name="firstRefOnly">Whether every partition uses the first reference, so no indices are sent.</param>
    /// <exception cref="InvalidDataException">A sub_mb_type is not valid.</exception>
    private void ReadSubMacroblocks(ref MsbBitReader reader, bool firstRefOnly)
    {
        Span<int> subTypes = stackalloc int[Groups];
        Span<int> refIdx = stackalloc int[Groups];
        for (var group = 0; group < Groups; group++)
        {
            subTypes[group] = (int)ExpGolomb.ReadUe(ref reader);
            if (subTypes[group] > MaxSubType)
            {
                throw new InvalidDataException("An H.264 sub-macroblock type is not valid.");
            }

            _allow8x8 &= subTypes[group] == 0;
        }

        for (var group = 0; group < Groups; group++)
        {
            refIdx[group] = firstRefOnly ? 0 : ReadRefIdx(ref reader);
        }

        for (var group = 0; group < Groups; group++)
        {
            for (var part = 0; part < H264PartitionGeometry.SubPartitionCount(subTypes[group]); part++)
            {
                ReadVector(ref reader, H264PartitionGeometry.InGroup(group, subTypes[group], part), refIdx[group]);
            }
        }
    }

    /// <summary>Reads ref_idx_l0.</summary>
    /// <param name="reader">The bits.</param>
    /// <returns>The reference index.</returns>
    /// <exception cref="InvalidDataException">The index names a frame that is not in the list.</exception>
    private int ReadRefIdx(ref MsbBitReader reader)
    {
        if (_refs.Length == 1)
        {
            return 0;
        }

        var index = ExpGolomb.ReadTe(ref reader, _refs.Length - 1);
        return index < _refs.Length ? index : throw new InvalidDataException("An H.264 reference index is not valid.");
    }

    /// <summary>Reads a motion vector difference, adds it to the prediction, and predicts the samples.</summary>
    /// <param name="reader">The bits.</param>
    /// <param name="partition">The partition.</param>
    /// <param name="refIdx">The partition's reference index.</param>
    private void ReadVector(ref MsbBitReader reader, H264Partition partition, int refIdx)
    {
        var predicted = _motion.Predict(partition, refIdx);
        var vector = new MotionVector(predicted.X + ExpGolomb.ReadSe(ref reader), predicted.Y + ExpGolomb.ReadSe(ref reader));
        SetMotion(partition, refIdx, vector);
        Compensate(partition, refIdx, vector);
    }

    /// <summary>Records the motion of a partition so the partitions and macroblocks that follow can predict from it.</summary>
    /// <param name="partition">The partition.</param>
    /// <param name="refIdx">The reference index.</param>
    /// <param name="vector">The motion vector.</param>
    private void SetMotion(H264Partition partition, int refIdx, MotionVector vector)
    {
        H264PMotionStore.Store(_picture, _mb.Address, partition, refIdx, _refs[refIdx].Picture.Uid, vector);
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
