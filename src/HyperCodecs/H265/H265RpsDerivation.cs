// Copyright (c) 2026 Glenn Watson. All rights reserved.
// Glenn Watson licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace HyperCodecs.H265;

/// <summary>Works out which pictures of the buffer the current picture may refer to and marks the rest as unused (8.3.2).</summary>
internal static class H265RpsDerivation
{
    /// <summary>Derives the reference picture set of a picture.</summary>
    /// <param name="set">Receives the pictures the current picture may use; it is emptied first.</param>
    /// <param name="dpb">The decoded picture buffer.</param>
    /// <param name="sps">The sequence parameter set.</param>
    /// <param name="header">The slice segment header.</param>
    /// <param name="poc">The picture order count of the current picture.</param>
    /// <param name="idr">Whether the current picture is an IDR picture, which refers to nothing.</param>
    /// <param name="makeMissing">Makes a stand-in picture with a given order count, for a reference that is not in the buffer.</param>
    internal static void Derive(H265RefPicSet set, H265Dpb dpb, H265Sps sps, H265SliceHeader header, int poc, bool idr, Func<int, H265Picture> makeMissing)
    {
        set.Clear();
        if (idr)
        {
            dpb.UnmarkAll();
            return;
        }

        foreach (var picture in dpb.Pictures)
        {
            picture.IsKept = false;
        }

        FindLongTerm(set.LtCurr, dpb, header, poc, 1 << sps.Log2MaxPocLsb, makeMissing);
        var rps = header.ShortTerm;
        FindShortTerm(set.StCurrBefore, dpb, rps.DeltaPocS0, rps.UsedS0, poc, makeMissing);
        FindShortTerm(set.StCurrAfter, dpb, rps.DeltaPocS1, rps.UsedS1, poc, makeMissing);
        foreach (var picture in dpb.Pictures)
        {
            if (!picture.IsKept)
            {
                picture.ReferenceState = 0;
            }
        }
    }

    /// <summary>Finds the long-term pictures the slice header names, marking them long-term.</summary>
    /// <param name="used">Receives the pictures the current picture uses.</param>
    /// <param name="dpb">The decoded picture buffer.</param>
    /// <param name="header">The slice segment header.</param>
    /// <param name="poc">The current picture order count.</param>
    /// <param name="maxLsb">The range of the low bits of the picture order count.</param>
    /// <param name="makeMissing">Makes a stand-in for a missing picture.</param>
    private static void FindLongTerm(List<H265Picture> used, H265Dpb dpb, H265SliceHeader header, int poc, int maxLsb, Func<int, H265Picture> makeMissing)
    {
        foreach (var entry in header.LongTerm)
        {
            var target = entry.PocLsb;
            if (entry.MsbPresent)
            {
                target += poc - (entry.DeltaPocMsbCycle * maxLsb) - (poc & (maxLsb - 1));
            }

            var picture = FindReference(dpb, target, entry.MsbPresent ? -1 : maxLsb - 1) ?? CreateMissing(dpb, makeMissing, target);
            picture.ReferenceState = H265Picture.LongTerm;
            picture.IsKept = true;
            if (entry.Used)
            {
                used.Add(picture);
            }
        }
    }

    /// <summary>Finds the short-term pictures of one side of the set.</summary>
    /// <param name="used">Receives the pictures the current picture uses.</param>
    /// <param name="dpb">The decoded picture buffer.</param>
    /// <param name="deltas">The picture order count distances.</param>
    /// <param name="usedFlags">Whether the current picture refers to each.</param>
    /// <param name="poc">The current picture order count.</param>
    /// <param name="makeMissing">Makes a stand-in for a missing picture.</param>
    private static void FindShortTerm(List<H265Picture> used, H265Dpb dpb, int[] deltas, bool[] usedFlags, int poc, Func<int, H265Picture> makeMissing)
    {
        for (var i = 0; i < deltas.Length; i++)
        {
            var target = poc + deltas[i];
            var picture = FindShortTermAt(dpb, target) ?? CreateMissing(dpb, makeMissing, target);
            picture.ReferenceState = picture.ReferenceState == H265Picture.Unused ? H265Picture.ShortTerm : picture.ReferenceState;
            picture.IsKept = true;
            if (usedFlags[i])
            {
                used.Add(picture);
            }
        }
    }

    /// <summary>Finds a short-term reference picture by its picture order count.</summary>
    /// <param name="dpb">The decoded picture buffer.</param>
    /// <param name="poc">The count.</param>
    /// <returns>The picture, or <see langword="null"/>.</returns>
    private static H265Picture? FindShortTermAt(H265Dpb dpb, int poc)
    {
        var pictures = dpb.Pictures;
        for (var i = 0; i < pictures.Count; i++)
        {
            if (pictures[i].ReferenceState == H265Picture.ShortTerm && pictures[i].Poc == poc)
            {
                return pictures[i];
            }
        }

        return null;
    }

    /// <summary>Finds a picture used for reference by its picture order count.</summary>
    /// <param name="dpb">The decoded picture buffer.</param>
    /// <param name="poc">The count, or only its low bits when a mask is given.</param>
    /// <param name="mask">A mask for the low bits to compare, or -1 to compare the whole count.</param>
    /// <returns>The picture, or <see langword="null"/>.</returns>
    private static H265Picture? FindReference(H265Dpb dpb, int poc, int mask)
    {
        var pictures = dpb.Pictures;
        for (var i = 0; i < pictures.Count; i++)
        {
            var candidate = pictures[i];
            if (candidate.ReferenceState != 0 && (mask < 0 ? candidate.Poc == poc : (candidate.Poc & mask) == poc))
            {
                return candidate;
            }
        }

        return null;
    }

    /// <summary>Makes a stand-in for a reference picture that is missing and adds it to the buffer.</summary>
    /// <param name="dpb">The decoded picture buffer.</param>
    /// <param name="makeMissing">Makes the picture.</param>
    /// <param name="poc">Its picture order count.</param>
    /// <returns>The picture.</returns>
    private static H265Picture CreateMissing(H265Dpb dpb, Func<int, H265Picture> makeMissing, int poc)
    {
        var picture = makeMissing(poc);
        dpb.Pictures.Add(picture);
        return picture;
    }
}
