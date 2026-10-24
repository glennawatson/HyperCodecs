// Copyright (c) 2026 Glenn Watson. All rights reserved.
// Glenn Watson licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace HyperCodecs.H264;

/// <summary>
/// One picture to decode: its slices, the frame to copy it into, and the pictures it reads, which stay in use until the job is done. A job runs on the
/// thread that reads the packet or on a worker thread, and the caller waits for it only when it needs the frame. Jobs are kept and used again.
/// </summary>
[DebuggerDisplay("H264PictureJob: {SliceCount} slices, done {IsDone}")]
internal sealed class H264PictureJob : DecodeJob
{
    /// <summary>The slice jobs, of which the first <see cref="SliceCount"/> are in use.</summary>
    private readonly List<H264SliceJob> _slices = [];

    /// <summary>The pictures the job holds in use: the one it decodes into and the ones it reads.</summary>
    private readonly List<H264Picture> _users = [];

    /// <summary>Gets the picture the job decodes into.</summary>
    internal H264Picture Picture { get; private set; } = null!;

    /// <summary>Gets the sequence parameter set of the picture, which gives the cropping.</summary>
    internal H264Sps? Sps { get; private set; }

    /// <summary>Gets the frame the finished picture is copied into.</summary>
    internal VideoFrame Frame { get; private set; } = null!;

    /// <summary>Gets the number of slices.</summary>
    internal int SliceCount { get; private set; }

    /// <summary>Gets one of the slices.</summary>
    /// <param name="index">The slice's index, from 0.</param>
    /// <returns>The slice.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal H264SliceJob Slice(int index) => _slices[index];

    /// <summary>Starts a job for a picture: it has no slices yet.</summary>
    /// <param name="picture">The picture to decode into.</param>
    /// <param name="frame">The frame to copy the finished picture into.</param>
    internal void Begin(H264Picture picture, VideoFrame frame)
    {
        Picture = picture;
        Frame = frame;
        Sps = null;
        SliceCount = 0;
        StartRun();
        Use(picture);
    }

    /// <summary>Takes the next slice job.</summary>
    /// <returns>The slice job, which the caller fills.</returns>
    internal H264SliceJob NextSlice()
    {
        if (SliceCount == _slices.Count)
        {
            _slices.Add(new());
        }

        var slice = _slices[SliceCount];
        SliceCount++;
        return slice;
    }

    /// <summary>Notes the sequence parameter set the picture is decoded with.</summary>
    /// <param name="sps">The set.</param>
    internal void SetSps(H264Sps sps) => Sps = sps;

    /// <summary>Holds a picture in use until the job is done, unless the job already does.</summary>
    /// <param name="picture">The picture.</param>
    internal void Use(H264Picture picture)
    {
        foreach (var user in _users)
        {
            if (ReferenceEquals(user, picture))
            {
                return;
            }
        }

        picture.Acquire();
        _users.Add(picture);
    }

    /// <summary>Lets go of the pictures the job used and says it has finished.</summary>
    internal void Finish()
    {
        foreach (var user in _users)
        {
            user.Release();
        }

        _users.Clear();
        EndRun();
    }
}
