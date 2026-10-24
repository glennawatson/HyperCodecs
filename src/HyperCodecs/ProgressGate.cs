// Copyright (c) 2026 Glenn Watson. All rights reserved.
// Glenn Watson licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Diagnostics;

namespace HyperCodecs;

/// <summary>
/// Lets a thread that decodes one picture wait for another to get far enough, and wakes it when it has. The wait spins for a moment, because rows are
/// usually finished soon, then gives up the thread's token and sleeps, so a waiting picture takes no processor from the ones that are being decoded.
/// </summary>
[DebuggerDisplay("ProgressGate")]
internal sealed class ProgressGate
{
    /// <summary>The spins before a wait gives up the processor.</summary>
    private const int SpinsBeforeSleep = 40;

    /// <summary>How long a sleeping wait goes without a wake-up before it looks again, in milliseconds; a safety net, since every change of progress wakes the waiters.</summary>
    private const int RecheckMilliseconds = 50;

    /// <summary>Guards the sleeping of waiters.</summary>
    private readonly object _gate = new();

    /// <summary>The number of threads that are sleeping or about to.</summary>
    private int _waiters;

    /// <summary>Wakes the threads that are waiting, if there are any. Call it after the progress they wait for has been published.</summary>
    internal void Pulse()
    {
        // The full fence orders the publication of the progress before the read of the count; a waiter counts itself before it looks at the progress.
        Interlocked.MemoryBarrier();
        if (Volatile.Read(ref _waiters) == 0)
        {
            return;
        }

        lock (_gate)
        {
            Monitor.PulseAll(_gate);
        }
    }

    /// <summary>Waits until a condition holds.</summary>
    /// <typeparam name="TCondition">The kind of condition.</typeparam>
    /// <param name="condition">The condition, which throws when waiting for it is pointless.</param>
    /// <param name="tokens">The caller's tokens to give up while it sleeps, or <see langword="null"/> when it holds none.</param>
    internal void Wait<TCondition>(in TCondition condition, WorkTokens? tokens)
        where TCondition : struct, IProgressCondition
    {
        var spin = default(SpinWait);
        for (var i = 0; i < SpinsBeforeSleep; i++)
        {
            if (condition.IsMet())
            {
                return;
            }

            spin.SpinOnce(-1);
        }

        Sleep(condition, tokens);
    }

    /// <summary>Sleeps until a condition holds.</summary>
    /// <typeparam name="TCondition">The kind of condition.</typeparam>
    /// <param name="condition">The condition.</param>
    /// <param name="tokens">The caller's tokens to give up while it sleeps, or <see langword="null"/>.</param>
    private void Sleep<TCondition>(in TCondition condition, WorkTokens? tokens)
        where TCondition : struct, IProgressCondition
    {
        tokens?.Release();
        _ = Interlocked.Increment(ref _waiters);
        try
        {
            lock (_gate)
            {
                while (!condition.IsMet())
                {
                    _ = Monitor.Wait(_gate, RecheckMilliseconds);
                }
            }
        }
        finally
        {
            _ = Interlocked.Decrement(ref _waiters);
            tokens?.Acquire();
        }
    }
}
