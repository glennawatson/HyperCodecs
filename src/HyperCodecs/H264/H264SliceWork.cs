// Copyright (c) 2026 Glenn Watson. All rights reserved.
// Glenn Watson licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Diagnostics;

namespace HyperCodecs.H264;

/// <summary>The working memory every slice of a stream uses, kept by the decoder so a slice does not allocate it again.</summary>
[DebuggerDisplay("H264SliceWork")]
internal sealed class H264SliceWork
{
    /// <summary>The samples along a side of a luma macroblock.</summary>
    private const int LumaSide = 16;

    /// <summary>The samples along a side of a chroma macroblock.</summary>
    private const int ChromaSide = 8;

    /// <summary>The number of chroma planes.</summary>
    private const int ChromaPlanes = 2;

    /// <summary>Initializes a new instance of the <see cref="H264SliceWork"/> class for decoding on the thread that reads the packets, which holds no token.</summary>
    internal H264SliceWork()
    {
    }

    /// <summary>Initializes a new instance of the <see cref="H264SliceWork"/> class for a worker thread.</summary>
    /// <param name="tokens">The tokens the thread gives up while it waits for another picture.</param>
    internal H264SliceWork(WorkTokens tokens) => Tokens = tokens;

    /// <summary>Gets the tokens the thread gives up while it waits for another picture, or <see langword="null"/> when it holds none.</summary>
    internal WorkTokens? Tokens { get; }

    /// <summary>Gets the decoder of CABAC intra slices, which the thread keeps from slice to slice.</summary>
    internal H264CabacIntraSliceDecoder CabacIntra => field ??= new();

    /// <summary>Gets the decoder of CABAC P slices.</summary>
    internal H264CabacPSliceDecoder CabacP => field ??= new(this);

    /// <summary>Gets the decoder of CABAC B slices.</summary>
    internal H264CabacBSliceDecoder CabacB => field ??= new(this);

    /// <summary>Gets the decoder of CAVLC intra slices.</summary>
    internal H264IntraSliceDecoder CavlcIntra => field ??= new();

    /// <summary>Gets the decoder of CAVLC P slices.</summary>
    internal H264PSliceDecoder CavlcP => field ??= new(this);

    /// <summary>Gets the decoder of CAVLC B slices.</summary>
    internal H264BSliceDecoder CavlcB => field ??= new(this);

    /// <summary>Gets the macroblock being decoded.</summary>
    internal H264CurrentMb Mb { get; } = new();

    /// <summary>Gets the macroblock the intra decoder inside a CAVLC inter slice decoder works on, which is not the inter decoder's.</summary>
    internal H264CurrentMb IntraMb { get; } = new();

    /// <summary>Gets the CABAC context states.</summary>
    internal byte[] Contexts { get; } = new byte[H264CabacContexts.Count];

    /// <summary>Gets the luma samples predicted from list 1 of a bi-predicted block.</summary>
    internal byte[] Luma1 { get; } = new byte[LumaSide * LumaSide];

    /// <summary>Gets the Cb then Cr samples predicted from list 1 of a bi-predicted block.</summary>
    internal byte[] Chroma1 { get; } = new byte[ChromaPlanes * ChromaSide * ChromaSide];
}
