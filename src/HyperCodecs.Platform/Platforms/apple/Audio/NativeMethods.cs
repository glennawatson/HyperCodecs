// Copyright (c) 2026 Glenn Watson. All rights reserved.
// Glenn Watson licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Runtime.InteropServices;

namespace HyperCodecs.Platform.Audio.Apple;

/// <summary>Source generated entry points of the macOS Audio Toolbox functions the sound output uses.</summary>
internal static unsafe partial class NativeMethods
{
    /// <summary>Audio Toolbox, home of the CoreAudio audio queues.</summary>
    internal const string AudioToolbox = "/System/Library/Frameworks/AudioToolbox.framework/AudioToolbox";

    /// <summary>Native <c>AudioQueueNewOutput</c>.</summary>
    /// <param name="format">The sample format.</param>
    /// <param name="callback">Called as each buffer finishes.</param>
    /// <param name="userData">Passed to the callback.</param>
    /// <param name="runLoop">The run loop for callbacks, or null for an internal thread.</param>
    /// <param name="runLoopMode">The run loop mode, or null.</param>
    /// <param name="flags">Reserved, zero.</param>
    /// <param name="queue">Receives the queue.</param>
    /// <returns>An OSStatus.</returns>
    [LibraryImport(AudioToolbox, EntryPoint = "AudioQueueNewOutput")]
    internal static partial int NewOutputQueue(StreamDescription* format, delegate* unmanaged<nint, nint, nint, void> callback, nint userData, nint runLoop, nint runLoopMode, uint flags, nint* queue);

    /// <summary>Native <c>AudioQueueAllocateBuffer</c>.</summary>
    /// <param name="queue">The queue.</param>
    /// <param name="size">The buffer size in bytes.</param>
    /// <param name="buffer">Receives the buffer.</param>
    /// <returns>An OSStatus.</returns>
    [LibraryImport(AudioToolbox, EntryPoint = "AudioQueueAllocateBuffer")]
    internal static partial int AllocateBuffer(nint queue, uint size, nint* buffer);

    /// <summary>Native <c>AudioQueueEnqueueBuffer</c>.</summary>
    /// <param name="queue">The queue.</param>
    /// <param name="buffer">The buffer.</param>
    /// <param name="descriptionCount">Packet descriptions, zero for PCM.</param>
    /// <param name="descriptions">The descriptions, null.</param>
    /// <returns>An OSStatus.</returns>
    [LibraryImport(AudioToolbox, EntryPoint = "AudioQueueEnqueueBuffer")]
    internal static partial int EnqueueBuffer(nint queue, nint buffer, uint descriptionCount, nint descriptions);

    /// <summary>Native <c>AudioQueueStart</c>.</summary>
    /// <param name="queue">The queue.</param>
    /// <param name="startTime">When to start, null for now.</param>
    /// <returns>An OSStatus.</returns>
    [LibraryImport(AudioToolbox, EntryPoint = "AudioQueueStart")]
    internal static partial int StartQueue(nint queue, nint startTime);

    /// <summary>Native <c>AudioQueueFlush</c>.</summary>
    /// <param name="queue">The queue.</param>
    /// <returns>An OSStatus.</returns>
    [LibraryImport(AudioToolbox, EntryPoint = "AudioQueueFlush")]
    internal static partial int FlushQueue(nint queue);

    /// <summary>Native <c>AudioQueueStop</c>.</summary>
    /// <param name="queue">The queue.</param>
    /// <param name="immediate">Nonzero to stop now; zero to stop once the queued audio has played.</param>
    /// <returns>An OSStatus.</returns>
    [LibraryImport(AudioToolbox, EntryPoint = "AudioQueueStop")]
    internal static partial int StopQueue(nint queue, byte immediate);

    /// <summary>Native <c>AudioQueueDispose</c>.</summary>
    /// <param name="queue">The queue.</param>
    /// <param name="immediate">Nonzero to dispose now.</param>
    /// <returns>An OSStatus.</returns>
    [LibraryImport(AudioToolbox, EntryPoint = "AudioQueueDispose")]
    internal static partial int DisposeQueue(nint queue, byte immediate);

    /// <summary>Native <c>AudioQueueGetProperty</c>.</summary>
    /// <param name="queue">The queue.</param>
    /// <param name="property">The property.</param>
    /// <param name="value">Receives the value.</param>
    /// <param name="size">The value's size; receives the size written.</param>
    /// <returns>An OSStatus.</returns>
    [LibraryImport(AudioToolbox, EntryPoint = "AudioQueueGetProperty")]
    internal static partial int GetQueueProperty(nint queue, uint property, void* value, uint* size);
}
