// Copyright (c) 2026 Glenn Watson. All rights reserved.
// Glenn Watson licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Collections.Concurrent;
using System.Diagnostics;

namespace HyperCodecs;

/// <summary>
/// A fixed set of threads that each decode whole pictures. Jobs start in the order they are submitted, so a job that waits for the progress of an earlier
/// picture only ever waits for a job that is running or finished, and the threads cannot hold each other up for ever. Each thread has working memory of its own.
/// </summary>
/// <typeparam name="TJob">The kind of job: one picture to decode.</typeparam>
/// <typeparam name="TWork">The working memory one thread uses to decode a picture.</typeparam>
[DebuggerDisplay("FrameWorkers: {Count} threads")]
internal sealed class FrameWorkers<TJob, TWork> : IDisposable
    where TJob : class
    where TWork : class
{
    /// <summary>The threads.</summary>
    private readonly Thread[] _threads;

    /// <summary>The jobs waiting to start, oldest first.</summary>
    private readonly ConcurrentQueue<TJob> _jobs = new();

    /// <summary>Counts the jobs waiting, so a thread with nothing to do sleeps.</summary>
    private readonly SemaphoreSlim _waiting = new(0);

    /// <summary>The work a thread does for each job. It must not throw.</summary>
    private readonly Action<TJob, TWork> _run;

    /// <summary>Lets only as many threads run as the caller allows; a thread that waits for another picture gives its token up.</summary>
    private readonly WorkTokens _tokens;

    /// <summary>Set to 1 once the threads have been told to stop.</summary>
    private int _disposed;

    /// <summary>Initializes a new instance of the <see cref="FrameWorkers{TJob, TWork}"/> class and starts the threads.</summary>
    /// <param name="count">The number of threads.</param>
    /// <param name="tokens">The tokens that let threads run, which the workers dispose with themselves.</param>
    /// <param name="createWork">Makes the working memory of one thread.</param>
    /// <param name="run">The work a thread does for each job. It must not throw.</param>
    /// <param name="name">The name of the threads.</param>
    internal FrameWorkers(int count, WorkTokens tokens, Func<TWork> createWork, Action<TJob, TWork> run, string name)
    {
        _run = run;
        _tokens = tokens;
        _threads = new Thread[count];
        for (var i = 0; i < count; i++)
        {
            var work = createWork();
            _threads[i] = new(() => Loop(work)) { IsBackground = true, Name = name };
            _threads[i].Start();
        }
    }

    /// <summary>Gets the number of threads.</summary>
    internal int Count => _threads.Length;

    /// <inheritdoc/>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _ = _waiting.Release(_threads.Length);
        foreach (var thread in _threads)
        {
            thread.Join();
        }

        _waiting.Dispose();
        _tokens.Dispose();
    }

    /// <summary>Hands a job to the threads. It starts when a thread is free and every earlier job has started.</summary>
    /// <param name="job">The job.</param>
    internal void Submit(TJob job)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        _jobs.Enqueue(job);
        _ = _waiting.Release();
    }

    /// <summary>The body of each thread: wait for a job, run it, and wait again.</summary>
    /// <param name="work">The thread's working memory.</param>
    private void Loop(TWork work)
    {
        while (true)
        {
            _waiting.Wait();
            if (Volatile.Read(ref _disposed) != 0)
            {
                return;
            }

            if (!_jobs.TryDequeue(out var job))
            {
                continue;
            }

            _tokens.Acquire();
            try
            {
                _run(job, work);
            }
            finally
            {
                _tokens.Release();
            }
        }
    }
}
