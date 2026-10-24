// Copyright (c) 2026 Glenn Watson. All rights reserved.
// Glenn Watson licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using HyperCodecs.H264;

namespace HyperCodecs.H265;

/// <summary>Applies sample adaptive offset to a deblocked picture (8.7.3).</summary>
internal static class H265Sao
{
    /// <summary>The 16 bit samples in a 128 bit vector.</summary>
    private const int ShortLanes = 8;

    /// <summary>The sum of the two comparison signs of a valley.</summary>
    private const short ValleySum = -2;

    /// <summary>The sum of the two comparison signs of a peak.</summary>
    private const short PeakSum = 2;

    /// <summary>The largest bit depth the vector path takes: samples are held as signed 16 bit values, with room for the offsets.</summary>
    private const int MaxVectorBits = 14;

    /// <summary>The bands of a band offset.</summary>
    private const int Bands = 32;

    /// <summary>The bit depth term of the band shift.</summary>
    private const int BandBits = 5;

    /// <summary>The blocks around a block in each direction, counting itself.</summary>
    private const int Around = 3;

    /// <summary>The coding tree blocks in the square around a block, counting itself.</summary>
    private const int Neighbourhood = Around * Around;

    /// <summary>The number of edge classes.</summary>
    private const int EdgeClasses = 4;

    /// <summary>The value added to the sum of the two comparison signs to index <see cref="Categories"/>.</summary>
    private const int SignBias = 2;

    /// <summary>Gets the edge category from the sum of the comparison signs plus the bias: valley, step, flat, step, peak (8-412). A category of 0 leaves the sample alone.</summary>
    private static ReadOnlySpan<byte> Categories => [0x01, 0x02, 0x00, 0x03, 0x04];

    /// <summary>Gets the horizontal offsets of the two neighbours of each edge class, first neighbours then second (Table 8-13).</summary>
    private static ReadOnlySpan<sbyte> HorizontalOffsets => [-1, 0, -1, 1, 1, 0, 1, -1];

    /// <summary>Gets the vertical offsets of the two neighbours of each edge class, first neighbours then second (Table 8-13).</summary>
    private static ReadOnlySpan<sbyte> VerticalOffsets => [0, -1, -1, -1, 0, 1, 1, 1];

    /// <summary>
    /// Applies the offsets to one row of coding tree blocks. Each block reads the deblocked copy of the plane that the filtering keeps, which holds the
    /// deblocked samples of the row, the line above it and the line below it, and writes only its own samples.
    /// </summary>
    /// <param name="context">The slice context of the picture.</param>
    /// <param name="ctbRow">The row of coding tree blocks.</param>
    internal static void FilterRow(H265SliceContext context, int ctbRow)
    {
        var components = context.HasChroma ? H265SaoParams.Components : 1;
        for (var component = 0; component < components; component++)
        {
            ApplyRows(context, component, context.SaoSources[component], ctbRow, context.Layout.HeightInCtbs);
        }
    }

    /// <summary>Applies the offsets to every n-th row of coding tree blocks of one colour component.</summary>
    /// <param name="context">The slice context of the picture.</param>
    /// <param name="component">The colour component.</param>
    /// <param name="source">The deblocked samples of the plane.</param>
    /// <param name="first">The first row of coding tree blocks.</param>
    /// <param name="step">The distance between the rows.</param>
    private static void ApplyRows(H265SliceContext context, int component, ushort[] source, int first, int step)
    {
        var picture = context.Picture;
        var layout = context.Layout;
        var plane = picture.PlaneOf(component);
        for (var row = first; row < layout.HeightInCtbs; row += step)
        {
            for (var ctb = row * layout.WidthInCtbs; ctb < (row + 1) * layout.WidthInCtbs; ctb++)
            {
                var offset = (ctb * H265SaoParams.IntsPerCtb) + (component * H265SaoParams.IntsPerComponent);
                if (picture.Sao[offset] == H265SaoParams.None || picture.HeaderOfCtb[ctb] is null)
                {
                    continue;
                }

                var block = MakeBlock(context, component, ctb, source, plane);
                ApplyBlock(context, block, picture.Sao.AsSpan(offset, H265SaoParams.IntsPerComponent));
            }
        }
    }

    /// <summary>Describes a coding tree block of a colour component.</summary>
    /// <param name="context">The slice context.</param>
    /// <param name="component">The colour component.</param>
    /// <param name="ctb">The raster scan address of the block.</param>
    /// <param name="source">The deblocked samples of the plane.</param>
    /// <param name="target">The plane to write to.</param>
    /// <returns>The block.</returns>
    private static H265SaoBlock MakeBlock(H265SliceContext context, int component, int ctb, ushort[] source, ushort[] target)
    {
        var sps = context.Sps;
        var layout = context.Layout;
        var picture = context.Picture;
        var subW = component == 0 ? 1 : sps.SubWidthC;
        var subH = component == 0 ? 1 : sps.SubHeightC;
        var ctbWidth = (1 << layout.CtbLog2) / subW;
        var ctbHeight = (1 << layout.CtbLog2) / subH;
        var planeWidth = component == 0 ? picture.Width : picture.ChromaWidth;
        var planeHeight = component == 0 ? picture.Height : picture.ChromaHeight;
        var x0 = (ctb % layout.WidthInCtbs) * ctbWidth;
        var y0 = (ctb / layout.WidthInCtbs) * ctbHeight;
        var area = new BlockRect(x0, y0, Math.Min(ctbWidth, planeWidth - x0), Math.Min(ctbHeight, planeHeight - y0));
        return new(component, ctb, source, target, area, planeWidth, planeHeight, component == 0 ? sps.BitDepthLuma : sps.BitDepthChroma);
    }

    /// <summary>Applies the offsets to one coding tree block of one colour component.</summary>
    /// <param name="context">The slice context.</param>
    /// <param name="block">The block.</param>
    /// <param name="parameters">The parameters of the block's component.</param>
    private static void ApplyBlock(H265SliceContext context, in H265SaoBlock block, ReadOnlySpan<int> parameters)
    {
        var offsets = parameters.Slice(H265SaoParams.OffsetsOffset, H265SaoParams.OffsetCount);
        var protectedSamples = context.Pps.Flags.TransquantBypass || context.Sps.Pcm.LoopFilterDisabled;
        if (parameters[0] == H265SaoParams.Band)
        {
            ApplyBand(context, block, parameters[H265SaoParams.ClassOffset], offsets, protectedSamples);
        }
        else
        {
            ApplyEdge(context, block, parameters[H265SaoParams.ClassOffset], offsets, protectedSamples);
        }
    }

    /// <summary>Applies band offset to a block (8-414 and 8-415).</summary>
    /// <param name="context">The slice context.</param>
    /// <param name="block">The block.</param>
    /// <param name="leftBand">The first of the four bands that get an offset.</param>
    /// <param name="offsets">The four offsets.</param>
    /// <param name="protectedSamples">Whether some samples may be exempt because they are lossless.</param>
    private static void ApplyBand(H265SliceContext context, in H265SaoBlock block, int leftBand, ReadOnlySpan<int> offsets, bool protectedSamples)
    {
        Span<int> table = stackalloc int[Bands];
        for (var k = 0; k < H265SaoParams.OffsetCount; k++)
        {
            table[(k + leftBand) & (Bands - 1)] = k + 1;
        }

        var area = block.Area;
        var vectorWidth = Vector128.IsHardwareAccelerated && !protectedSamples && block.BitDepth <= MaxVectorBits ? area.Width & ~(ShortLanes - 1) : 0;
        for (var y = area.Y; y < area.Y + area.Height; y++)
        {
            if (vectorWidth > 0)
            {
                BandRow(block, leftBand, offsets, (y * block.PlaneWidth) + area.X, vectorWidth);
            }

            BandSamples(context, block, table, offsets, new(area.X + vectorWidth, y), protectedSamples);
        }
    }

    /// <summary>Applies band offset to the samples of a row from a column to the end of the block, one by one.</summary>
    /// <param name="context">The slice context.</param>
    /// <param name="block">The block.</param>
    /// <param name="table">The offset number, 1 to 4, of each band; 0 for a band without an offset.</param>
    /// <param name="offsets">The four offsets.</param>
    /// <param name="from">The column to start at, and the row.</param>
    /// <param name="protectedSamples">Whether some samples may be exempt because they are lossless.</param>
    private static void BandSamples(H265SliceContext context, in H265SaoBlock block, ReadOnlySpan<int> table, ReadOnlySpan<int> offsets, SamplePlace from, bool protectedSamples)
    {
        var shift = block.BitDepth - BandBits;
        var maximum = (1 << block.BitDepth) - 1;
        var (start, y) = from;
        for (var x = start; x < block.Area.X + block.Area.Width; x++)
        {
            var index = (y * block.PlaneWidth) + x;
            var band = table[block.Source[index] >> shift];
            if (band != 0 && !(protectedSamples && IsProtected(context, block.Component, x, y)))
            {
                block.Target[index] = (ushort)Math.Clamp(block.Source[index] + offsets[band - 1], 0, maximum);
            }
        }
    }

    /// <summary>Applies edge offset to a block (8-409 to 8-413).</summary>
    /// <param name="context">The slice context.</param>
    /// <param name="block">The block.</param>
    /// <param name="edgeClass">The edge class, 0 to 3.</param>
    /// <param name="offsets">The four offsets.</param>
    /// <param name="protectedSamples">Whether some samples may be exempt because they are lossless.</param>
    private static void ApplyEdge(H265SliceContext context, in H265SaoBlock block, int edgeClass, ReadOnlySpan<int> offsets, bool protectedSamples)
    {
        Span<bool> allowed = stackalloc bool[Neighbourhood];
        FillAllowed(context, block.Ctb, allowed);
        var horizontal0 = HorizontalOffsets[edgeClass];
        var vertical0 = VerticalOffsets[edgeClass];
        var horizontal1 = HorizontalOffsets[edgeClass + EdgeClasses];
        var vertical1 = VerticalOffsets[edgeClass + EdgeClasses];
        var area = block.Area;
        var limits = new H265SaoLimits((1 << block.BitDepth) - 1, protectedSamples, Vector128.IsHardwareAccelerated && !protectedSamples && block.BitDepth <= MaxVectorBits && area.Width >= Around);
        var neighbours = new H265SaoNeighbours(horizontal0, vertical0, horizontal1, vertical1);
        for (var y = area.Y; y < area.Y + area.Height; y++)
        {
            EdgeLine(context, block, allowed, y, neighbours, offsets, limits);
        }
    }

    /// <summary>Applies edge offset to one row of a block: the samples inside the block eight at a time where vectors are available, and the samples at its ends one by one.</summary>
    /// <param name="context">The slice context.</param>
    /// <param name="block">The block.</param>
    /// <param name="allowed">Whether each of the 3x3 coding tree blocks around the block may be used.</param>
    /// <param name="y">The row.</param>
    /// <param name="neighbours">The column and row distance from a sample to each of its two neighbours.</param>
    /// <param name="offsets">The four offsets.</param>
    /// <param name="limits">The largest sample value and which paths apply.</param>
    private static void EdgeLine(
        H265SliceContext context,
        in H265SaoBlock block,
        ReadOnlySpan<bool> allowed,
        int y,
        H265SaoNeighbours neighbours,
        ReadOnlySpan<int> offsets,
        H265SaoLimits limits)
    {
        var area = block.Area;
        var from = area.X;
        var to = area.X + area.Width;

        // Samples that are not on the edge of the block have both neighbours in the block, so the vector path can take them without any checks.
        var interior = limits.Vector && y > area.Y && y < area.Y + area.Height - 1;
        if (interior)
        {
            from++;
            to--;
            var vectorEnd = from + ((to - from) & ~(ShortLanes - 1));
            var plane = block.PlaneWidth;
            EdgeRun(block, new((neighbours.Vertical0 * plane) + neighbours.Horizontal0, (neighbours.Vertical1 * plane) + neighbours.Horizontal1), offsets, (y * plane) + from, vectorEnd - from);
            from = vectorEnd;
            EdgeSample(context, block, allowed, new(area.X, y), neighbours, offsets, limits);
            EdgeSample(context, block, allowed, new(to, y), neighbours, offsets, limits);
        }

        for (var x = from; x < to; x++)
        {
            EdgeSample(context, block, allowed, new(x, y), neighbours, offsets, limits);
        }
    }

    /// <summary>Applies edge offset to one sample, checking that both neighbours may be used.</summary>
    /// <param name="context">The slice context.</param>
    /// <param name="block">The block.</param>
    /// <param name="allowed">Whether each of the 3x3 coding tree blocks around the block may be used.</param>
    /// <param name="at">The sample's column and row.</param>
    /// <param name="neighbours">The column and row distance from a sample to each of its two neighbours.</param>
    /// <param name="offsets">The four offsets.</param>
    /// <param name="limits">The largest sample value and whether some samples may be exempt because they are lossless.</param>
    private static void EdgeSample(
        H265SliceContext context,
        in H265SaoBlock block,
        ReadOnlySpan<bool> allowed,
        SamplePlace at,
        H265SaoNeighbours neighbours,
        ReadOnlySpan<int> offsets,
        H265SaoLimits limits)
    {
        var (x, y) = at;
        var plane = block.PlaneWidth;
        var firstX = x + neighbours.Horizontal0;
        var firstY = y + neighbours.Vertical0;
        var secondX = x + neighbours.Horizontal1;
        var secondY = y + neighbours.Vertical1;
        if (!Neighbour(block, allowed, firstX, firstY) || !Neighbour(block, allowed, secondX, secondY))
        {
            return;
        }

        var index = (y * plane) + x;
        int centre = block.Source[index];
        var first = block.Source[(firstY * plane) + firstX];
        var second = block.Source[(secondY * plane) + secondX];
        var category = Categories[SignBias + Math.Sign(centre - first) + Math.Sign(centre - second)];
        if (category != 0 && !(limits.Protected && IsProtected(context, block.Component, x, y)))
        {
            block.Target[index] = (ushort)Math.Clamp(centre + offsets[category - 1], 0, limits.Maximum);
        }
    }

    /// <summary>Applies edge offset to a run of samples that all have both neighbours inside the block, eight at a time.</summary>
    /// <param name="block">The block.</param>
    /// <param name="steps">The distance in the plane from a sample to each of its two neighbours.</param>
    /// <param name="offsets">The four offsets: valley, step, step, peak.</param>
    /// <param name="start">The index in the plane of the first sample.</param>
    /// <param name="count">The number of samples, a multiple of eight.</param>
    private static void EdgeRun(in H265SaoBlock block, H265SaoSteps steps, ReadOnlySpan<int> offsets, int start, int count)
    {
        ReadOnlySpan<short> source = MemoryMarshal.Cast<ushort, short>(block.Source.AsSpan());
        var target = MemoryMarshal.Cast<ushort, short>(block.Target.AsSpan());
        var valley = Vector128.Create((short)offsets[0]);
        var stepDown = Vector128.Create((short)offsets[1]);
        var stepUp = Vector128.Create((short)offsets[2]);
        var peak = Vector128.Create((short)offsets[3]);
        var maximum = Vector128.Create((short)((1 << block.BitDepth) - 1));
        for (var index = start; index < start + count; index += ShortLanes)
        {
            var centre = Vector128.Create(source.Slice(index, ShortLanes));
            var first = Vector128.Create(source.Slice(index + steps.First, ShortLanes));
            var second = Vector128.Create(source.Slice(index + steps.Second, ShortLanes));

            // The sign of a difference is the count of smaller neighbours less the count of larger ones; the masks are -1 where true.
            var sum = Vector128.LessThan(centre, first) - Vector128.GreaterThan(centre, first) + Vector128.LessThan(centre, second) - Vector128.GreaterThan(centre, second);
            var offset = Vector128.ConditionalSelect(Vector128.Equals(sum, Vector128.Create(ValleySum)), valley, Vector128<short>.Zero);
            offset = Vector128.ConditionalSelect(Vector128.Equals(sum, Vector128.Create((short)-1)), stepDown, offset);
            offset = Vector128.ConditionalSelect(Vector128.Equals(sum, Vector128.Create((short)1)), stepUp, offset);
            offset = Vector128.ConditionalSelect(Vector128.Equals(sum, Vector128.Create(PeakSum)), peak, offset);
            Vector128.Min(Vector128.Max(centre + offset, Vector128<short>.Zero), maximum).CopyTo(target.Slice(index, ShortLanes));
        }
    }

    /// <summary>Applies band offset to a run of samples, eight at a time.</summary>
    /// <param name="block">The block.</param>
    /// <param name="leftBand">The first of the four bands that get an offset.</param>
    /// <param name="offsets">The four offsets.</param>
    /// <param name="start">The index in the plane of the first sample.</param>
    /// <param name="count">The number of samples, a multiple of eight.</param>
    private static void BandRow(in H265SaoBlock block, int leftBand, ReadOnlySpan<int> offsets, int start, int count)
    {
        ReadOnlySpan<short> source = MemoryMarshal.Cast<ushort, short>(block.Source.AsSpan());
        var target = MemoryMarshal.Cast<ushort, short>(block.Target.AsSpan());
        var shift = block.BitDepth - BandBits;
        var maximum = Vector128.Create((short)((1 << block.BitDepth) - 1));
        for (var index = start; index < start + count; index += ShortLanes)
        {
            var value = Vector128.Create(source.Slice(index, ShortLanes));
            var band = Vector128.ShiftRightLogical(value, shift);
            var offset = Vector128<short>.Zero;
            for (var k = 0; k < H265SaoParams.OffsetCount; k++)
            {
                offset += Vector128.Create((short)offsets[k]) & Vector128.Equals(band, Vector128.Create((short)((k + leftBand) & (Bands - 1))));
            }

            Vector128.Min(Vector128.Max(value + offset, Vector128<short>.Zero), maximum).CopyTo(target.Slice(index, ShortLanes));
        }
    }

    /// <summary>Tells whether a neighbouring sample may be used: it is inside the picture and, when it lies in another coding tree block, that block may be crossed.</summary>
    /// <param name="block">The block.</param>
    /// <param name="allowed">Whether each of the 3x3 coding tree blocks around the block may be used.</param>
    /// <param name="x">The neighbour's column.</param>
    /// <param name="y">The neighbour's row.</param>
    /// <returns><see langword="true"/> when the neighbour can be used.</returns>
    private static bool Neighbour(in H265SaoBlock block, ReadOnlySpan<bool> allowed, int x, int y)
    {
        if ((uint)x >= (uint)block.PlaneWidth || (uint)y >= (uint)block.PlaneHeight)
        {
            return false;
        }

        var area = block.Area;
        return allowed[(Side(y, area.Y, area.Height) * Around) + Side(x, area.X, area.Width)];
    }

    /// <summary>Tells on which side of a block a coordinate lies along one axis.</summary>
    /// <param name="value">The coordinate.</param>
    /// <param name="start">The first coordinate of the block.</param>
    /// <param name="size">The size of the block.</param>
    /// <returns>0 before the block, 1 inside it, 2 after it.</returns>
    private static int Side(int value, int start, int size)
    {
        if (value < start)
        {
            return 0;
        }

        return value >= start + size ? Around - 1 : 1;
    }

    /// <summary>Works out which of the coding tree blocks around a block may supply neighbours: those in the picture whose slice and tile settings allow the filter to cross into them.</summary>
    /// <param name="context">The slice context.</param>
    /// <param name="ctb">The block.</param>
    /// <param name="allowed">Receives a flag for each of the 3x3 blocks.</param>
    private static void FillAllowed(H265SliceContext context, int ctb, Span<bool> allowed)
    {
        var layout = context.Layout;
        var picture = context.Picture;
        var rx = ctb % layout.WidthInCtbs;
        var ry = ctb / layout.WidthInCtbs;
        for (var dy = -1; dy <= 1; dy++)
        {
            for (var dx = -1; dx <= 1; dx++)
            {
                var nx = rx + dx;
                var ny = ry + dy;
                var slot = ((dy + 1) * Around) + dx + 1;
                if (nx < 0 || ny < 0 || nx >= layout.WidthInCtbs || ny >= layout.HeightInCtbs)
                {
                    allowed[slot] = false;
                    continue;
                }

                var neighbour = (ny * layout.WidthInCtbs) + nx;
                allowed[slot] = neighbour == ctb || CanCross(context, picture, ctb, neighbour);
            }
        }
    }

    /// <summary>Tells whether the filter may use samples of another coding tree block (8.7.3.2).</summary>
    /// <param name="context">The slice context.</param>
    /// <param name="picture">The picture.</param>
    /// <param name="ctb">The block being filtered.</param>
    /// <param name="neighbour">The block the neighbour lies in.</param>
    /// <returns><see langword="true"/> when the neighbour may be used.</returns>
    private static bool CanCross(H265SliceContext context, H265Picture picture, int ctb, int neighbour)
    {
        var layout = context.Layout;
        var header = picture.HeaderOfCtb[ctb];
        var other = picture.HeaderOfCtb[neighbour];
        if (header is null || other is null)
        {
            return false;
        }

        if (picture.SliceOfCtb[ctb] != picture.SliceOfCtb[neighbour])
        {
            var neighbourIsEarlier = layout.RsToTs[neighbour] < layout.RsToTs[ctb];
            if (!(neighbourIsEarlier ? header : other).Deblocking.LoopFilterAcrossSlices)
            {
                return false;
            }
        }

        return context.Pps.Tiles.LoopFilterAcrossTiles || layout.TileIdByRs[ctb] == layout.TileIdByRs[neighbour];
    }

    /// <summary>Tells whether a sample belongs to a coding unit that the in-loop filters leave alone.</summary>
    /// <param name="context">The slice context.</param>
    /// <param name="component">The colour component.</param>
    /// <param name="x">The column in the colour plane.</param>
    /// <param name="y">The row in the colour plane.</param>
    /// <returns><see langword="true"/> when the sample must not be changed.</returns>
    private static bool IsProtected(H265SliceContext context, int component, int x, int y)
    {
        var picture = context.Picture;
        var lumaX = component == 0 ? x : x * context.Sps.SubWidthC;
        var lumaY = component == 0 ? y : y * context.Sps.SubHeightC;
        return (picture.Flags[picture.GridIndex(lumaX, lumaY)] & H265Picture.BypassFlag) != 0;
    }
}
