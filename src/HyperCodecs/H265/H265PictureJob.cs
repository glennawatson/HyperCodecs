// Copyright (c) 2026 Glenn Watson. All rights reserved.
// Glenn Watson licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace HyperCodecs.H265;

/// <summary>
/// One picture to decode: its slice segments, the parameter sets and layout to decode them with, the frame to copy the finished picture into when it is to
/// be output, and the pictures it reads, which stay in use until the job is done. A job runs on the thread that reads the packet or on a worker thread.
/// Jobs are kept and used again.
/// </summary>
[DebuggerDisplay("H265PictureJob: {SliceCount} segments, done {IsDone}")]
internal sealed class H265PictureJob : DecodeJob
{
    /// <summary>The slice jobs, of which the first <see cref="SliceCount"/> are in use.</summary>
    private readonly List<H265SliceJob> _slices = [];

    /// <summary>The pictures the job holds in use: the one it decodes into and the ones it reads.</summary>
    private readonly List<H265Picture> _users = [];

    /// <summary>Gets the picture the job decodes into.</summary>
    internal H265Picture Picture { get; private set; } = null!;

    /// <summary>Gets the sequence parameter set.</summary>
    internal H265Sps Sps { get; private set; } = null!;

    /// <summary>Gets the picture parameter set.</summary>
    internal H265Pps Pps { get; private set; } = null!;

    /// <summary>Gets the layout of the picture.</summary>
    internal H265PictureLayout Layout { get; private set; } = null!;

    /// <summary>Gets the frame the finished picture is copied into, or <see langword="null"/> for a picture that is not output.</summary>
    internal VideoFrame? Frame { get; private set; }

    /// <summary>Gets the number of slice segments.</summary>
    internal int SliceCount { get; private set; }

    /// <summary>Gets one of the slice segments.</summary>
    /// <param name="index">The segment's index, from 0.</param>
    /// <returns>The segment.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal H265SliceJob Slice(int index) => _slices[index];

    /// <summary>Starts a job for a picture: it has no slice segments yet.</summary>
    /// <param name="picture">The picture to decode into.</param>
    /// <param name="sets">The parameter sets and layout.</param>
    /// <param name="frame">The frame to copy the finished picture into, or <see langword="null"/> when the picture is not output.</param>
    internal void Begin(H265Picture picture, H265PictureSetup sets, VideoFrame? frame)
    {
        Picture = picture;
        Sps = sets.Sps;
        Pps = sets.Pps;
        Layout = sets.Layout;
        Frame = frame;
        SliceCount = 0;
        StartRun();
        Use(picture);
    }

    /// <summary>Takes the next slice job.</summary>
    /// <returns>The slice job, which the caller fills.</returns>
    internal H265SliceJob NextSlice()
    {
        if (SliceCount == _slices.Count)
        {
            _slices.Add(new());
        }

        var slice = _slices[SliceCount];
        SliceCount++;
        return slice;
    }

    /// <summary>Holds a picture in use until the job is done, unless the job already does.</summary>
    /// <param name="picture">The picture.</param>
    internal void Use(H265Picture picture)
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
