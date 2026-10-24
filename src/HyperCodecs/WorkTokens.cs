// Copyright (c) 2026 Glenn Watson. All rights reserved.
// Glenn Watson licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace HyperCodecs;

/// <summary>
/// The right to run on a processor. A decoder keeps more threads than the number of processors its caller allows it to use, so that a picture waiting for
/// the picture it refers to does not stop the next picture from starting. A thread holds a token while it decodes and gives it up while it waits, so no more
/// threads than the number allowed are ever running.
/// </summary>
[DebuggerDisplay("WorkTokens")]
internal sealed class WorkTokens : IDisposable
{
    /// <summary>Counts the tokens that are free.</summary>
    private readonly SemaphoreSlim _free;

    /// <summary>Initializes a new instance of the <see cref="WorkTokens"/> class.</summary>
    /// <param name="count">The number of tokens, which is the number of threads that may run at once.</param>
    internal WorkTokens(int count) => _free = new(count, count);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Dispose() => _free.Dispose();

    /// <summary>Waits for a token and takes it.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void Acquire() => _free.Wait();

    /// <summary>Gives a token back.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void Release() => _ = _free.Release();
}
