// Copyright (c) 2026 Glenn Watson. All rights reserved.
// Glenn Watson licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace HyperCodecs.H265;

/// <summary>Decodes the slice segments of a picture job, filters the picture, and copies it into its frame when it is to be output.</summary>
internal static class H265PictureRunner
{
    /// <summary>Runs a job. It does not throw: a failure is kept in the job, and pictures that wait for this one are told it failed.</summary>
    /// <param name="job">The job.</param>
    /// <param name="context">The slice context of the thread running the job.</param>
    internal static void Run(H265PictureJob job, H265SliceContext context)
    {
        try
        {
            job.Picture.ClearData();
            context.Begin(job.Sps, job.Pps, job.Layout, job.Picture);
            for (var index = 0; index < job.SliceCount; index++)
            {
                var slice = job.Slice(index);
                context.LoadSlice(slice);
                H265SliceDecoder.Decode(slice.Rbsp.AsSpan(0, slice.Length), context);
            }

            H265LoopFilter.Finish(context);
            if (job.Frame is { } frame)
            {
                _ = H265FrameConverter.Convert(job.Picture, job.Sps, frame);
            }
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
}
