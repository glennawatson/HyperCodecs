// Copyright (c) 2026 Glenn Watson. All rights reserved.
// Glenn Watson licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace HyperCodecs.H264;

/// <summary>Decodes the slices of a picture job, finishes its filtering, and copies it into its frame.</summary>
internal static class H264PictureRunner
{
    /// <summary>The pixels in a crop unit of 4:2:0 video.</summary>
    private const int CropUnit = 2;

    /// <summary>Runs a job. It does not throw: a failure is kept in the job, and pictures that wait for this one are told it failed.</summary>
    /// <param name="job">The job.</param>
    /// <param name="work">The working memory of the thread running the job.</param>
    internal static void Run(H264PictureJob job, H264SliceWork work)
    {
        try
        {
            job.Picture.ClearData();
            for (var index = 0; index < job.SliceCount; index++)
            {
                DecodeSlice(job.Picture, job.Slice(index), work);
            }

            H264Deblocking.Finish(job.Picture);
            CopyFrame(job.Picture, job.Sps!, job.Frame);
        }
        catch (Exception exception)
        {
            job.Picture.AbortDecoding();
            job.Fail(exception);
        }
        finally
        {
            job.Finish();
        }
    }

    /// <summary>Decodes one slice into its picture.</summary>
    /// <param name="picture">The picture.</param>
    /// <param name="slice">The slice.</param>
    /// <param name="work">The working memory of the thread.</param>
    private static void DecodeSlice(H264Picture picture, H264SliceJob slice, H264SliceWork work)
    {
        var header = slice.Header;
        var pps = slice.Sets.Pps;
        var startQp = pps.PicInitQp + header.Tail.QpDelta;
        var rbsp = slice.Rbsp.AsSpan(0, slice.Length);
        if (header.IsP || header.IsB)
        {
            DecodeInterSlice(picture, slice, work);
        }
        else if (pps.Cabac)
        {
            var decoder = work.CabacIntra;
            decoder.Bind(picture, pps, slice.Scale, slice.Number, startQp, work);
            decoder.Decode(rbsp, slice.DataBit, header.FirstMb);
        }
        else
        {
            var decoder = work.CavlcIntra;
            decoder.Bind(picture, pps, slice.Scale, slice.Number, startQp, work.Mb);
            decoder.Decode(rbsp, slice.DataBit, header.FirstMb);
        }
    }

    /// <summary>Decodes a P or B slice into its picture.</summary>
    /// <param name="picture">The picture.</param>
    /// <param name="slice">The slice.</param>
    /// <param name="work">The working memory of the thread.</param>
    private static void DecodeInterSlice(H264Picture picture, H264SliceJob slice, H264SliceWork work)
    {
        var header = slice.Header;
        var sets = slice.Sets;
        var rbsp = slice.Rbsp.AsSpan(0, slice.Length);
        if (header.IsB)
        {
            DecodeBSlice(picture, slice, new(slice.List0, slice.List1), work);
            return;
        }

        if (sets.Pps.Cabac)
        {
            var decoder = work.CabacP;
            decoder.Bind(picture, sets.Pps, slice.Scale, slice.Number, header, slice.List0);
            decoder.Decode(rbsp, slice.DataBit, header.FirstMb);
            return;
        }

        var cavlc = work.CavlcP;
        cavlc.Bind(picture, sets.Pps, slice.Scale, slice.Number, header, slice.List0);
        cavlc.Decode(rbsp, slice.DataBit, header.FirstMb);
    }

    /// <summary>Decodes a B slice into its picture.</summary>
    /// <param name="picture">The picture.</param>
    /// <param name="slice">The slice.</param>
    /// <param name="lists">The two reference lists.</param>
    /// <param name="work">The working memory of the thread.</param>
    private static void DecodeBSlice(H264Picture picture, H264SliceJob slice, H264RefLists lists, H264SliceWork work)
    {
        var header = slice.Header;
        var rbsp = slice.Rbsp.AsSpan(0, slice.Length);
        if (slice.Sets.Pps.Cabac)
        {
            var decoder = work.CabacB;
            decoder.Bind(picture, slice.Sets, slice.Scale, slice.Number, header, lists);
            decoder.Decode(rbsp, slice.DataBit, header.FirstMb);
            return;
        }

        var cavlc = work.CavlcB;
        cavlc.Bind(picture, slice.Sets, slice.Scale, slice.Number, header, lists);
        cavlc.Decode(rbsp, slice.DataBit, header.FirstMb);
    }

    /// <summary>Copies the visible part of a decoded picture into a frame.</summary>
    /// <param name="picture">The picture.</param>
    /// <param name="sps">The sequence parameter set, which gives the cropping.</param>
    /// <param name="frame">The frame, of the size of the visible part.</param>
    private static void CopyFrame(H264Picture picture, H264Sps sps, VideoFrame frame)
    {
        var left = CropUnit * sps.Crop.Left;
        var top = CropUnit * sps.Crop.Top;
        for (var row = 0; row < frame.Height; row++)
        {
            Array.Copy(picture.Luma, ((top + row) * picture.LumaStride) + left, frame.Y, row * frame.Width, frame.Width);
        }

        for (var row = 0; row < frame.ChromaHeight; row++)
        {
            var source = (((top >> 1) + row) * picture.ChromaStride) + (left >> 1);
            Array.Copy(picture.Cb, source, frame.U, row * frame.ChromaWidth, frame.ChromaWidth);
            Array.Copy(picture.Cr, source, frame.V, row * frame.ChromaWidth, frame.ChromaWidth);
        }
    }
}
