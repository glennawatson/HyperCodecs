// Copyright (c) 2026 Glenn Watson. All rights reserved.
// Glenn Watson licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Diagnostics;

namespace HyperCodecs.H265;

/// <summary>What the hardware needs to decode a picture. The decoder fills one instance again for each picture.</summary>
[DebuggerDisplay("H265HardwarePicture: POC {Picture.Poc}, {Slices.Count} slices")]
internal sealed class H265HardwarePicture
{
    /// <summary>Gets the picture being decoded.</summary>
    internal H265Picture Picture { get; private set; } = null!;

    /// <summary>Gets the sequence parameter set.</summary>
    internal H265Sps Sps { get; private set; } = null!;

    /// <summary>Gets the picture parameter set.</summary>
    internal H265Pps Pps { get; private set; } = null!;

    /// <summary>Gets the reference picture set of the picture.</summary>
    internal H265RefPicSet References { get; private set; } = null!;

    /// <summary>Gets the pictures in the decoded picture buffer.</summary>
    internal List<H265Picture> Buffer { get; private set; } = null!;

    /// <summary>Gets the slice segments of the picture.</summary>
    internal List<H265HardwareSlice> Slices { get; private set; } = null!;

    /// <summary>Gets the NAL units of the segments, one after the other.</summary>
    internal ReadOnlyMemory<byte> Bytes { get; private set; }

    /// <summary>Fills the instance for a picture.</summary>
    /// <param name="picture">The picture being decoded.</param>
    /// <param name="sps">The sequence parameter set.</param>
    /// <param name="pps">The picture parameter set.</param>
    /// <param name="references">The reference picture set of the picture.</param>
    /// <param name="buffer">The pictures in the decoded picture buffer.</param>
    /// <param name="slices">The slice segments of the picture.</param>
    /// <param name="bytes">The NAL units of the segments, one after the other.</param>
    internal void Fill(H265Picture picture, H265Sps sps, H265Pps pps, H265RefPicSet references, List<H265Picture> buffer, List<H265HardwareSlice> slices, ReadOnlyMemory<byte> bytes)
    {
        Picture = picture;
        Sps = sps;
        Pps = pps;
        References = references;
        Buffer = buffer;
        Slices = slices;
        Bytes = bytes;
    }
}
