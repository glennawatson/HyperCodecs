// Copyright (c) 2026 Glenn Watson. All rights reserved.
// Glenn Watson licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace HyperCodecs.H265;

/// <summary>The limits and options of applying an edge offset to a block.</summary>
/// <param name="Maximum">The largest sample value.</param>
/// <param name="Protected">Whether some samples may be exempt because they are lossless.</param>
/// <param name="Vector">Whether the samples inside the block can be filtered eight at a time.</param>
internal readonly record struct H265SaoLimits(int Maximum, bool Protected, bool Vector);
