// Copyright (c) 2026 Glenn Watson. All rights reserved.
// Glenn Watson licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace HyperCodecs.H265;

/// <summary>What a picture is decoded with: its parameter sets and the layout made from them.</summary>
/// <param name="Sps">The sequence parameter set.</param>
/// <param name="Pps">The picture parameter set.</param>
/// <param name="Layout">The layout of the picture.</param>
internal readonly record struct H265PictureSetup(H265Sps Sps, H265Pps Pps, H265PictureLayout Layout);
