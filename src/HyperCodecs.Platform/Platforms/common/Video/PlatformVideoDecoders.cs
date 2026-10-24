// Copyright (c) 2026 Glenn Watson. All rights reserved.
// Glenn Watson licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Runtime.CompilerServices;

namespace HyperCodecs.Platform.Video;

/// <summary>Makes the decoder for a picture track: the one the operating system provides when it has one for the coding, otherwise the one written in C#.</summary>
public static class PlatformVideoDecoders
{
    /// <summary>The pixels a picture needs before the system's decoder is tried first, because hardware pays about 2 ms for each picture (measured with VA-API).</summary>
    private const int MinimumSystemPixels = 640 * 360;

    /// <summary>Makes a decoder.</summary>
    /// <param name="track">The picture track.</param>
    /// <returns>The decoder, or <see langword="null"/> when no decoder reads the track's coding.</returns>
    public static VideoDecoder? Create(MediaTrack track)
    {
        ArgumentNullException.ThrowIfNull(track);
        return IsLargeEnough(track) ? TryCreateNative(track, out _) ?? VideoDecoders.Create(track) : VideoDecoders.Create(track) ?? TryCreateNative(track, out _);
    }

    /// <summary>Makes the operating system's decoder for a track.</summary>
    /// <param name="track">The picture track.</param>
    /// <param name="reason">Receives why there is no decoder, when there is none.</param>
    /// <returns>The decoder, or <see langword="null"/> when this system has none for the coding.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static VideoDecoder? TryCreateNative(MediaTrack track, out string? reason) => SystemVideoDecoders.TryCreate(track, out reason);

    /// <summary>Finds whether a track's pictures are big enough for the system's decoder to be the first choice.</summary>
    /// <param name="track">The picture track.</param>
    /// <returns><see langword="true"/> when they are.</returns>
    private static bool IsLargeEnough(MediaTrack track) => (long)track.Width * track.Height >= MinimumSystemPixels;
}
