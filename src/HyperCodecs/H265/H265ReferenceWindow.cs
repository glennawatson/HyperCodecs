// Copyright (c) 2026 Glenn Watson. All rights reserved.
// Glenn Watson licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace HyperCodecs.H265;

/// <summary>The reference samples of a block and the margins an interpolation filter reads around it.</summary>
/// <param name="Memory">The samples, starting at the first sample of the margin above and to the left of the block.</param>
/// <param name="Stride">The samples in a row.</param>
internal readonly record struct H265ReferenceWindow(ReadOnlyMemory<ushort> Memory, int Stride)
{
    /// <summary>Gets the samples.</summary>
    internal ReadOnlySpan<ushort> Samples => Memory.Span;
}
