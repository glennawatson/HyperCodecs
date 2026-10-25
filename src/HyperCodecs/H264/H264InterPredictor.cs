// Copyright (c) 2026 Glenn Watson. All rights reserved.
// Glenn Watson licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;

namespace HyperCodecs.H264;

/// <summary>Predicts the samples of inter blocks from one or two reference frames, with the slice's weighting (clause 8.4.2 of ITU-T H.264).</summary>
[DebuggerDisplay("H264InterPredictor: weights {_mode}")]
internal sealed class H264InterPredictor
{
    /// <summary>The weighting mode of a slice that does not weight its predictions.</summary>
    internal const int Unweighted = 0;

    /// <summary>The weighting mode of a slice that sends the weights.</summary>
    internal const int Explicit = 1;

    /// <summary>The weighting mode of a B slice that works the weights out from picture order counts.</summary>
    internal const int Implicit = 2;

    /// <summary>The samples along a macroblock side.</summary>
    private const int MacroblockSize = 16;

    /// <summary>The samples along a macroblock side in a chroma plane.</summary>
    private const int ChromaMacroblockSize = 8;

    /// <summary>How many luma samples there are for each chroma sample along a side.</summary>
    private const int ChromaRatio = 2;

    /// <summary>The log2 of the denominator of implicit weights.</summary>
    private const int ImplicitLogDenom = 5;

    /// <summary>The weight each prediction gets when implicit weights fall back to a plain average.</summary>
    private const int EqualWeight = 32;

    /// <summary>The sum of the two implicit weights.</summary>
    private const int WeightTotal = 64;

    /// <summary>The largest sample value.</summary>
    private const int MaxSample = 255;

    /// <summary>The shift of a luma motion vector to whole samples.</summary>
    private const int VectorFractionBits = 2;

    /// <summary>The lines below a block that the luma interpolation reads.</summary>
    private const int FilterReach = 3;

    /// <summary>The samples weighted at a time by the vector path.</summary>
    private const int EightSamples = 8;

    /// <summary>The range limit of the picture order count differences used to scale vectors and weights.</summary>
    private const int DistanceLimit = 127;

    /// <summary>The numerator of the reciprocal that scales picture order count distances.</summary>
    private const int ReciprocalNumerator = 16_384;

    /// <summary>The rounding of the distance scale factor.</summary>
    private const int ScaleRound = 32;

    /// <summary>The shift of the distance scale factor.</summary>
    private const int ScaleShift = 6;

    /// <summary>The smallest distance scale factor.</summary>
    private const int ScaleMin = -1024;

    /// <summary>The largest distance scale factor.</summary>
    private const int ScaleMax = 1023;

    /// <summary>The shift that turns a distance scale factor into an implicit weight.</summary>
    private const int WeightShift = 2;

    /// <summary>The smallest implicit weight that is used.</summary>
    private const int WeightMin = -64;

    /// <summary>The largest implicit weight that is used.</summary>
    private const int WeightMax = 128;

    /// <summary>The luma samples predicted from list 1 when a block uses both lists.</summary>
    private readonly byte[] _luma1;

    /// <summary>The Cb and Cr samples predicted from list 1, one plane after the other.</summary>
    private readonly byte[] _chroma1;

    /// <summary>The picture being decoded.</summary>
    private H264Picture _picture = null!;

    /// <summary>How predictions are weighted: unweighted, explicit or implicit.</summary>
    private int _mode;

    /// <summary>The weights the slice sent, or <see langword="null"/>.</summary>
    private H264WeightTable? _table;

    /// <summary>Initializes a new instance of the <see cref="H264InterPredictor"/> class.</summary>
    /// <param name="work">The decoder's working memory, which holds the scratch predictions.</param>
    internal H264InterPredictor(H264SliceWork work)
    {
        _luma1 = work.Luma1;
        _chroma1 = work.Chroma1;
        Tokens = work.Tokens;
    }

    /// <summary>Gets the tokens the thread gives up while it waits for another picture, or <see langword="null"/> when it holds none.</summary>
    internal WorkTokens? Tokens { get; }

    /// <summary>Works out the distance scale factor that temporal direct prediction and implicit weights use (8-196 to 8-201).</summary>
    /// <param name="current">The picture order count of the picture being decoded.</param>
    /// <param name="first">The picture order count of the first reference.</param>
    /// <param name="second">The picture order count of the second reference.</param>
    /// <returns>The factor.</returns>
    internal static int DistScaleFactor(int current, int first, int second)
    {
        var tb = Math.Clamp(current - first, -DistanceLimit - 1, DistanceLimit);
        var td = Math.Clamp(second - first, -DistanceLimit - 1, DistanceLimit);
        var tx = (ReciprocalNumerator + Math.Abs(td / ChromaRatio)) / td;
        return Math.Clamp(((tb * tx) + ScaleRound) >> ScaleShift, ScaleMin, ScaleMax);
    }

    /// <summary>Points the predictor at a picture, so one object serves every slice of a worker.</summary>
    /// <param name="picture">The picture being decoded.</param>
    /// <param name="mode">How predictions are weighted: <see cref="Unweighted"/>, <see cref="Explicit"/> or <see cref="Implicit"/>.</param>
    /// <param name="table">The weights the slice sent, used in explicit mode.</param>
    internal void Bind(H264Picture picture, int mode, H264WeightTable? table)
    {
        _picture = picture;
        _mode = mode;
        _table = table;
    }

    /// <summary>Predicts the samples of a partition into the picture being decoded.</summary>
    /// <param name="mb">The macroblock.</param>
    /// <param name="partition">The partition, in 4x4 blocks from the macroblock's corner.</param>
    /// <param name="prediction">How the partition is predicted.</param>
    internal void Compensate(H264CurrentMb mb, H264Partition partition, in H264BlockPrediction prediction)
    {
        var x = (mb.X * MacroblockSize) + (partition.Column * H264Layout.BlockSide);
        var y = (mb.Y * MacroblockSize) + (partition.Row * H264Layout.BlockSide);
        var luma = new BlockRect(x, y, partition.Width * H264Layout.BlockSide, partition.Height * H264Layout.BlockSide);
        if (prediction.L0.Ref is not null && prediction.L1.Ref is not null)
        {
            Bidirectional(luma, new(partition.Column * H264Layout.BlockSide, partition.Row * H264Layout.BlockSide, luma.Width, luma.Height), prediction);
            return;
        }

        var single = prediction.L0.Ref is not null ? prediction.L0 : prediction.L1;
        var isSecond = prediction.L0.Ref is null;
        var chromaX = x / ChromaRatio;
        var chromaY = y / ChromaRatio;
        PredictInto(single, luma, new(_picture.Luma, _picture.LumaStride, x, y), new(_picture.Cb, _picture.ChromaStride, chromaX, chromaY), new(_picture.Cr, _picture.ChromaStride, chromaX, chromaY));
        if (_mode == Explicit && _table is not null)
        {
            ApplyExplicit(luma, isSecond ? _table.L1[single.RefIdx] : _table.L0[single.RefIdx]);
        }
    }

    /// <summary>Combines the first prediction of a block, which is already in the picture, with the second, which is in a scratch plane, and leaves the result in the picture.</summary>
    /// <param name="picture">Where the first prediction is and the result goes: the block's first sample in the picture.</param>
    /// <param name="second">Where the second prediction is: the block's first sample in the scratch plane.</param>
    /// <param name="size">The block's width and height; its position is not used.</param>
    /// <param name="weights">The weights to combine with.</param>
    private static void Combine(H264PlaneTarget picture, H264PlaneTarget second, BlockRect size, H264PlaneWeights weights)
    {
        var offset = (weights.Offset + 1) >> 1;
        for (var row = 0; row < size.Height; row++)
        {
            var target = picture.Plane.AsSpan(((picture.Y + row) * picture.Stride) + picture.X, size.Width);
            var other = second.Plane.AsSpan(((second.Y + row) * second.Stride) + second.X, size.Width);
            if (!weights.Weighted)
            {
                AverageRow(target, other, target);
                continue;
            }

            WeightRow(target, other, target, weights, offset);
        }
    }

    /// <summary>Combines two rows of samples with weights, eight samples at a time where vectors are available.</summary>
    /// <param name="first">The first row.</param>
    /// <param name="second">The second row.</param>
    /// <param name="destination">Receives the weighted samples.</param>
    /// <param name="weights">The weights.</param>
    /// <param name="offset">The offset added after weighting.</param>
    private static void WeightRow(ReadOnlySpan<byte> first, ReadOnlySpan<byte> second, Span<byte> destination, H264PlaneWeights weights, int offset)
    {
        var done = 0;
        if (Vector128.IsHardwareAccelerated)
        {
            var shift = weights.LogDenom + 1;
            var firstWeight = Vector128.Create(weights.First);
            var secondWeight = Vector128.Create(weights.Second);
            var round = Vector128.Create(1 << weights.LogDenom);
            var add = Vector128.Create(offset);
            for (; done + EightSamples <= destination.Length; done += EightSamples)
            {
                var a = Vector128.WidenLower(Vector64.Create(first.Slice(done, EightSamples)).ToVector128Unsafe());
                var b = Vector128.WidenLower(Vector64.Create(second.Slice(done, EightSamples)).ToVector128Unsafe());
                var low = WeightLanes(Vector128.WidenLower(a), Vector128.WidenLower(b), firstWeight, secondWeight, round, add, shift);
                var high = WeightLanes(Vector128.WidenUpper(a), Vector128.WidenUpper(b), firstWeight, secondWeight, round, add, shift);
                var shorts = Vector128.Narrow(low.AsUInt32(), high.AsUInt32());
                Vector128.Narrow(shorts, shorts).GetLower().CopyTo(destination.Slice(done, EightSamples));
            }
        }

        for (; done < destination.Length; done++)
        {
            var value = ((((first[done] * weights.First) + (second[done] * weights.Second) + (1 << weights.LogDenom)) >> (weights.LogDenom + 1)) + offset);
            destination[done] = (byte)Math.Clamp(value, 0, MaxSample);
        }
    }

    /// <summary>Weights four pairs of samples and limits the results to the range of a sample.</summary>
    /// <param name="a">Four samples of the first prediction.</param>
    /// <param name="b">Four samples of the second prediction.</param>
    /// <param name="firstWeight">The first weight in every lane.</param>
    /// <param name="secondWeight">The second weight in every lane.</param>
    /// <param name="round">The rounding term in every lane.</param>
    /// <param name="add">The offset in every lane.</param>
    /// <param name="shift">The right shift.</param>
    /// <returns>The four samples.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector128<int> WeightLanes(Vector128<uint> a, Vector128<uint> b, Vector128<int> firstWeight, Vector128<int> secondWeight, Vector128<int> round, Vector128<int> add, int shift)
    {
        var sum = Vector128.ShiftRightArithmetic((a.AsInt32() * firstWeight) + (b.AsInt32() * secondWeight) + round, shift) + add;
        return Vector128.Clamp(sum, Vector128<int>.Zero, Vector128.Create(MaxSample));
    }

    /// <summary>Averages two rows of samples with rounding up, sixteen samples at a time: (a + b + 1) >> 1 is (a | b) - ((a ^ b) >> 1) in every byte.</summary>
    /// <param name="first">The first row.</param>
    /// <param name="second">The second row.</param>
    /// <param name="destination">Receives the averages.</param>
    private static void AverageRow(ReadOnlySpan<byte> first, ReadOnlySpan<byte> second, Span<byte> destination)
    {
        var done = 0;
        if (Vector128.IsHardwareAccelerated)
        {
            var lowBits = Vector128.Create((byte)0x7F).AsUInt64();
            for (; done + Vector128<byte>.Count <= destination.Length; done += Vector128<byte>.Count)
            {
                var a = Vector128.Create(first.Slice(done, Vector128<byte>.Count)).AsUInt64();
                var b = Vector128.Create(second.Slice(done, Vector128<byte>.Count)).AsUInt64();
                var half = Vector128.ShiftRightLogical(a ^ b, 1) & lowBits;
                ((a | b) - half).AsByte().CopyTo(destination.Slice(done, Vector128<byte>.Count));
            }
        }

        for (; done < destination.Length; done++)
        {
            destination[done] = (byte)((first[done] + second[done] + 1) >> 1);
        }
    }

    /// <summary>Predicts a block from one reference into the given luma and Cb targets, and the matching Cr plane.</summary>
    /// <param name="motion">The motion to predict with.</param>
    /// <param name="luma">The block's position and size in the reference frame.</param>
    /// <param name="lumaTarget">Where the luma samples go.</param>
    /// <param name="blueTarget">Where the Cb samples go.</param>
    /// <param name="redTarget">Where the Cr samples go.</param>
    private void PredictInto(H264ListMotion motion, BlockRect luma, H264PlaneTarget lumaTarget, H264PlaneTarget blueTarget, H264PlaneTarget redTarget)
    {
        var reference = motion.Ref!.Picture;

        // The block reads up to three lines below itself, and the chroma interpolation never reaches further down than that in luma terms.
        reference.WaitForLines(Math.Min(luma.Y + (motion.Vector.Y >> VectorFractionBits) + luma.Height + FilterReach, reference.LumaLines - 1), Tokens);
        H264LumaInterpolation.Predict(reference, lumaTarget, luma, motion.Vector);
        var rows = reference.MbHeight * H264Picture.ChromaSize;
        var chroma = new BlockRect(luma.X / ChromaRatio, luma.Y / ChromaRatio, luma.Width / ChromaRatio, luma.Height / ChromaRatio);
        H264ChromaInterpolation.Predict(reference.Cb, reference.ChromaStride, rows, blueTarget, chroma, motion.Vector);
        H264ChromaInterpolation.Predict(reference.Cr, reference.ChromaStride, rows, redTarget, chroma, motion.Vector);
    }

    /// <summary>Applies explicit weights to a block already predicted into the picture.</summary>
    /// <param name="luma">The block's position and size in the picture.</param>
    /// <param name="weight">The weights of the reference used.</param>
    private void ApplyExplicit(BlockRect luma, H264Weight weight)
    {
        var chroma = new BlockRect(luma.X / ChromaRatio, luma.Y / ChromaRatio, luma.Width / ChromaRatio, luma.Height / ChromaRatio);
        H264WeightedPrediction.Apply(_picture.Luma, _picture.LumaStride, luma, weight.LumaWeight, weight.LumaOffset, _table!.LumaLog2Denom);
        H264WeightedPrediction.Apply(_picture.Cb, _picture.ChromaStride, chroma, weight.CbWeight, weight.CbOffset, _table.ChromaLog2Denom);
        H264WeightedPrediction.Apply(_picture.Cr, _picture.ChromaStride, chroma, weight.CrWeight, weight.CrOffset, _table.ChromaLog2Denom);
    }

    /// <summary>Predicts a block from two references and combines the predictions.</summary>
    /// <param name="luma">The block's position and size in the picture.</param>
    /// <param name="inMacroblock">The block's position and size inside the macroblock, where the scratch predictions are kept.</param>
    /// <param name="prediction">The motion of both lists.</param>
    private void Bidirectional(BlockRect luma, BlockRect inMacroblock, in H264BlockPrediction prediction)
    {
        // The first list is predicted straight into the picture and the second into the scratch planes, which are then blended into the picture,
        // so only one of the two predictions makes the extra trip through a scratch plane.
        var chromaX = inMacroblock.X / ChromaRatio;
        var chromaY = inMacroblock.Y / ChromaRatio;
        var redRow = chromaY + ChromaMacroblockSize;
        var lumaTarget = new H264PlaneTarget(_picture.Luma, _picture.LumaStride, luma.X, luma.Y);
        var blueTarget = new H264PlaneTarget(_picture.Cb, _picture.ChromaStride, luma.X / ChromaRatio, luma.Y / ChromaRatio);
        var redTarget = blueTarget with { Plane = _picture.Cr };
        PredictInto(prediction.L0, luma, lumaTarget, blueTarget, redTarget);
        var lumaScratch = new H264PlaneTarget(_luma1, MacroblockSize, inMacroblock.X, inMacroblock.Y);
        var blueScratch = new H264PlaneTarget(_chroma1, ChromaMacroblockSize, chromaX, chromaY);
        PredictInto(prediction.L1, luma, lumaScratch, blueScratch, blueScratch with { Y = redRow });
        var weights = BiWeights(prediction);
        var chroma = new BlockRect(0, 0, luma.Width / ChromaRatio, luma.Height / ChromaRatio);
        Combine(lumaTarget, lumaScratch, new(0, 0, luma.Width, luma.Height), weights.Luma);
        Combine(blueTarget, blueScratch, chroma, weights.Cb);
        Combine(redTarget, blueScratch with { Y = redRow }, chroma, weights.Cr);
    }

    /// <summary>Works out the weights for a block predicted from both lists.</summary>
    /// <param name="prediction">The motion of both lists.</param>
    /// <returns>The weights of the three colour planes.</returns>
    private H264BiWeights BiWeights(in H264BlockPrediction prediction)
    {
        if (_mode == Explicit && _table is not null)
        {
            var first = _table.L0[prediction.L0.RefIdx];
            var second = _table.L1[prediction.L1.RefIdx];
            return new(
                new(first.LumaWeight, second.LumaWeight, first.LumaOffset + second.LumaOffset, _table.LumaLog2Denom, true),
                new(first.CbWeight, second.CbWeight, first.CbOffset + second.CbOffset, _table.ChromaLog2Denom, true),
                new(first.CrWeight, second.CrWeight, first.CrOffset + second.CrOffset, _table.ChromaLog2Denom, true));
        }

        if (_mode == Implicit)
        {
            var implicitWeights = ImplicitWeights(prediction);
            return new(implicitWeights, implicitWeights, implicitWeights);
        }

        var plain = new H264PlaneWeights(1, 1, 0, 0, false);
        return new(plain, plain, plain);
    }

    /// <summary>Works out the implicit weights of a block from the picture order counts of the current picture and its two references (8.4.2.3.1).</summary>
    /// <param name="prediction">The motion of both lists.</param>
    /// <returns>The weights.</returns>
    private H264PlaneWeights ImplicitWeights(in H264BlockPrediction prediction)
    {
        var first = prediction.L0.Ref!;
        var second = prediction.L1.Ref!;
        var equal = new H264PlaneWeights(EqualWeight, EqualWeight, 0, ImplicitLogDenom, true);
        if (first.IsLongTerm || second.IsLongTerm || second.Picture.Poc == first.Picture.Poc)
        {
            return equal;
        }

        var factor = DistScaleFactor(_picture.Poc, first.Picture.Poc, second.Picture.Poc) >> WeightShift;
        return factor is < WeightMin or > WeightMax ? equal : new(WeightTotal - factor, factor, 0, ImplicitLogDenom, true);
    }
}
