// Copyright (c) 2026 Glenn Watson. All rights reserved.
// Glenn Watson licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace HyperCodecs;

/// <summary>Something a thread decoding one picture waits for another picture to reach.</summary>
internal interface IProgressCondition
{
    /// <summary>Tells whether the condition holds.</summary>
    /// <returns><see langword="true"/> when it does.</returns>
    bool IsMet();
}
