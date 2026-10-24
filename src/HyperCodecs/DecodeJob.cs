// Copyright (c) 2026 Glenn Watson. All rights reserved.
// Glenn Watson licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;

namespace HyperCodecs;

/// <summary>
/// The part of a job to decode one picture that does not depend on the codec: whether it has finished, what it threw, and who still holds it. A job is run
/// once and then kept to be used again, and two holders can outlast the run: the decoder's queue of jobs under way, and the frame that is still to be
/// given out. The job can be used again only when both have let go.
/// </summary>
internal class DecodeJob
{
    /// <summary>Guards the waiting for the job to finish.</summary>
    private readonly object _gate = new();

    /// <summary>What the job threw, or <see langword="null"/>.</summary>
    private ExceptionDispatchInfo? _failure;

    /// <summary>Whether no run of the job is under way.</summary>
    private bool _finished = true;

    /// <summary>Whether the decoder still has the job in its queue of jobs under way.</summary>
    private bool _queued;

    /// <summary>Whether the decoder is still to give out the job's frame.</summary>
    private bool _owed;

    /// <summary>Gets a value indicating whether the job has finished, successfully or not.</summary>
    internal bool IsDone => Volatile.Read(ref _finished);

    /// <summary>Notes that the job has been put in the decoder's queue of jobs under way, and whether its frame is still to be given out.</summary>
    /// <param name="hasFrame">Whether the job has a frame to give out.</param>
    internal void Queue(bool hasFrame)
    {
        _queued = true;
        _owed = hasFrame;
    }

    /// <summary>Notes that the job has left the queue of jobs under way.</summary>
    /// <returns><see langword="true"/> when nothing else holds the job, so it can be used again.</returns>
    internal bool Unqueue()
    {
        _queued = false;
        return !_owed;
    }

    /// <summary>Notes that the job's frame has been given out.</summary>
    /// <returns><see langword="true"/> when nothing else holds the job, so it can be used again.</returns>
    internal bool Settle()
    {
        _owed = false;
        return !_queued;
    }

    /// <summary>Records that the job failed.</summary>
    /// <param name="exception">What it threw.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void Fail(Exception exception) => _failure = ExceptionDispatchInfo.Capture(exception);

    /// <summary>Waits until the job has finished, and throws what it threw.</summary>
    internal void Wait()
    {
        WaitQuietly();
        _failure?.Throw();
    }

    /// <summary>Waits until the job has finished and keeps what it threw, for a caller that reports it later.</summary>
    internal void WaitQuietly()
    {
        if (IsDone)
        {
            return;
        }

        lock (_gate)
        {
            while (!_finished)
            {
                _ = Monitor.Wait(_gate);
            }
        }
    }

    /// <summary>Starts a run of the job: it has not finished, and nothing holds it yet.</summary>
    protected void StartRun()
    {
        _failure = null;
        _queued = false;
        _owed = false;
        Volatile.Write(ref _finished, false);
    }

    /// <summary>Ends a run of the job and wakes whoever waits for it.</summary>
    protected void EndRun()
    {
        lock (_gate)
        {
            _finished = true;
            Monitor.PulseAll(_gate);
        }
    }
}
