// Copyright (c) 2026 Glenn Watson. All rights reserved.
// Glenn Watson licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace HyperCodecs.H265;

/// <summary>Where the two neighbours an edge offset compares a sample with lie, as column and row distances.</summary>
/// <param name="Horizontal0">The column distance of the first neighbour.</param>
/// <param name="Vertical0">The row distance of the first neighbour.</param>
/// <param name="Horizontal1">The column distance of the second neighbour.</param>
/// <param name="Vertical1">The row distance of the second neighbour.</param>
internal readonly record struct H265SaoNeighbours(int Horizontal0, int Vertical0, int Horizontal1, int Vertical1);
