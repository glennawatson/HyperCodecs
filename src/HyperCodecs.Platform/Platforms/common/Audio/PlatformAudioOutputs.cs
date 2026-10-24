// Copyright (c) 2026 Glenn Watson. All rights reserved.
// Glenn Watson licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Runtime.CompilerServices;

namespace HyperCodecs.Platform.Audio;

/// <summary>Makes the sound output the operating system provides.</summary>
public static class PlatformAudioOutputs
{
    /// <summary>Makes a sound output for this system.</summary>
    /// <returns>The output, or <see langword="null"/> when the build of this package for the running system has none.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static IStreamingAudioOutput? Create() => SystemAudioOutputs.Create();
}
