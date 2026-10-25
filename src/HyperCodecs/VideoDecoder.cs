// Copyright (c) 2026 Glenn Watson. All rights reserved.
// Glenn Watson licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Diagnostics;

namespace HyperCodecs;

/// <summary>Turns the packets of one picture track into pictures. A decoder keeps what it needs from one packet to the next, so packets go in the order the file stores them.</summary>
[DebuggerDisplay("VideoDecoder: {Width}x{Height}")]
public abstract class VideoDecoder : IDisposable
{
    /// <summary>The most pictures kept for reuse.</summary>
    private const int MaxSpare = 8;

    /// <summary>The pictures handed back to <see cref="Recycle"/> and not yet reused.</summary>
    private readonly Stack<VideoFrame> _spare = new();

    /// <summary>Initializes a new instance of the <see cref="VideoDecoder"/> class.</summary>
    /// <param name="width">The width of the pictures in pixels.</param>
    /// <param name="height">The height of the pictures in pixels.</param>
    protected VideoDecoder(int width, int height)
    {
        Width = width;
        Height = height;
    }

    /// <summary>Gets the width of the pictures in pixels.</summary>
    public int Width { get; }

    /// <summary>Gets the height of the pictures in pixels.</summary>
    public int Height { get; }

    /// <summary>
    /// Gets or sets the most threads the decoder may use. The default is 1: the decoder uses the thread that calls it and no other. With more, the decoder
    /// keeps threads waiting between pictures until it is disposed. Pictures come out the same whatever the number. A decoder that works on several pictures
    /// at once (H.264 and H.265 reconstructed here) gives each picture back a few calls to <c>Decode</c> later than it otherwise would, and <c>Flush</c> gives
    /// back the rest. A damaged picture reports its error when the picture would have been given back, not when its packet is decoded.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is less than 1.</exception>
    public int MaxThreads
    {
        get;
        set
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, 1);
            field = value;
        }
    } = 1;

    /// <inheritdoc/>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>Hands back a picture the caller has finished with, so the decoder can fill it again instead of allocating one.</summary>
    /// <param name="frame">The picture.</param>
    /// <remarks>The caller must not use the picture afterwards, and must call this from the thread that decodes.</remarks>
    public virtual void Recycle(VideoFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (_spare.Count < MaxSpare && frame.Width == Width && frame.Height == Height)
        {
            _spare.Push(frame);
        }
    }

    /// <summary>Decodes a packet and appends the pictures that are ready to be shown, in the order they are shown.</summary>
    /// <param name="packet">The packet.</param>
    /// <param name="output">Receives the pictures.</param>
    /// <exception cref="InvalidDataException">The packet is not valid for this coding.</exception>
    public abstract void Decode(MediaPacket packet, List<VideoFrame> output);

    /// <summary>Appends the pictures still held back for reordering, at the end of the stream.</summary>
    /// <param name="output">Receives the pictures.</param>
    public abstract void Flush(List<VideoFrame> output);

    /// <summary>Forgets what was kept from earlier packets, for decoding to start again at a key packet after a jump in the file.</summary>
    public abstract void Reset();

    /// <summary>Gets a picture to fill: one handed back to <see cref="Recycle"/> when it has the size, otherwise a new one.</summary>
    /// <param name="width">The width in pixels.</param>
    /// <param name="height">The height in pixels.</param>
    /// <param name="time">When the picture is shown, in seconds.</param>
    /// <returns>The picture.</returns>
    /// <remarks>The samples of a recycled picture are those it last held, so every sample must be written.</remarks>
    protected VideoFrame RentFrame(int width, int height, double time)
    {
        if (_spare.TryPop(out var frame) && frame.Width == width && frame.Height == height)
        {
            frame.Time = time;
            return frame;
        }

        return new(width, height, time);
    }

    /// <summary>Lets go of what the decoder holds.</summary>
    /// <param name="disposing"><see langword="true"/> when called from <see cref="Dispose()"/>.</param>
    protected abstract void Dispose(bool disposing);
}
