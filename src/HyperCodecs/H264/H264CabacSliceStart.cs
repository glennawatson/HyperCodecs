// Copyright (c) 2026 Glenn Watson. All rights reserved.
// Glenn Watson licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace HyperCodecs.H264;

/// <summary>What the context states of a CABAC slice are initialised from.</summary>
/// <param name="Contexts">The state of each context.</param>
/// <param name="IsIntra">Whether the slice is an I slice.</param>
/// <param name="InitIdc">The slice's cabac_init_idc.</param>
/// <param name="Qp">The quantiser the slice starts from.</param>
internal readonly record struct H264CabacSliceStart(byte[] Contexts, bool IsIntra, int InitIdc, int Qp);
