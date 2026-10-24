// Copyright (c) 2026 Glenn Watson. All rights reserved.
// Glenn Watson licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace HyperCodecs.H265;

/// <summary>The state of decoding one slice segment: the parameter sets in use, the CABAC state, the quantiser state and the work areas.</summary>
[DebuggerDisplay("H265SliceContext: segment at {Header.Segment.Address}")]
internal sealed class H265SliceContext
{
    /// <summary>The largest transform block area.</summary>
    private const int MaxBlockArea = 32 * 32;

    /// <summary>The Rice parameter states kept for persistent adaptation.</summary>
    private const int StatCoeffCount = 4;

    /// <summary>Initializes a new instance of the <see cref="H265SliceContext"/> class. The decoder makes one and starts it again for each picture.</summary>
    /// <param name="prediction">The work areas of inter prediction, which the decoder keeps from picture to picture.</param>
    internal H265SliceContext(H265PredictionBuffers prediction)
    {
        SaoSources = [[], [], []];
        Sps = null!;
        Pps = null!;
        Layout = null!;
        Picture = null!;
        Header = null!;
        Contexts = new byte[H265CabacContexts.Count];
        StatCoeff = new int[StatCoeffCount];
        WppContexts = new byte[H265CabacContexts.Count];
        WppStatCoeff = new int[StatCoeffCount];
        DependentContexts = new byte[H265CabacContexts.Count];
        DependentStatCoeff = new int[StatCoeffCount];
        Coefficients = new int[MaxBlockArea];
        Scratch = new int[MaxBlockArea];
        Prediction = prediction;
        ChromaModes = new int[4];
    }

    /// <summary>Initializes a new instance of the <see cref="H265SliceContext"/> class for a worker thread.</summary>
    /// <param name="prediction">The work areas of inter prediction.</param>
    /// <param name="tokens">The tokens the thread gives up while it waits for another picture.</param>
    internal H265SliceContext(H265PredictionBuffers prediction, WorkTokens tokens)
        : this(prediction) => Tokens = tokens;

    /// <summary>Gets the tokens the thread gives up while it waits for another picture, or <see langword="null"/> when it holds none.</summary>
    internal WorkTokens? Tokens { get; }

    /// <summary>Gets the work areas that hold a copy of each plane while sample adaptive offset reads it, which the context keeps from picture to picture.</summary>
    internal ushort[][] SaoSources { get; }

    /// <summary>Gets the sequence parameter set.</summary>
    internal H265Sps Sps { get; private set; }

    /// <summary>Gets the picture parameter set.</summary>
    internal H265Pps Pps { get; private set; }

    /// <summary>Gets the picture layout.</summary>
    internal H265PictureLayout Layout { get; private set; }

    /// <summary>Gets the picture being decoded.</summary>
    internal H265Picture Picture { get; private set; }

    /// <summary>Gets or sets the header of the slice segment being decoded.</summary>
    internal H265SliceHeader Header { get; set; }

    /// <summary>Gets or sets the raster scan address of the first coding tree block of the slice this segment belongs to.</summary>
    internal int SliceAddrRs { get; set; }

    /// <summary>Gets or sets the index of the slice in the picture, which <see cref="H265Picture.SliceRefs"/> is indexed by.</summary>
    internal int SliceIndex { get; set; }

    /// <summary>Gets or sets a value indicating whether no reference picture of the slice follows the current picture in output order.</summary>
    internal bool NoBackwardPred { get; set; }

    /// <summary>Gets the pictures of reference list 0.</summary>
    internal List<H265Picture> RefList0 { get; } = [];

    /// <summary>Gets the pictures of reference list 1.</summary>
    internal List<H265Picture> RefList1 { get; } = [];

    /// <summary>Gets a work area that holds the initial reference list before it is reordered.</summary>
    internal List<H265Picture> RefInitial { get; } = [];

    /// <summary>Gets whether each picture of reference list 0 was a long-term picture when the slice was read; the marking of the pictures can change before the slice is decoded.</summary>
    internal bool[] RefLong0 { get; } = new bool[H265SliceRefs.MaxReferences];

    /// <summary>Gets whether each picture of reference list 1 was a long-term picture when the slice was read.</summary>
    internal bool[] RefLong1 { get; } = new bool[H265SliceRefs.MaxReferences];

    /// <summary>Gets the context variables.</summary>
    internal byte[] Contexts { get; }

    /// <summary>Gets the Rice parameter states.</summary>
    internal int[] StatCoeff { get; }

    /// <summary>Gets the context variables saved after the second coding tree block of a row, for wavefront synchronisation.</summary>
    internal byte[] WppContexts { get; }

    /// <summary>Gets the Rice parameter states saved with <see cref="WppContexts"/>.</summary>
    internal int[] WppStatCoeff { get; }

    /// <summary>Gets the context variables saved at the end of a slice segment, for a dependent segment to continue from.</summary>
    internal byte[] DependentContexts { get; }

    /// <summary>Gets the Rice parameter states saved with <see cref="DependentContexts"/>.</summary>
    internal int[] DependentStatCoeff { get; }

    /// <summary>Gets the coefficients of the transform block being read, row after row.</summary>
    internal int[] Coefficients { get; }

    /// <summary>Gets a work area the size of the largest transform block.</summary>
    internal int[] Scratch { get; }

    /// <summary>Gets the residual samples of the transform block being reconstructed, row after row.</summary>
    internal int[] ResidualSamples { get; } = new int[MaxBlockArea];

    /// <summary>Gets the state of reading a residual block.</summary>
    internal H265ResidualState Residual { get; } = new();

    /// <summary>Gets the work areas of inter prediction.</summary>
    internal H265PredictionBuffers Prediction { get; }

    /// <summary>Gets the intra chroma prediction mode of each prediction block of the current coding unit.</summary>
    internal int[] ChromaModes { get; }

    /// <summary>Gets or sets the quantiser of the current coding unit.</summary>
    internal int QpY { get; set; }

    /// <summary>Gets or sets the predicted quantiser of the current quantisation group.</summary>
    internal int QpYPred { get; set; }

    /// <summary>Gets or sets the quantiser of the last coding unit decoded, which the next quantisation group predicts from.</summary>
    internal int LastQpY { get; set; }

    /// <summary>Gets or sets a value indicating whether the quantiser change of the current quantisation group was read.</summary>
    internal bool IsCuQpDeltaCoded { get; set; }

    /// <summary>Gets or sets the quantiser change of the current quantisation group.</summary>
    internal int CuQpDeltaVal { get; set; }

    /// <summary>Gets or sets a value indicating whether the chroma quantiser offset of the current group was read.</summary>
    internal bool IsCuChromaQpOffsetCoded { get; set; }

    /// <summary>Gets or sets the Cb quantiser offset of the current coding unit.</summary>
    internal int CuQpOffsetCb { get; set; }

    /// <summary>Gets or sets the Cr quantiser offset of the current coding unit.</summary>
    internal int CuQpOffsetCr { get; set; }

    /// <summary>Gets a value indicating whether the picture has any chroma.</summary>
    internal bool HasChroma => Sps.ChromaArrayType != ChromaSampling.Monochrome;

    /// <summary>Tells whether a picture of a reference list is a long-term picture, as it was when the slice was read.</summary>
    /// <param name="list">The reference list, 0 or 1.</param>
    /// <param name="refIdx">The index in that list.</param>
    /// <returns><see langword="true"/> for a long-term picture.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal bool IsLongTerm(int list, int refIdx) => (list == 0 ? RefLong0 : RefLong1)[refIdx];

    /// <summary>Gets the context ready to decode a slice that was read earlier: its header and its reference lists.</summary>
    /// <param name="slice">The slice.</param>
    internal void LoadSlice(H265SliceJob slice)
    {
        Header = slice.Header;
        SliceIndex = slice.SliceIndex;
        NoBackwardPred = slice.NoBackwardPred;
        RefList0.Clear();
        RefList0.AddRange(slice.List0);
        RefList1.Clear();
        RefList1.AddRange(slice.List1);
        slice.LongTerm0.AsSpan().CopyTo(RefLong0);
        slice.LongTerm1.AsSpan().CopyTo(RefLong1);
    }

    /// <summary>Starts the context on a new picture, clearing what the last picture left.</summary>
    /// <param name="sps">The sequence parameter set.</param>
    /// <param name="pps">The picture parameter set.</param>
    /// <param name="layout">The picture layout.</param>
    /// <param name="picture">The picture being decoded.</param>
    internal void Begin(H265Sps sps, H265Pps pps, H265PictureLayout layout, H265Picture picture)
    {
        for (var component = 0; component < SaoSources.Length; component++)
        {
            var length = picture.PlaneOf(component).Length;
            if (SaoSources[component].Length < length)
            {
                SaoSources[component] = new ushort[length];
            }
        }

        Sps = sps;
        Pps = pps;
        Layout = layout;
        Picture = picture;
        SliceAddrRs = 0;
        SliceIndex = 0;
        NoBackwardPred = false;
        QpY = 0;
        QpYPred = 0;
        LastQpY = 0;
        IsCuQpDeltaCoded = false;
        CuQpDeltaVal = 0;
        IsCuChromaQpOffsetCoded = false;
        CuQpOffsetCb = 0;
        CuQpOffsetCr = 0;
        RefList0.Clear();
        RefList1.Clear();
        RefInitial.Clear();
    }
}
