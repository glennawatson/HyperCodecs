// Copyright (c) 2026 Glenn Watson. All rights reserved.
// Glenn Watson licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace HyperCodecs.H265;

/// <summary>The distances in a plane's samples, row after row, from a sample to the two neighbours an edge offset compares it with.</summary>
/// <param name="First">The distance to the first neighbour.</param>
/// <param name="Second">The distance to the second neighbour.</param>
internal readonly record struct H265SaoSteps(int First, int Second);
