// Copyright (c) 2026 Glenn Watson. All rights reserved.
// Glenn Watson licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using HyperCodecs.Platform.Video.VaApi;

namespace HyperCodecs.Platform.Video;

/// <summary>Makes the graphics hardware's decoder through VA-API. The plain .NET build of the package carries this, because Linux has no platform target framework.</summary>
internal static class SystemVideoDecoders
{
    /// <summary>Makes the decoder for a track.</summary>
    /// <param name="track">The picture track.</param>
    /// <param name="reason">Receives why there is no decoder, when there is none.</param>
    /// <returns>The decoder, or <see langword="null"/>.</returns>
    internal static VideoDecoder? TryCreate(MediaTrack track, out string? reason)
    {
        reason = "VA-API is only used on Linux.";
        if (!OperatingSystem.IsLinux())
        {
            return null;
        }

        DecoderFailure failure;
        VideoDecoder? decoder = track.Codec == MediaCodec.H265 ? VaApiH265Decoder.TryCreate(track, out failure) : VaApiH264Decoder.TryCreate(track, out failure);
        reason = decoder is null ? failure.ToString() : null;
        return decoder;
    }
}
