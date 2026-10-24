// Copyright (c) 2026 Glenn Watson. All rights reserved.
// Glenn Watson licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace HyperCodecs.H265;

/// <summary>A decoded picture: its samples, and what the decoder keeps about every 4x4 block for the neighbours and later pictures that need it.</summary>
[DebuggerDisplay("H265Picture: POC {Poc} {Width}x{Height}")]
internal sealed class H265Picture
{
    /// <summary>The side of the grid the block information is kept on.</summary>
    internal const int GridLog2 = 2;

    /// <summary>The block kind of a block that was not decoded.</summary>
    internal const byte NotDecoded = 0;

    /// <summary>The block kind of an intra predicted block.</summary>
    internal const byte Intra = 1;

    /// <summary>The block kind of an inter predicted block.</summary>
    internal const byte Inter = 2;

    /// <summary>The bit of a block's flags that says the block was coded with transform and quantisation bypass.</summary>
    internal const byte BypassFlag = 1;

    /// <summary>The bit of a block's flags that says the block holds pulse code modulation samples.</summary>
    internal const byte PcmFlag = 2;

    /// <summary>The bit of a block's flags that says the block was skipped.</summary>
    internal const byte SkipFlag = 4;

    /// <summary>The bit of a block's flags that says the transform block holding it has luma coefficients.</summary>
    internal const byte CodedFlag = 128;

    /// <summary>The bit of a block's flags that says a transform block edge lies on its left side.</summary>
    internal const byte TransformEdgeLeft = 8;

    /// <summary>The bit of a block's flags that says a transform block edge lies on its top side.</summary>
    internal const byte TransformEdgeTop = 16;

    /// <summary>The bit of a block's flags that says a prediction block edge lies on its left side.</summary>
    internal const byte PredictionEdgeLeft = 32;

    /// <summary>The bit of a block's flags that says a prediction block edge lies on its top side.</summary>
    internal const byte PredictionEdgeTop = 64;

    /// <summary>The reference state of a picture that is not used for reference.</summary>
    internal const int Unused = 0;

    /// <summary>The reference state of a picture used for short-term reference.</summary>
    internal const int ShortTerm = 1;

    /// <summary>The reference state of a picture used for long-term reference.</summary>
    internal const int LongTerm = 2;

    /// <summary>The state of a picture whose coding tree blocks are still being decoded.</summary>
    private const int Running = 0;

    /// <summary>The state of a picture whose decoding and filtering has finished.</summary>
    private const int Finished = 1;

    /// <summary>The state of a picture whose decoding has failed.</summary>
    private const int Aborted = 2;

    /// <summary>The colour components.</summary>
    private const int Components = 3;

    /// <summary>Wakes the threads decoding later pictures that wait for this one.</summary>
    private readonly ProgressGate _gate = new();

    /// <summary>Whether the picture is still being decoded, has been decoded, or failed.</summary>
    private int _decoding;

    /// <summary>The number of luma lines from the top that are final.</summary>
    private int _finalLines;

    /// <summary>The number of decoding jobs that use the picture, as the one decoding into it or as a reference.</summary>
    private int _users;

    /// <summary>Initializes a new instance of the <see cref="H265Picture"/> class for a picture whose samples live in a hardware decoder, so nothing is kept here but its identity.</summary>
    /// <param name="sps">The sequence parameter set the picture uses.</param>
    internal H265Picture(H265Sps sps)
    {
        Width = sps.Width;
        Height = sps.Height;
        Luma = [];
        Cb = [];
        Cr = [];
        Kind = [];
        Flags = [];
        IntraMode = [];
        CtDepth = [];
        QpY = [];
        Motion = [];
        SliceOfCtb = [];
        Sao = [];
        HeaderOfCtb = [];
        RefsIndexOfCtb = [];
        SliceRefs = [];
        RowsDecoded = [];
        SaoCopied = [];
    }

    /// <summary>Initializes a new instance of the <see cref="H265Picture"/> class.</summary>
    /// <param name="sps">The sequence parameter set the picture uses.</param>
    /// <param name="layout">The layout of the picture.</param>
    internal H265Picture(H265Sps sps, H265PictureLayout layout)
    {
        Width = sps.Width;
        Height = sps.Height;
        ChromaWidth = sps.ChromaFormat == ChromaSampling.Monochrome ? 0 : Width / sps.SubWidthC;
        ChromaHeight = sps.ChromaFormat == ChromaSampling.Monochrome ? 0 : Height / sps.SubHeightC;
        Luma = new ushort[Width * Height];
        Cb = new ushort[ChromaWidth * ChromaHeight];
        Cr = new ushort[ChromaWidth * ChromaHeight];
        GridWidth = (Width + (1 << GridLog2) - 1) >> GridLog2;
        GridHeight = (Height + (1 << GridLog2) - 1) >> GridLog2;
        var grid = GridWidth * GridHeight;
        Kind = new byte[grid];
        Flags = new byte[grid];
        IntraMode = new byte[grid];
        CtDepth = new byte[grid];
        QpY = new sbyte[grid];
        Motion = new H265PuMotion[grid];
        SliceOfCtb = new int[layout.CtbCount];
        Sao = new int[layout.CtbCount * H265SaoParams.IntsPerCtb];
        HeaderOfCtb = new H265SliceHeader?[layout.CtbCount];
        RefsIndexOfCtb = new int[layout.CtbCount];
        Array.Fill(SliceOfCtb, -1);
        Array.Fill(Motion, H265PuMotion.None);
        SliceRefs = [];
        RowsDecoded = new int[layout.HeightInCtbs];
        CtbColumns = layout.WidthInCtbs;
        SaoCopied = new int[Components];
    }

    /// <summary>Gets the luma width in samples.</summary>
    internal int Width { get; }

    /// <summary>Gets the luma height in samples.</summary>
    internal int Height { get; }

    /// <summary>Gets the chroma width in samples, or 0 for monochrome video.</summary>
    internal int ChromaWidth { get; }

    /// <summary>Gets the chroma height in samples, or 0 for monochrome video.</summary>
    internal int ChromaHeight { get; }

    /// <summary>Gets the 4x4 blocks in a row.</summary>
    internal int GridWidth { get; }

    /// <summary>Gets the rows of 4x4 blocks.</summary>
    internal int GridHeight { get; }

    /// <summary>Gets the luma samples, row after row.</summary>
    internal ushort[] Luma { get; }

    /// <summary>Gets the Cb samples, row after row.</summary>
    internal ushort[] Cb { get; }

    /// <summary>Gets the Cr samples, row after row.</summary>
    internal ushort[] Cr { get; }

    /// <summary>Gets how each 4x4 block was predicted: not decoded, intra or inter.</summary>
    internal byte[] Kind { get; }

    /// <summary>Gets the flags of each 4x4 block.</summary>
    internal byte[] Flags { get; }

    /// <summary>Gets the luma intra prediction mode of each 4x4 block.</summary>
    internal byte[] IntraMode { get; }

    /// <summary>Gets the coding quadtree depth of each 4x4 block.</summary>
    internal byte[] CtDepth { get; }

    /// <summary>Gets the luma quantiser of each 4x4 block.</summary>
    internal sbyte[] QpY { get; }

    /// <summary>Gets the motion of each 4x4 block.</summary>
    internal H265PuMotion[] Motion { get; }

    /// <summary>Gets the slice address of each coding tree block, or -1 for a block not decoded.</summary>
    internal int[] SliceOfCtb { get; }

    /// <summary>Gets the header of the slice segment each coding tree block belongs to, or <see langword="null"/> for a block not decoded.</summary>
    internal H265SliceHeader?[] HeaderOfCtb { get; }

    /// <summary>Gets the index in <see cref="SliceRefs"/> of the slice each coding tree block belongs to.</summary>
    internal int[] RefsIndexOfCtb { get; }

    /// <summary>Gets the sample adaptive offset parameters of each coding tree block, in the layout of <see cref="H265SaoParams"/>.</summary>
    internal int[] Sao { get; }

    /// <summary>Gets the reference picture lists of each slice of the picture, which a later picture needs to follow the motion. The first <see cref="SliceRefCount"/> are in use.</summary>
    internal List<H265SliceRefs> SliceRefs { get; }

    /// <summary>Gets the number of entries of <see cref="SliceRefs"/> in use.</summary>
    internal int SliceRefCount { get; private set; }

    /// <summary>Gets how many coding tree blocks of each row have been decoded, which the filtering and the pictures that refer to this one follow.</summary>
    internal int[] RowsDecoded { get; }

    /// <summary>Gets the coding tree blocks in a row.</summary>
    internal int CtbColumns { get; }

    /// <summary>Gets or sets the rows of coding tree blocks whose edges the deblocking filter has been run on. Only the thread decoding the picture uses this.</summary>
    internal int DeblockedRows { get; set; }

    /// <summary>Gets or sets the rows of coding tree blocks that sample adaptive offset has been applied to. Only the thread decoding the picture uses this.</summary>
    internal int SaoRows { get; set; }

    /// <summary>Gets the lines of each colour component whose deblocked samples have been copied for sample adaptive offset to read. Only the thread decoding the picture reads this.</summary>
    internal int[] SaoCopied { get; }

    /// <summary>Gets or sets a value indicating whether the reference picture set being derived keeps the picture.</summary>
    internal bool IsKept { get; set; }

    /// <summary>Gets or sets when the picture is shown, in seconds.</summary>
    internal double Time { get; set; }

    /// <summary>Gets or sets the surface a hardware decoder holds this picture in.</summary>
    internal uint Surface { get; set; }

    /// <summary>Gets or sets the frame the picture is copied into, or that a hardware decoder produced for it, until the picture is output.</summary>
    internal VideoFrame? OutputFrame { get; set; }

    /// <summary>Gets or sets the picture order count.</summary>
    internal int Poc { get; set; }

    /// <summary>Gets or sets how the picture is kept for reference: 0 not at all, 1 as a short-term picture, 2 as a long-term picture.</summary>
    internal int ReferenceState { get; set; }

    /// <summary>Gets a value indicating whether the picture is used for long-term reference.</summary>
    internal bool IsLongTerm => ReferenceState == LongTerm;

    /// <summary>Gets or sets a value indicating whether the picture still has to be output.</summary>
    internal bool NeededForOutput { get; set; }

    /// <summary>Gets or sets how many pictures have been decoded since this one waited for output.</summary>
    internal int LatencyCount { get; set; }

    /// <summary>Gets or sets a value indicating whether a picture is only a stand-in made for a missing reference.</summary>
    internal bool IsGenerated { get; set; }

    /// <summary>Gets or sets the number of the sequence parameter set the picture was decoded with.</summary>
    internal H265Sps? Sps { get; set; }

    /// <summary>Finds whether the picture has the sizes a picture of a sequence and layout needs.</summary>
    /// <param name="sps">The sequence parameter set.</param>
    /// <param name="layout">The layout of the picture.</param>
    /// <returns><see langword="true"/> when the picture can be recycled for it.</returns>
    internal bool Fits(H265Sps sps, H265PictureLayout layout) =>
        Width == sps.Width
        && Height == sps.Height
        && ChromaWidth == (sps.ChromaFormat == ChromaSampling.Monochrome ? 0 : sps.Width / sps.SubWidthC)
        && ChromaHeight == (sps.ChromaFormat == ChromaSampling.Monochrome ? 0 : sps.Height / sps.SubHeightC)
        && SliceOfCtb.Length == layout.CtbCount;

    /// <summary>
    /// Clears what decoding the picture before left in its arrays, so they hold what a new picture's do. This is the larger part of getting a picture ready
    /// to be decoded into again, and the thread that decodes the picture does it, so the thread that reads the packets does not have to.
    /// </summary>
    internal void ClearData()
    {
        // Decoding writes every sample of a coding tree block, so the planes of a picture whose blocks were all decoded hold nothing that needs clearing.
        if (Array.IndexOf(SliceOfCtb, -1) >= 0)
        {
            Array.Clear(Luma);
            Array.Clear(Cb);
            Array.Clear(Cr);
        }

        Array.Clear(Kind);
        Array.Clear(Flags);
        Array.Clear(IntraMode);
        Array.Clear(CtDepth);
        Array.Clear(QpY);
        Array.Clear(Sao);
        Array.Clear(HeaderOfCtb);
        Array.Clear(RefsIndexOfCtb);
        Array.Fill(SliceOfCtb, -1);
        Array.Fill(Motion, H265PuMotion.None);
    }

    /// <summary>Returns the picture's state to the state a new one has, so a picture nothing refers to any more can be decoded into again. <see cref="ClearData"/> does the rest.</summary>
    internal void Recycle()
    {
        SliceRefCount = 0;
        Array.Clear(RowsDecoded);
        Array.Clear(SaoCopied);
        DeblockedRows = 0;
        SaoRows = 0;
        Volatile.Write(ref _finalLines, 0);
        Volatile.Write(ref _decoding, Running);
        Time = 0;
        Poc = 0;
        ReferenceState = 0;
        NeededForOutput = false;
        LatencyCount = 0;
        IsGenerated = false;
        Sps = null;
    }

    /// <summary>Records that a coding tree block has been decoded. Only the thread decoding the picture calls this.</summary>
    /// <param name="rs">The raster scan address of the block.</param>
    /// <returns><see langword="true"/> when the block was the last of its row to be decoded.</returns>
    internal bool CtbDecoded(int rs)
    {
        ref var count = ref RowsDecoded[rs / CtbColumns];
        var next = count + 1;
        Volatile.Write(ref count, next);
        if (next != CtbColumns)
        {
            return false;
        }

        _gate.Pulse();
        return true;
    }

    /// <summary>Tells whether the deblocking filter can run on a row: the row and the one below it are decoded, and the one below holds the last reads of the row's samples.</summary>
    /// <param name="row">The row of coding tree blocks.</param>
    /// <returns><see langword="true"/> when it can.</returns>
    internal bool CanDeblock(int row) => RowsDecoded[row] >= CtbColumns && RowsDecoded[Math.Min(row + 1, RowsDecoded.Length - 1)] >= CtbColumns;

    /// <summary>Says how many lines of luma, from the top, are final. Only the thread decoding the picture calls this.</summary>
    /// <param name="lines">The number of lines.</param>
    internal void PublishLines(int lines)
    {
        Volatile.Write(ref _finalLines, lines);
        _gate.Pulse();
    }

    /// <summary>Says that the picture is decoded and filtered, so every line of it is final.</summary>
    internal void FinishDecoding()
    {
        Volatile.Write(ref _finalLines, Height);
        Volatile.Write(ref _decoding, Finished);
        _gate.Pulse();
    }

    /// <summary>Says that decoding has failed, so pictures that wait for this one should stop.</summary>
    internal void AbortDecoding()
    {
        Volatile.Write(ref _decoding, Aborted);
        _gate.Pulse();
    }

    /// <summary>Notes that a decoding job uses this picture, so it is not decoded into again until the job is done with it.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void Acquire() => Interlocked.Increment(ref _users);

    /// <summary>Notes that a decoding job is done with this picture.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void Release() => Interlocked.Decrement(ref _users);

    /// <summary>Tells whether no decoding job uses the picture.</summary>
    /// <returns><see langword="true"/> when it can be decoded into again.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal bool IsIdle() => Volatile.Read(ref _users) == 0;

    /// <summary>Waits until a line of luma is final, so it can be read as a reference.</summary>
    /// <param name="line">The line, from 0 to one less than <see cref="Height"/>.</param>
    /// <param name="tokens">The caller's tokens, to give up while it sleeps, or <see langword="null"/>.</param>
    /// <exception cref="InvalidDataException">The picture failed to decode.</exception>
    internal void WaitForLines(int line, WorkTokens? tokens)
    {
        if (line < Volatile.Read(ref _finalLines))
        {
            return;
        }

        _gate.Wait(new LinesFinal(this, line), tokens);
    }

    /// <summary>Waits until a row of coding tree blocks is decoded, so its motion can be read for temporal prediction.</summary>
    /// <param name="row">The row.</param>
    /// <param name="tokens">The caller's tokens, to give up while it sleeps, or <see langword="null"/>.</param>
    /// <exception cref="InvalidDataException">The picture failed to decode.</exception>
    internal void WaitForDecodedRow(int row, WorkTokens? tokens)
    {
        if (RowsDecoded.Length == 0 || Volatile.Read(ref RowsDecoded[row]) >= CtbColumns)
        {
            return;
        }

        _gate.Wait(new RowDecoded(this, row), tokens);
    }

    /// <summary>Takes the next entry of <see cref="SliceRefs"/> for a new slice, adding one only when the picture has not held this many slices before.</summary>
    /// <returns>The entry, which still holds the lists of an earlier use.</returns>
    internal H265SliceRefs NextSliceRefs()
    {
        if (SliceRefCount == SliceRefs.Count)
        {
            SliceRefs.Add(new());
        }

        var refs = SliceRefs[SliceRefCount];
        SliceRefCount++;
        return refs;
    }

    /// <summary>Gets the offset in the 4x4 grid of a luma position.</summary>
    /// <param name="x">The column in samples.</param>
    /// <param name="y">The row in samples.</param>
    /// <returns>The index.</returns>
    internal int GridIndex(int x, int y) => ((y >> GridLog2) * GridWidth) + (x >> GridLog2);

    /// <summary>Gets the samples of a colour component.</summary>
    /// <param name="component">0 for luma, 1 for Cb, 2 for Cr.</param>
    /// <returns>The plane.</returns>
    internal ushort[] PlaneOf(int component)
    {
        if (component == 0)
        {
            return Luma;
        }

        return component == 1 ? Cb : Cr;
    }

    /// <summary>Gets the samples in a row of a colour component.</summary>
    /// <param name="component">0 for luma, otherwise a chroma component.</param>
    /// <returns>The stride.</returns>
    internal int StrideOf(int component) => component == 0 ? Width : ChromaWidth;

    /// <summary>Throws when the picture failed to decode, so a picture that waits for it does not wait for ever.</summary>
    /// <exception cref="InvalidDataException">The picture failed to decode.</exception>
    private void ThrowIfAborted()
    {
        if (Volatile.Read(ref _decoding) == Aborted)
        {
            throw new InvalidDataException("An H.265 picture that a later picture refers to failed to decode.");
        }
    }

    /// <summary>Waits for a line of luma to be final.</summary>
    /// <param name="Picture">The picture.</param>
    /// <param name="Line">The line.</param>
    private readonly record struct LinesFinal(H265Picture Picture, int Line) : IProgressCondition
    {
        /// <inheritdoc/>
        public bool IsMet()
        {
            if (Line < Volatile.Read(ref Picture._finalLines))
            {
                return true;
            }

            Picture.ThrowIfAborted();
            return false;
        }
    }

    /// <summary>Waits for a row of coding tree blocks to be decoded.</summary>
    /// <param name="Picture">The picture.</param>
    /// <param name="Row">The row.</param>
    private readonly record struct RowDecoded(H265Picture Picture, int Row) : IProgressCondition
    {
        /// <inheritdoc/>
        public bool IsMet()
        {
            if (Volatile.Read(ref Picture.RowsDecoded[Row]) >= Picture.CtbColumns || Volatile.Read(ref Picture._decoding) == Finished)
            {
                return true;
            }

            Picture.ThrowIfAborted();
            return false;
        }
    }
}
