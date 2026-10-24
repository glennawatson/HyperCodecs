// Copyright (c) 2026 Glenn Watson. All rights reserved.
// Glenn Watson licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace HyperCodecs.H264;

/// <summary>What the boundary strength of the edges of a macroblock needs to know about one macroblock.</summary>
/// <param name="Address">The macroblock.</param>
/// <param name="Coded">One bit for each 4x4 block, row after row, set when its transform block has non-zero coefficients; -1 for a macroblock that is not inter coded.</param>
/// <param name="Uniform">Whether every block of the macroblock moves the same way.</param>
internal readonly record struct H264EdgeBlocks(int Address, int Coded, bool Uniform);
