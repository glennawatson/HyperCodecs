// Copyright (c) 2026 Glenn Watson. All rights reserved.
// Glenn Watson licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Buffers.Binary;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace HyperCodecs;

/// <summary>
/// The CABAC arithmetic decoding engine of clause 9.3.3.2 of ITU-T H.264 and clause 9.3.4.3 of ITU-T H.265, with the context states it decodes with.
/// The offset of the specification (9 bits) is kept with the bits that follow it, loaded 32 at a time: the value holds the offset in its high
/// bits and the next <c>_extra</c> bits below it, and every comparison scales the range to match. This reads the data once for every 32 bits
/// decoded instead of once for every bin.
/// </summary>
internal ref struct CabacEngine
{
    /// <summary>The mask of a context byte (a state of 0 to 63 and the more probable symbol), which keeps the table lookups inside their tables without a bounds check.</summary>
    private const int ContextMask = 0x7F;

    /// <summary>The bits of codIOffset at the start.</summary>
    private const int OffsetBits = 9;

    /// <summary>The value codIRange starts at.</summary>
    private const int InitialRange = 510;

    /// <summary>The smallest codIRange that needs no renormalisation.</summary>
    private const int MinRange = 256;

    /// <summary>The leading zeros of a 32 bit value whose highest set bit is bit 8.</summary>
    private const int LeadingZerosAtMin = 23;

    /// <summary>The shift that finds the quantised range index.</summary>
    private const int RangeIndexShift = 6;

    /// <summary>The mask of the quantised range index.</summary>
    private const int RangeIndexMask = 3;

    /// <summary>The amount codIRange shrinks by before the terminating bin is tested.</summary>
    private const int TerminateRange = 2;

    /// <summary>The bits of a byte.</summary>
    private const int ByteBits = 8;

    /// <summary>The shift that turns a bit index into a byte index.</summary>
    private const int ByteShift = 3;

    /// <summary>The mask of the bit within a byte.</summary>
    private const int BitMask = 7;

    /// <summary>The shift that turns a context byte into the index of its row of range values: the state, times four entries.</summary>
    private const int RowShift = 1;

    /// <summary>The shift that multiplies a state by the four range values it has.</summary>
    private const int RowEntriesShift = 2;

    /// <summary>The bits loaded at a time.</summary>
    private const int LoadBits = 32;

    /// <summary>The bytes loaded at a time.</summary>
    private const int LoadBytes = 4;

    /// <summary>The bits read when the engine starts: enough to hold the offset and whole bytes of the bits after it.</summary>
    private const int StartBits = 40;

    /// <summary>The bytes read when the engine starts.</summary>
    private const int StartBytes = 5;

    /// <summary>The fewest bits kept after the offset; below this the next bits are loaded, so a renormalisation never runs out.</summary>
    private const int MinExtra = 8;

    /// <summary>The slice data.</summary>
    private readonly ReadOnlySpan<byte> _data;

    /// <summary>The context states: the probability state in the high bits and the most probable symbol in the low bit.</summary>
    private readonly Span<byte> _contexts;

    /// <summary>The offset in its high bits, then <see cref="_extra"/> bits that follow it in the data.</summary>
    private ulong _value;

    /// <summary>The range of the interval.</summary>
    private int _range;

    /// <summary>The number of bits held in the value below the offset.</summary>
    private int _extra;

    /// <summary>The index of the first bit not yet loaded into the value; always on a byte boundary.</summary>
    private long _loaded;

    /// <summary>Initializes a new instance of the <see cref="CabacEngine"/> struct and reads the first bits (clause 9.3.1.2).</summary>
    /// <param name="data">The slice data.</param>
    /// <param name="bitPosition">The index of the bit where arithmetic coded data starts.</param>
    /// <param name="contexts">The context states, already initialised.</param>
    internal CabacEngine(ReadOnlySpan<byte> data, long bitPosition, Span<byte> contexts)
    {
        _data = data;
        _contexts = contexts;
        Restart(bitPosition);
    }

    /// <summary>Gets the slice data the engine reads.</summary>
    internal readonly ReadOnlySpan<byte> Data => _data;

    /// <summary>Gets the index of the next bit the engine would read: the bits of the offset and the ones renormalisation has used.</summary>
    internal readonly long BitPosition => _loaded - _extra;

    /// <summary>Gets a value indicating whether the engine has read past the end of the data.</summary>
    internal readonly bool IsOverrun => BitPosition > (long)_data.Length * ByteBits;

    /// <summary>Decodes one bin with a context (clause 9.3.3.2.1).</summary>
    /// <param name="ctxIdx">The context index, not 276.</param>
    /// <returns>The bin, 0 or 1.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal int DecodeDecision(int ctxIdx)
    {
        var context = _contexts[ctxIdx] & ContextMask;
        var rangeLps = CabacTables.RangeLps[((context >> RowShift) << RowEntriesShift) + ((_range >> RangeIndexShift) & RangeIndexMask)];
        _range -= rangeLps;
        var scaled = (ulong)_range << _extra;
        int bin;
        if (_value < scaled)
        {
            bin = context & 1;
            _contexts[ctxIdx] = CabacTables.ContextAfterMps[context];
        }
        else
        {
            bin = (context & 1) ^ 1;
            _value -= scaled;
            _range = rangeLps;
            _contexts[ctxIdx] = CabacTables.ContextAfterLps[context];
        }

        Renormalize();
        return bin;
    }

    /// <summary>Decodes one bin that is as likely to be 0 as 1 (clause 9.3.3.2.3).</summary>
    /// <returns>The bin.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal int DecodeBypass()
    {
        _extra--;
        var scaled = (ulong)_range << _extra;
        var bin = 0;
        if (_value >= scaled)
        {
            _value -= scaled;
            bin = 1;
        }

        if (_extra < MinExtra)
        {
            Load();
        }

        return bin;
    }

    /// <summary>Decodes the bin that tells the end of a slice or an I_PCM macroblock (clause 9.3.3.2.4).</summary>
    /// <returns>The bin; 1 means arithmetic decoding has ended.</returns>
    internal int DecodeTerminate()
    {
        _range -= TerminateRange;
        if (_value >= (ulong)_range << _extra)
        {
            return 1;
        }

        Renormalize();
        return 0;
    }

    /// <summary>Starts decoding again at a bit position, as after the samples of an I_PCM macroblock (clause 9.3.1.2).</summary>
    /// <param name="bitPosition">The index of the bit where arithmetic coded data resumes.</param>
    internal void Restart(long bitPosition)
    {
        var first = (int)(bitPosition >> ByteShift);
        var skip = (int)bitPosition & BitMask;
        ulong word = 0;
        for (var i = 0; i < StartBytes; i++)
        {
            word = (word << ByteBits) | (uint)ByteAt(first + i);
        }

        _value = word & ((1UL << (StartBits - skip)) - 1);
        _extra = StartBits - skip - OffsetBits;
        _loaded = ((long)first * ByteBits) + StartBits;
        _range = InitialRange;
    }

    /// <summary>Doubles the range until it is at least 256, taking the bits that follow the offset into it (clause 9.3.3.2.2).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void Renormalize()
    {
        if (_range >= MinRange)
        {
            return;
        }

        var shift = BitOperations.LeadingZeroCount((uint)_range) - LeadingZerosAtMin;
        _range <<= shift;
        _extra -= shift;
        if (_extra < MinExtra)
        {
            Load();
        }
    }

    /// <summary>Loads the next 32 bits of the data below the bits already held; bits past the end read as zero.</summary>
    private void Load()
    {
        var index = (int)(_loaded >> ByteShift);
        uint word;
        if (index + LoadBytes <= _data.Length)
        {
            word = BinaryPrimitives.ReadUInt32BigEndian(_data.Slice(index, LoadBytes));
        }
        else
        {
            word = 0;
            for (var i = 0; i < LoadBytes; i++)
            {
                word = (word << ByteBits) | (uint)ByteAt(index + i);
            }
        }

        _value = (_value << LoadBits) | word;
        _extra += LoadBits;
        _loaded += LoadBits;
    }

    /// <summary>Gets a byte of the data, or zero past the end.</summary>
    /// <param name="index">The byte's index.</param>
    /// <returns>The byte.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private readonly int ByteAt(int index) => (uint)index < (uint)_data.Length ? _data[index] : 0;
}
