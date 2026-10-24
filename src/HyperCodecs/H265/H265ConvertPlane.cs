// Copyright (c) 2026 Glenn Watson. All rights reserved.
// Glenn Watson licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace HyperCodecs.H265;

/// <summary>One plane of a picture and where its 8 bit copy goes.</summary>
/// <param name="Plane">The source plane.</param>
/// <param name="Stride">The samples in a row of the source.</param>
/// <param name="Area">The area to copy, in source samples (the width and height are those of the output).</param>
/// <param name="StepX">The source columns between output columns.</param>
/// <param name="StepY">The source rows between output rows.</param>
/// <param name="Target">The output plane, row after row with no padding.</param>
/// <param name="BitDepth">The bit depth of the source.</param>
internal readonly record struct H265ConvertPlane(ushort[] Plane, int Stride, BlockRect Area, int StepX, int StepY, byte[] Target, int BitDepth);
