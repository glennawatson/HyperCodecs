// Copyright (c) 2026 Glenn Watson. All rights reserved.
// Glenn Watson licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Runtime.CompilerServices;
using HyperCodecs.Platform.Audio.Linux;

namespace HyperCodecs.Platform.Audio;

/// <summary>Makes the PulseAudio sound output, which PipeWire also provides.</summary>
public static class LinuxAudioOutputs
{
    /// <summary>Makes the output.</summary>
    /// <returns>The output, or <see langword="null"/> when the system is not Linux.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static IStreamingAudioOutput? Create() => OperatingSystem.IsLinux() ? new PulseStreamingAudioOutput() : null;
}
