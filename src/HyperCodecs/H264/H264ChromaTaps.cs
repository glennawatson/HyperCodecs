// Copyright (c) 2026 Glenn Watson. All rights reserved.
// Glenn Watson licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace HyperCodecs.H264;

/// <summary>The weights of the four reference samples that a chroma sample is blended from (clause 8.4.2.2.2); they add up to 64.</summary>
/// <param name="TopLeft">The weight of the sample above and to the left.</param>
/// <param name="TopRight">The weight of the sample above and to the right.</param>
/// <param name="BottomLeft">The weight of the sample below and to the left.</param>
/// <param name="BottomRight">The weight of the sample below and to the right.</param>
internal readonly record struct H264ChromaTaps(int TopLeft, int TopRight, int BottomLeft, int BottomRight);
