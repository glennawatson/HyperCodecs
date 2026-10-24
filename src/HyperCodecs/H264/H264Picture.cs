// Copyright (c) 2026 Glenn Watson. All rights reserved.
// Glenn Watson licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace HyperCodecs.H264;

/// <summary>A decoded picture: its planes, and what the decoder keeps about each macroblock for the neighbours that follow it.</summary>
[DebuggerDisplay("H264Picture: {MbWidth}x{MbHeight} macroblocks")]
internal sealed class H264Picture
{
    /// <summary>The luma samples in a macroblock row.</summary>
    internal const int LumaSize = 16;

    /// <summary>The chroma samples in a macroblock row of 4:2:0 video.</summary>
    internal const int ChromaSize = 8;

    /// <summary>The coefficient counts kept per macroblock: 16 luma blocks and 4 for each chroma component.</summary>
    internal const int CountsPerMb = 24;

    /// <summary>The first of the chroma counts.</summary>
    internal const int ChromaCounts = 16;

    /// <summary>The 4x4 blocks in a macroblock, each with a motion vector.</summary>
    internal const int BlocksPerMb = 16;

    /// <summary>The 8x8 groups in a macroblock, each with a reference index.</summary>
    internal const int GroupsPerMb = 4;

    /// <summary>The reference index of a block that is intra coded or not decoded.</summary>
    internal const sbyte NoReference = -1;

    /// <summary>The state of a picture whose macroblocks are still being decoded.</summary>
    private const int Running = 0;

    /// <summary>The state of a picture whose decoding has finished.</summary>
    private const int Finished = 1;

    /// <summary>The state of a picture whose decoding has failed.</summary>
    private const int Aborted = 2;

    /// <summary>The lines of luma, counting from the top of the picture, that the filtering will no longer change.</summary>
    private const int FilterReach = 3;

    /// <summary>Wakes the threads decoding later pictures that wait for this one.</summary>
    private readonly ProgressGate _gate = new();

    /// <summary>Whether the picture is still being decoded, has been decoded, or failed.</summary>
    private int _decoding;

    /// <summary>The number of luma lines from the top that are final.</summary>
    private int _finalLines;

    /// <summary>The number of decoding jobs that use the picture, as the one decoding into it or as a reference.</summary>
    private int _users;

    /// <summary>Initializes a new instance of the <see cref="H264Picture"/> class.</summary>
    /// <param name="columns">The width in macroblocks.</param>
    /// <param name="rows">The height in macroblocks.</param>
    internal H264Picture(int columns, int rows)
    {
        MbWidth = columns;
        MbHeight = rows;
        var count = columns * rows;
        Luma = new byte[count * LumaSize * LumaSize];
        Cb = new byte[count * ChromaSize * ChromaSize];
        Cr = new byte[count * ChromaSize * ChromaSize];
        SliceOfMb = new int[count];
        RowsDecoded = new int[rows];
        SliceFilters = new H264SliceFilter[count];
        Kinds = new byte[count];
        IntraModes = new byte[count * LumaSize];
        Counts = new byte[count * CountsPerMb];
        Qp = new sbyte[count];
        Cbp = new byte[count];
        ChromaModes = new byte[count];
        DcFlags = new byte[count];
        Field0 = new(count);
        Field1 = new(count);
        Skipped = new bool[count];
        Transform8x8 = new bool[count];
        DirectOnly = new bool[count];
        DirectGroups = new byte[count];
    }

    /// <summary>Gets the width in macroblocks.</summary>
    internal int MbWidth { get; }

    /// <summary>Gets the height in macroblocks.</summary>
    internal int MbHeight { get; }

    /// <summary>Gets the lines of luma in the picture.</summary>
    internal int LumaLines => MbHeight * LumaSize;

    /// <summary>Gets the number of rows of macroblocks the deblocking filter has been run on. Only the thread decoding the picture reads this.</summary>
    internal int FilteredRows { get; private set; }

    /// <summary>Gets the bytes in a row of the luma plane.</summary>
    internal int LumaStride => MbWidth * LumaSize;

    /// <summary>Gets the bytes in a row of a chroma plane.</summary>
    internal int ChromaStride => MbWidth * ChromaSize;

    /// <summary>Gets the luma plane.</summary>
    internal byte[] Luma { get; }

    /// <summary>Gets the Cb plane.</summary>
    internal byte[] Cb { get; }

    /// <summary>Gets the Cr plane.</summary>
    internal byte[] Cr { get; }

    /// <summary>Gets the number of the slice each macroblock belongs to, or -1 for a macroblock not yet decoded.</summary>
    internal int[] SliceOfMb { get; }

    /// <summary>Gets how many macroblocks of each row have been decoded, which a thread filtering the rows alongside the decoding follows.</summary>
    internal int[] RowsDecoded { get; }

    /// <summary>Gets the kind of each macroblock.</summary>
    internal byte[] Kinds { get; }

    /// <summary>Gets the Intra 4x4 prediction mode of each 4x4 block of each macroblock, by raster position in the macroblock.</summary>
    internal byte[] IntraModes { get; }

    /// <summary>Gets the number of non-zero coefficients of each block of each macroblock.</summary>
    internal byte[] Counts { get; }

    /// <summary>Gets the luma quantiser of each macroblock.</summary>
    internal sbyte[] Qp { get; }

    /// <summary>Gets the coded block pattern of each macroblock: the luma bits, then the chroma value shifted left by four.</summary>
    internal byte[] Cbp { get; }

    /// <summary>Gets the intra chroma prediction mode of each macroblock.</summary>
    internal byte[] ChromaModes { get; }

    /// <summary>Gets which DC blocks of each macroblock had coded_block_flag set: bit 0 luma, bit 1 Cb, bit 2 Cr.</summary>
    internal byte[] DcFlags { get; }

    /// <summary>Gets the motion of every macroblock for reference list 0.</summary>
    internal H264MotionField Field0 { get; }

    /// <summary>Gets the motion of every macroblock for reference list 1.</summary>
    internal H264MotionField Field1 { get; }

    /// <summary>Gets the horizontal motion vector of each 4x4 block of each macroblock for list 0, in quarter samples, by raster position in the macroblock.</summary>
    internal short[] MvX => Field0.MvX;

    /// <summary>Gets the vertical motion vector of each 4x4 block of each macroblock for list 0, in quarter samples.</summary>
    internal short[] MvY => Field0.MvY;

    /// <summary>Gets the list 0 reference index of each 8x8 group of each macroblock, or -1 when the group is intra coded.</summary>
    internal sbyte[] RefIdx => Field0.RefIdx;

    /// <summary>Gets the identity of the frame each 8x8 group of each macroblock refers to in list 0, or -1 when it is intra coded.</summary>
    internal int[] RefUid => Field0.RefUid;

    /// <summary>Gets the size of the horizontal list 0 motion vector difference of each 4x4 block of each macroblock, limited to 255.</summary>
    internal byte[] MvdX => Field0.MvdX;

    /// <summary>Gets the size of the vertical list 0 motion vector difference of each 4x4 block of each macroblock, limited to 255.</summary>
    internal byte[] MvdY => Field0.MvdY;

    /// <summary>Gets or sets the surface a hardware decoder holds this picture in, for a picture whose samples are not kept here.</summary>
    internal uint Surface { get; set; }

    /// <summary>Gets or sets the identity of this frame among the frames of the stream.</summary>
    internal int Uid { get; set; }

    /// <summary>Gets or sets the picture order count of the frame.</summary>
    internal int Poc { get; set; }

    /// <summary>Gets or sets the offset of the Cb quantiser from the luma quantiser, which the deblocking filter needs.</summary>
    internal int ChromaQpOffsetCb { get; set; }

    /// <summary>Gets or sets the offset of the Cr quantiser from the luma quantiser, which the deblocking filter needs.</summary>
    internal int ChromaQpOffsetCr { get; set; }

    /// <summary>Gets the deblocking filter settings of each slice, in the order the slices were decoded. There is room for a slice of a single macroblock each.</summary>
    internal H264SliceFilter[] SliceFilters { get; }

    /// <summary>Gets a value for each macroblock saying whether it was skipped.</summary>
    internal bool[] Skipped { get; }

    /// <summary>Gets a value for each macroblock saying whether its luma uses the 8x8 transform.</summary>
    internal bool[] Transform8x8 { get; }

    /// <summary>Gets a value for each macroblock saying whether it is a B_Skip or B_Direct_16x16 macroblock.</summary>
    internal bool[] DirectOnly { get; }

    /// <summary>Gets which 8x8 groups of each macroblock got their motion by direct prediction: bit 0 is the first group.</summary>
    internal byte[] DirectGroups { get; }

    /// <summary>Records that a macroblock has been decoded. Only the thread decoding the picture calls this.</summary>
    /// <param name="address">The macroblock's address.</param>
    /// <returns><see langword="true"/> when the macroblock was the last of its row to be decoded.</returns>
    internal bool MacroblockDecoded(int address)
    {
        ref var count = ref RowsDecoded[address / MbWidth];
        var next = count + 1;
        Volatile.Write(ref count, next);
        if (next != MbWidth)
        {
            return false;
        }

        _gate.Pulse();
        return true;
    }

    /// <summary>Tells whether a row can be filtered: the row and the one below it are decoded, and the one below holds the last reads of the row's samples.</summary>
    /// <param name="row">The row.</param>
    /// <returns><see langword="true"/> when it can.</returns>
    internal bool CanFilter(int row) => RowsDecoded[row] >= MbWidth && RowsDecoded[Math.Min(row + 1, MbHeight - 1)] >= MbWidth;

    /// <summary>Records that the next row has been filtered, which makes the lines above the reach of the next row's filtering final. Only the thread decoding the picture calls this.</summary>
    internal void RowFiltered()
    {
        FilteredRows++;
        Volatile.Write(ref _finalLines, FilteredRows == MbHeight ? LumaLines : (FilteredRows * LumaSize) - FilterReach);
        _gate.Pulse();
    }

    /// <summary>Says that the picture is decoded and filtered, so every line of it is final.</summary>
    internal void FinishDecoding()
    {
        Volatile.Write(ref _finalLines, LumaLines);
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

    /// <summary>Waits until a line of luma is final, so it can be read as a reference.</summary>
    /// <param name="line">The line, from 0 to one less than <see cref="LumaLines"/>.</param>
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

    /// <summary>Waits until a row of macroblocks is decoded, so its motion can be read for direct prediction.</summary>
    /// <param name="row">The row.</param>
    /// <param name="tokens">The caller's tokens, to give up while it sleeps, or <see langword="null"/>.</param>
    /// <exception cref="InvalidDataException">The picture failed to decode.</exception>
    internal void WaitForDecodedRow(int row, WorkTokens? tokens)
    {
        if (Volatile.Read(ref RowsDecoded[row]) >= MbWidth)
        {
            return;
        }

        _gate.Wait(new RowDecoded(this, row), tokens);
    }

    /// <summary>Tells whether no decoding job uses the picture.</summary>
    /// <returns><see langword="true"/> when it can be decoded into again.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal bool IsIdle() => Volatile.Read(ref _users) == 0;

    /// <summary>Marks a macroblock as having no motion, as an intra macroblock does, and as not skipped.</summary>
    /// <param name="address">The macroblock's address.</param>
    internal void ClearMotion(int address)
    {
        Skipped[address] = false;
        Transform8x8[address] = false;
        DirectOnly[address] = false;
        DirectGroups[address] = 0;
        Field0.Clear(address);
        Field1.Clear(address);
    }

    /// <summary>
    /// Clears what decoding the picture before left in its arrays, so they hold what a new picture's do. This is the larger part of getting a picture ready
    /// to be decoded into again, and the thread that decodes the picture does it, so the thread that reads the packets does not have to.
    /// </summary>
    internal void ClearData()
    {
        // Decoding writes every sample of a macroblock, so the planes of a picture whose macroblocks were all decoded hold nothing that needs clearing.
        if (Array.IndexOf(SliceOfMb, -1) >= 0)
        {
            Array.Clear(Luma);
            Array.Clear(Cb);
            Array.Clear(Cr);
        }

        Array.Clear(IntraModes);
        Array.Clear(Counts);
        Array.Clear(Qp);
        Array.Clear(Cbp);
        Array.Clear(ChromaModes);
        Array.Clear(DcFlags);
        Array.Clear(Skipped);
        Array.Clear(Transform8x8);
        Array.Clear(DirectOnly);
        Array.Clear(DirectGroups);
        Field0.Recycle();
        Field1.Recycle();
        ResetMacroblocks();
    }

    /// <summary>Returns the picture's state to the state a new one has, so a picture nothing refers to any more can be decoded into again. <see cref="ClearData"/> does the rest.</summary>
    internal void Recycle()
    {
        Array.Clear(RowsDecoded);
        FilteredRows = 0;
        Volatile.Write(ref _finalLines, 0);
        Volatile.Write(ref _decoding, Running);
        Uid = 0;
        Poc = 0;
        ChromaQpOffsetCb = 0;
        ChromaQpOffsetCr = 0;
    }

    /// <summary>Marks every macroblock as not decoded.</summary>
    internal void ResetMacroblocks()
    {
        Array.Fill(SliceOfMb, -1);
        Array.Clear(Kinds);
    }

    /// <summary>Throws when the picture failed to decode, so a picture that waits for it does not wait for ever.</summary>
    /// <exception cref="InvalidDataException">The picture failed to decode.</exception>
    private void ThrowIfAborted()
    {
        if (Volatile.Read(ref _decoding) == Aborted)
        {
            throw new InvalidDataException("An H.264 picture that a later picture refers to failed to decode.");
        }
    }

    /// <summary>Waits for a line of luma to be final.</summary>
    /// <param name="Picture">The picture.</param>
    /// <param name="Line">The line.</param>
    private readonly record struct LinesFinal(H264Picture Picture, int Line) : IProgressCondition
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

    /// <summary>Waits for a row of macroblocks to be decoded.</summary>
    /// <param name="Picture">The picture.</param>
    /// <param name="Row">The row.</param>
    private readonly record struct RowDecoded(H264Picture Picture, int Row) : IProgressCondition
    {
        /// <inheritdoc/>
        public bool IsMet()
        {
            if (Volatile.Read(ref Picture.RowsDecoded[Row]) >= Picture.MbWidth || Volatile.Read(ref Picture._decoding) == Finished)
            {
                return true;
            }

            Picture.ThrowIfAborted();
            return false;
        }
    }
}
