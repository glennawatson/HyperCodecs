// Copyright (c) 2026 Glenn Watson. All rights reserved.
// Glenn Watson licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using HyperCodecs.Platform.Video.VideoToolbox;

namespace HyperCodecs.Platform.Video;

/// <summary>Makes the VideoToolbox decoder.</summary>
internal static class SystemVideoDecoders
{
    /// <summary>Makes the decoder for a track.</summary>
    /// <param name="track">The picture track.</param>
    /// <param name="reason">Receives why there is no decoder, when there is none.</param>
    /// <returns>The decoder, or <see langword="null"/>.</returns>
    internal static VideoDecoder? TryCreate(MediaTrack track, out string? reason)
    {
        var decoder = VideoToolboxDecoder.TryCreate(track, out var failure);
        reason = decoder is null ? failure.ToString() : null;
        return decoder;
    }
}
