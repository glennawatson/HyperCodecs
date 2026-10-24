// Copyright (c) 2026 Glenn Watson. All rights reserved.
// Glenn Watson licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace HyperCodecs.Platform.Video;

/// <summary>The plain .NET build has no system video decoder; Linux has its own package, HyperCodecs.Platform.Linux.</summary>
internal static class SystemVideoDecoders
{
    /// <summary>Makes the decoder for a track.</summary>
    /// <param name="track">The picture track.</param>
    /// <param name="reason">Receives why there is no decoder.</param>
    /// <returns>Always <see langword="null"/>.</returns>
    internal static VideoDecoder? TryCreate(MediaTrack track, out string? reason)
    {
        ArgumentNullException.ThrowIfNull(track);
        reason = "This build has no system video decoder. Use the Windows, Apple or Android target framework, or HyperCodecs.Platform.Linux.";
        return null;
    }
}
