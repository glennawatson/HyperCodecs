// Copyright (c) 2026 Glenn Watson. All rights reserved.
// Glenn Watson licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Runtime.CompilerServices;

namespace HyperCodecs.Platform.Audio;

/// <summary>The plain .NET build has no system sound output; Linux has its own package, HyperCodecs.Platform.Linux.</summary>
internal static class SystemAudioOutputs
{
    /// <summary>Makes the output.</summary>
    /// <returns>Always <see langword="null"/>.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static IStreamingAudioOutput? Create() => null;
}
