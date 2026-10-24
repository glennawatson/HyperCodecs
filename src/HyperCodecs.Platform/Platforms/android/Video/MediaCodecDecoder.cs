// Copyright (c) 2026 Glenn Watson. All rights reserved.
// Glenn Watson licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Buffers;
using System.Diagnostics;
using Android.Graphics;
using Android.Media;
using Java.Nio;
using AndroidCodec = Android.Media.MediaCodec;

namespace HyperCodecs.Platform.Video.AndroidMedia;

/// <summary>
/// Decodes H.264 and H.265 pictures with the hardware decoder Android provides, through MediaCodec. The coded pictures go in as Annex B
/// units and the pictures come out in the order they are shown, so nothing is held back for reordering here.
/// </summary>
[DebuggerDisplay("MediaCodecDecoder: {Width}x{Height}")]
internal sealed unsafe class MediaCodecDecoder : VideoDecoder
{
    /// <summary>The MIME type of H.264.</summary>
    private const string AvcMime = "video/avc";

    /// <summary>The MIME type of H.265.</summary>
    private const string HevcMime = "video/hevc";

    /// <summary>The name of the format entry that holds the parameter sets.</summary>
    private const string ParameterSetsKey = "csd-0";

    /// <summary>The microseconds in a second.</summary>
    private const double Microseconds = 1_000_000.0;

    /// <summary>The microseconds to wait for a buffer while the decoder is busy.</summary>
    private const long BufferWaitMicroseconds = 10_000;

    /// <summary>The times to wait for an input buffer before giving up.</summary>
    private const int InputAttempts = 500;

    /// <summary>The samples of luma for each sample of colour, across and down.</summary>
    private const int ChromaSubsampling = 2;

    /// <summary>The plane of an image that holds luma.</summary>
    private const int LumaPlane = 0;

    /// <summary>The plane of an image that holds Cb.</summary>
    private const int CbPlane = 1;

    /// <summary>The plane of an image that holds Cr.</summary>
    private const int CrPlane = 2;

    /// <summary>The decoder.</summary>
    private readonly AndroidCodec _codec;

    /// <summary>The bytes that tell how long each NAL unit of a packet is.</summary>
    private readonly int _lengthSize;

    /// <summary>The packet converted to Annex B, reused for every packet.</summary>
    private readonly ArrayBufferWriter<byte> _stream = new();

    /// <summary>Receives facts about each output buffer.</summary>
    private readonly AndroidCodec.BufferInfo _info = new();

    /// <summary>Initializes a new instance of the <see cref="MediaCodecDecoder"/> class.</summary>
    /// <param name="track">The track.</param>
    /// <param name="codec">The started decoder.</param>
    /// <param name="lengthSize">The bytes that tell how long each NAL unit is.</param>
    private MediaCodecDecoder(MediaTrack track, AndroidCodec codec, int lengthSize)
        : base(track.Width, track.Height)
    {
        _codec = codec;
        _lengthSize = lengthSize;
    }

    /// <inheritdoc/>
    public override void Decode(MediaPacket packet, List<VideoFrame> output)
    {
        ArgumentNullException.ThrowIfNull(packet);
        ArgumentNullException.ThrowIfNull(output);
        _stream.ResetWrittenCount();
        if (!AnnexB.WriteUnits(packet.Data, _lengthSize, _stream))
        {
            throw new InvalidDataException("A packet of pictures is not valid.");
        }

        Submit(_stream.WrittenSpan, (long)(packet.Time * Microseconds), 0, output);
        _ = Drain(output, 0);
    }

    /// <inheritdoc/>
    public override void Flush(List<VideoFrame> output)
    {
        ArgumentNullException.ThrowIfNull(output);
        Submit([], 0, MediaCodecBufferFlags.EndOfStream, output);
        var ended = false;
        for (var attempt = 0; attempt < InputAttempts && !ended; attempt++)
        {
            ended = Drain(output, BufferWaitMicroseconds);
        }

        _codec.Flush();
    }

    /// <inheritdoc/>
    public override void Reset() => _codec.Flush();

    /// <summary>Makes a decoder for a track.</summary>
    /// <param name="track">The track.</param>
    /// <param name="failure">Receives why no decoder was made, when none was.</param>
    /// <returns>The decoder, or <see langword="null"/> when the coding is not H.264 or H.265, or Android has no hardware decoder for it.</returns>
    internal static MediaCodecDecoder? TryCreate(MediaTrack track, out DecoderFailure failure)
    {
        ArgumentNullException.ThrowIfNull(track);
        failure = new("The track is not one MediaCodec can be given", 0);
        var mime = track.Codec switch
        {
            MediaCodec.H264 => AvcMime,
            MediaCodec.H265 => HevcMime,
            _ => null,
        };
        var lengthSize = AnnexB.LengthSize(track.Codec, track.Configuration.Span);
        var sets = new ArrayBufferWriter<byte>();
        var usable = OperatingSystem.IsAndroid() && mime is not null && track.Kind == MediaTrackKind.Video && track.Width > 0 && track.Height > 0;
        return usable && lengthSize != 0 && AnnexB.WriteParameterSets(track.Codec, track.Configuration.Span, sets)
            ? Start(track, mime!, lengthSize, sets.WrittenMemory.ToArray(), out failure)
            : null;
    }

    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
        if (!disposing)
        {
            return;
        }

        _codec.Stop();
        _codec.Release();
        _codec.Dispose();
        _info.Dispose();
    }

    /// <summary>Makes and starts the decoder, when Android has a hardware one for the coding.</summary>
    /// <param name="track">The track.</param>
    /// <param name="mime">The MIME type of the coding.</param>
    /// <param name="lengthSize">The bytes that tell how long each NAL unit is.</param>
    /// <param name="parameterSets">The parameter sets in Annex B form.</param>
    /// <param name="failure">Receives why no decoder was made, when none was.</param>
    /// <returns>The decoder, or <see langword="null"/>.</returns>
    private static MediaCodecDecoder? Start(MediaTrack track, string mime, int lengthSize, byte[] parameterSets, out DecoderFailure failure)
    {
        failure = default;
        var codec = CreateCodec(mime, out failure);
        if (codec is null)
        {
            return null;
        }

        try
        {
            using var format = MediaFormat.CreateVideoFormat(mime, track.Width, track.Height);
            using var sets = ByteBuffer.Wrap(parameterSets);
            format.SetByteBuffer(ParameterSetsKey, sets);
            codec.Configure(format, null, null, MediaCodecConfigFlags.None);
            codec.Start();
            return new(track, codec, lengthSize);
        }
        catch (Java.Lang.Exception exception)
        {
            failure = new($"MediaCodec could not start: {exception.Message}", 0);
            codec.Dispose();
            return null;
        }
    }

    /// <summary>Makes the decoder for a coding, when it is done by hardware.</summary>
    /// <param name="mime">The MIME type of the coding.</param>
    /// <param name="failure">Receives why there is no decoder, when there is none.</param>
    /// <returns>The decoder, or <see langword="null"/>.</returns>
    private static AndroidCodec? CreateCodec(string mime, out DecoderFailure failure)
    {
        failure = default;
        try
        {
            var codec = AndroidCodec.CreateDecoderByType(mime);
            if (codec.CodecInfo is { IsHardwareAccelerated: true })
            {
                return codec;
            }

            failure = new("Android has no hardware decoder for the coding", 0);
            codec.Dispose();
            return null;
        }
        catch (Java.Lang.Exception exception)
        {
            failure = new($"MediaCodec could not be opened: {exception.Message}", 0);
            return null;
        }
    }

    /// <summary>Finds where the first sample of the cropped picture is in a plane.</summary>
    /// <param name="plane">The plane.</param>
    /// <param name="left">The samples to skip in a row.</param>
    /// <param name="top">The rows to skip.</param>
    /// <returns>The byte offset.</returns>
    private static int OffsetOf(Image.Plane plane, int left, int top) => (top * plane.RowStride) + (left * plane.PixelStride);

    /// <summary>Copies the samples of an image plane into a run of a frame, skipping the padding between rows and between samples.</summary>
    /// <param name="plane">The plane.</param>
    /// <param name="width">The samples in a row to copy.</param>
    /// <param name="height">The rows to copy.</param>
    /// <param name="offset">The bytes to skip at the start of the plane.</param>
    /// <param name="destination">Receives the samples, tightly packed.</param>
    /// <exception cref="InvalidDataException">The plane is smaller than the picture.</exception>
    private static void CopyPlane(Image.Plane plane, int width, int height, int offset, byte[] destination)
    {
        var buffer = plane.Buffer ?? throw new InvalidDataException("MediaCodec gave a picture with a missing plane.");
        var rowStride = plane.RowStride;
        var pixelStride = plane.PixelStride;
        var length = buffer.Capacity();
        if (offset + (rowStride * (height - 1)) + (pixelStride * (width - 1)) >= length)
        {
            throw new InvalidDataException("MediaCodec gave a picture smaller than the track says.");
        }

        var source = new ReadOnlySpan<byte>((void*)buffer.GetDirectBufferAddress(), length);
        for (var row = 0; row < height; row++)
        {
            CopyRow(source[(offset + (row * rowStride))..], pixelStride, destination.AsSpan(row * width, width));
        }
    }

    /// <summary>Copies one row of samples that are a fixed distance apart.</summary>
    /// <param name="source">The row, starting at its first sample.</param>
    /// <param name="pixelStride">The bytes between two samples.</param>
    /// <param name="destination">Receives the samples.</param>
    private static void CopyRow(ReadOnlySpan<byte> source, int pixelStride, Span<byte> destination)
    {
        if (pixelStride == 1)
        {
            source[..destination.Length].CopyTo(destination);
            return;
        }

        for (var x = 0; x < destination.Length; x++)
        {
            destination[x] = source[x * pixelStride];
        }
    }

    /// <summary>Gives the decoder one coded picture, draining pictures while it has no room.</summary>
    /// <param name="data">The picture in Annex B form, or nothing for the end of the stream.</param>
    /// <param name="time">When the picture is shown, in microseconds.</param>
    /// <param name="flags">The buffer flags.</param>
    /// <param name="output">Receives the pictures drained while waiting.</param>
    /// <exception cref="InvalidDataException">The decoder did not take the picture.</exception>
    private void Submit(ReadOnlySpan<byte> data, long time, MediaCodecBufferFlags flags, List<VideoFrame> output)
    {
        for (var attempt = 0; attempt < InputAttempts; attempt++)
        {
            var index = _codec.DequeueInputBuffer(BufferWaitMicroseconds);
            if (index < 0)
            {
                _ = Drain(output, 0);
                continue;
            }

            using var buffer = _codec.GetInputBuffer(index) ?? throw new InvalidDataException("MediaCodec gave no input buffer.");
            if (data.Length > buffer.Capacity())
            {
                throw new InvalidDataException("A packet of pictures is larger than the decoder takes.");
            }

            data.CopyTo(new((void*)buffer.GetDirectBufferAddress(), data.Length));
            _codec.QueueInputBuffer(index, 0, data.Length, time, flags);
            return;
        }

        throw new InvalidDataException("MediaCodec did not take a packet of pictures.");
    }

    /// <summary>Takes the finished pictures out of the decoder.</summary>
    /// <param name="output">Receives the pictures.</param>
    /// <param name="wait">The microseconds to wait for the first picture.</param>
    /// <returns><see langword="true"/> when the end of the stream came out.</returns>
    private bool Drain(List<VideoFrame> output, long wait)
    {
        while (true)
        {
            var index = _codec.DequeueOutputBuffer(_info, wait);
            if (index < 0)
            {
                // A format change is not a picture, so look again; anything else means nothing is ready.
                if (index == (int)MediaCodecInfoState.OutputFormatChanged)
                {
                    continue;
                }

                return false;
            }

            var ended = (_info.Flags & MediaCodecBufferFlags.EndOfStream) != 0;
            if (_info.Size > 0)
            {
                output.Add(ReadPicture(index, _info.PresentationTimeUs / Microseconds));
            }

            _codec.ReleaseOutputBuffer(index, false);
            if (ended)
            {
                return true;
            }

            wait = 0;
        }
    }

    /// <summary>Copies a finished picture out of the decoder.</summary>
    /// <param name="index">The output buffer.</param>
    /// <param name="time">When the picture is shown, in seconds.</param>
    /// <returns>The picture.</returns>
    /// <exception cref="InvalidDataException">The decoder gave no picture.</exception>
    private VideoFrame ReadPicture(int index, double time)
    {
        using var image = _codec.GetOutputImage(index) ?? throw new InvalidDataException("MediaCodec gave no picture.");
        var planes = image.GetPlanes() ?? throw new InvalidDataException("MediaCodec gave a picture with no planes.");
        using var crop = image.CropRect ?? new Rect(0, 0, Width, Height);
        var frame = RentFrame(Width, Height, time);
        CopyPlane(planes[LumaPlane], frame.Width, frame.Height, OffsetOf(planes[LumaPlane], crop.Left, crop.Top), frame.Y);
        CopyPlane(planes[CbPlane], frame.ChromaWidth, frame.ChromaHeight, OffsetOf(planes[CbPlane], crop.Left / ChromaSubsampling, crop.Top / ChromaSubsampling), frame.U);
        CopyPlane(planes[CrPlane], frame.ChromaWidth, frame.ChromaHeight, OffsetOf(planes[CrPlane], crop.Left / ChromaSubsampling, crop.Top / ChromaSubsampling), frame.V);
        return frame;
    }
}
