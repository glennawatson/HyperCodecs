// Copyright (c) 2026 Glenn Watson. All rights reserved.
// Glenn Watson licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Runtime.CompilerServices;
using HyperCodecs.Platform.Audio.Linux;

namespace HyperCodecs.Platform.Audio;

/// <summary>Makes the PulseAudio sound output, which PipeWire also provides. The plain .NET build of the package carries this, because Linux has no platform target framework.</summary>
internal static class SystemAudioOutputs
{
    /// <summary>Makes the output.</summary>
    /// <returns>The output, or <see langword="null"/> when the system is not Linux.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static IStreamingAudioOutput? Create() => OperatingSystem.IsLinux() ? new PulseStreamingAudioOutput() : null;
}
