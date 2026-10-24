// Copyright (c) 2026 Glenn Watson. All rights reserved.
// Glenn Watson licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace HyperCodecs.H265;

/// <summary>
/// Applies the in-loop filters to a picture while it is being decoded: deblocking, then sample adaptive offset (8.7). Intra prediction reads the samples of
/// the row above before they are filtered, and the last reader of a row's samples is the row below it, so deblocking a row waits for the row below to be
/// decoded. Deblocking a row changes the three bottom lines of the row above, and offsetting a row reads the line above and the line below it as
/// deblocked, so sample adaptive offset waits until the row below has been deblocked and works from a copy of the deblocked samples.
/// </summary>
internal static class H265LoopFilter
{
    /// <summary>The lines of luma above an edge that the deblocking of the row below can still change.</summary>
    private const int DeblockReach = 3;

    /// <summary>The rows that must be deblocked, counting the row itself, before sample adaptive offset can be applied to a row: the one below it too.</summary>
    private const int RowsBelowNeeded = 2;

    /// <summary>Records that a coding tree block has been decoded and filters the rows that can be filtered.</summary>
    /// <param name="context">The slice context of the picture.</param>
    /// <param name="rs">The raster scan address of the block.</param>
    internal static void CtbDecoded(H265SliceContext context, int rs)
    {
        if (context.Picture.CtbDecoded(rs))
        {
            Advance(context, false);
        }
    }

    /// <summary>Filters the rows not yet filtered, whether or not every block of them was decoded, and marks the picture finished.</summary>
    /// <param name="context">The slice context of the picture, all of whose segments have been decoded.</param>
    internal static void Finish(H265SliceContext context)
    {
        Advance(context, true);
        context.Picture.FinishDecoding();
    }

    /// <summary>Runs the filters on every row that has been decoded far enough, and says how much of the picture is final.</summary>
    /// <param name="context">The slice context.</param>
    /// <param name="all">Whether every row counts as decoded.</param>
    private static void Advance(H265SliceContext context, bool all)
    {
        var picture = context.Picture;
        var rows = context.Layout.HeightInCtbs;
        while (picture.DeblockedRows < rows && (all || picture.CanDeblock(picture.DeblockedRows)))
        {
            H265Deblocking.FilterRow(context, picture.DeblockedRows);
            picture.DeblockedRows++;
        }

        var deblocked = picture.DeblockedRows == rows ? picture.Height : Math.Max(0, (picture.DeblockedRows << context.Layout.CtbLog2) - DeblockReach);
        if (!context.Sps.Tools.SaoEnabled)
        {
            picture.PublishLines(deblocked);
            return;
        }

        CopyDeblocked(context, deblocked);
        while (picture.SaoRows < rows && picture.DeblockedRows >= Math.Min(picture.SaoRows + RowsBelowNeeded, rows))
        {
            H265Sao.FilterRow(context, picture.SaoRows);
            picture.SaoRows++;
        }

        picture.PublishLines(picture.SaoRows == rows ? picture.Height : picture.SaoRows << context.Layout.CtbLog2);
    }

    /// <summary>Copies the lines that deblocking will no longer change into the work areas that sample adaptive offset reads.</summary>
    /// <param name="context">The slice context.</param>
    /// <param name="lines">The lines of luma that are deblocked.</param>
    private static void CopyDeblocked(H265SliceContext context, int lines)
    {
        var picture = context.Picture;
        var sps = context.Sps;
        var components = context.HasChroma ? H265SaoParams.Components : 1;
        for (var component = 0; component < components; component++)
        {
            var plane = picture.PlaneOf(component);
            var stride = picture.StrideOf(component);
            var target = lines == picture.Height ? plane.Length / stride : lines / (component == 0 ? 1 : sps.SubHeightC);
            var from = picture.SaoCopied[component];
            plane.AsSpan(from * stride, Math.Max(target - from, 0) * stride).CopyTo(context.SaoSources[component].AsSpan(from * stride));
            picture.SaoCopied[component] = Math.Max(target, from);
        }
    }
}
