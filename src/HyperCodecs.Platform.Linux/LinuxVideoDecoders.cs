// Copyright (c) 2026 Glenn Watson. All rights reserved.
// Glenn Watson licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using HyperCodecs.Platform.Video.VaApi;

namespace HyperCodecs.Platform.Video;

/// <summary>Makes the graphics hardware's decoder through VA-API.</summary>
public static class LinuxVideoDecoders
{
    /// <summary>Makes a decoder: the hardware's when the picture is large enough to pay for it, otherwise the managed one.</summary>
    /// <param name="track">The picture track.</param>
    /// <returns>The decoder, or <see langword="null"/> when no decoder reads the track's coding.</returns>
    public static VideoDecoder? Create(MediaTrack track)
    {
        ArgumentNullException.ThrowIfNull(track);
        return PlatformVideoDecoders.IsLargeEnough(track) ? TryCreate(track, out _) ?? VideoDecoders.Create(track) : VideoDecoders.Create(track) ?? TryCreate(track, out _);
    }

    /// <summary>Makes the hardware decoder for a track.</summary>
    /// <param name="track">The picture track.</param>
    /// <param name="reason">Receives why there is no decoder, when there is none.</param>
    /// <returns>The decoder, or <see langword="null"/>.</returns>
    public static VideoDecoder? TryCreate(MediaTrack track, out string? reason)
    {
        ArgumentNullException.ThrowIfNull(track);
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
