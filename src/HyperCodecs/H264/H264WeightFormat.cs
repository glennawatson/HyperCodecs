// Copyright (c) 2026 Glenn Watson. All rights reserved.
// Glenn Watson licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace HyperCodecs.H264;

/// <summary>How the weights of a slice are coded.</summary>
/// <param name="LumaDenom">The log2 of the denominator of the luma weights.</param>
/// <param name="ChromaDenom">The log2 of the denominator of the chroma weights.</param>
/// <param name="ChromaArrayType">The type of colour array.</param>
internal readonly record struct H264WeightFormat(int LumaDenom, int ChromaDenom, int ChromaArrayType);
