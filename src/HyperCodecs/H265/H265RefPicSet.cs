// Copyright (c) 2026 Glenn Watson. All rights reserved.
// Glenn Watson licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace HyperCodecs.H265;

/// <summary>
/// The reference pictures the current picture can use, in the three groups reference lists are built from (8.3.2). The decoder
/// keeps one and fills it again for each picture, so deriving the set allocates nothing once the lists have grown.
/// </summary>
internal sealed class H265RefPicSet
{
    /// <summary>Gets the short-term pictures before the current one in output order, nearest first.</summary>
    internal List<H265Picture> StCurrBefore { get; } = [];

    /// <summary>Gets the short-term pictures after the current one in output order, nearest first.</summary>
    internal List<H265Picture> StCurrAfter { get; } = [];

    /// <summary>Gets the long-term pictures.</summary>
    internal List<H265Picture> LtCurr { get; } = [];

    /// <summary>Empties the three groups.</summary>
    internal void Clear()
    {
        StCurrBefore.Clear();
        StCurrAfter.Clear();
        LtCurr.Clear();
    }
}
