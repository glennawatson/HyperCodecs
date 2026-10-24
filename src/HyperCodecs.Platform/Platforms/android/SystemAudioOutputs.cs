// Copyright (c) 2026 Glenn Watson. All rights reserved.
// Glenn Watson licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Runtime.CompilerServices;
using HyperCodecs.Platform.Audio.AndroidMedia;

namespace HyperCodecs.Platform.Audio;

/// <summary>Makes the AudioTrack sound output.</summary>
internal static class SystemAudioOutputs
{
    /// <summary>Makes the output.</summary>
    /// <returns>The output.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static IStreamingAudioOutput? Create() => new AudioTrackStreamingOutput();
}
