// Copyright (c) 2026 Glenn Watson. All rights reserved.
// Glenn Watson licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace HyperCodecs.H264;

/// <summary>Which edges of a macroblock need a strength.</summary>
/// <param name="Edges">How many edges to work out, counting from the left or the top.</param>
/// <param name="Step">The step between edges that have a strength: 1 for every edge, 2 with the 8x8 transform.</param>
internal readonly record struct H264EdgeShape(int Edges, int Step);
