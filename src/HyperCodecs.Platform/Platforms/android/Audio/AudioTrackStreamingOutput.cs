// Copyright (c) 2026 Glenn Watson. All rights reserved.
// Glenn Watson licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Diagnostics;
using System.Runtime.CompilerServices;
using Android.Media;
using Track = Android.Media.AudioTrack;

namespace HyperCodecs.Platform.Audio.AndroidMedia;

/// <summary>
/// Plays a continuous stream of sound through an Android <c>AudioTrack</c>. A write waits while the track holds about a fifth of a
/// second, so the caller stays just ahead of what is heard. The latency is the sound written but not yet played.
/// </summary>
[DebuggerDisplay("AudioTrackStreamingOutput: AudioTrack")]
internal sealed class AudioTrackStreamingOutput : IStreamingAudioOutput
{
    /// <summary>The audio the track keeps buffered, in seconds.</summary>
    private const double TargetSeconds = 0.2;

    /// <summary>The seconds of sound written at a time, so a stop is noticed quickly.</summary>
    private const double ChunkSeconds = 0.025;

    /// <summary>The channels of a mono stream.</summary>
    private const int Mono = 1;

    /// <summary>The channels of a stereo stream.</summary>
    private const int Stereo = 2;

    /// <summary>The channels of a 5.1 stream.</summary>
    private const int FivePointOne = 6;

    /// <summary>Serialises calls to the track.</summary>
    private readonly Lock _gate = new();

    /// <summary>The open track.</summary>
    private Track? _track;

    /// <summary>The samples written at a time.</summary>
    private float[] _chunk = [];

    /// <summary>The frames to play each second.</summary>
    private int _sampleRate;

    /// <summary>The frames written, counted the way the track counts its play position: modulo 2 to the 32.</summary>
    private uint _framesWritten;

    /// <summary>The channels of the open track.</summary>
    private int _channels;

    /// <inheritdoc/>
    public bool IsAvailable => OperatingSystem.IsAndroid();

    /// <inheritdoc/>
    public TimeSpan Latency
    {
        get
        {
            lock (_gate)
            {
                if (_track is null || _sampleRate <= 0)
                {
                    return TimeSpan.Zero;
                }

                var pending = unchecked(_framesWritten - (uint)_track.PlaybackHeadPosition);
                return TimeSpan.FromSeconds((double)pending / _sampleRate);
            }
        }
    }

    /// <inheritdoc/>
    public bool Start(int sampleRate, int channels)
    {
        if (!IsAvailable || ChannelMaskOf(channels) is not { } mask)
        {
            return false;
        }

        lock (_gate)
        {
            Release();
            var bytes = (int)(sampleRate * channels * sizeof(float) * TargetSeconds);
            var size = Math.Max(bytes, Track.GetMinBufferSize(sampleRate, mask, Encoding.PcmFloat));
            using var attributes = new AudioAttributes.Builder().SetUsage(AudioUsageKind.Media)!.SetContentType(AudioContentType.Music)!.Build()!;
            using var format = new AudioFormat.Builder().SetEncoding(Encoding.PcmFloat)!.SetSampleRate(sampleRate)!.SetChannelMask(mask)!.Build()!;
            using var builder = new Track.Builder().SetAudioAttributes(attributes)!.SetAudioFormat(format)!.SetBufferSizeInBytes(size)!.SetTransferMode(AudioTrackMode.Stream)!;
            var track = builder.Build();
            if (track.State != AudioTrackState.Initialized)
            {
                track.Release();
                track.Dispose();
                return false;
            }

            _track = track;
            _sampleRate = sampleRate;
            _channels = channels;
            _chunk = new float[Math.Max(1, (int)(sampleRate * ChunkSeconds)) * channels];
            _framesWritten = (uint)track.PlaybackHeadPosition;
            track.Play();
            return true;
        }
    }

    /// <inheritdoc/>
    public bool Write(ReadOnlySpan<float> samples, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (_track is null)
            {
                return false;
            }

            for (var offset = 0; offset < samples.Length; offset += _chunk.Length)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return false;
                }

                var piece = samples.Slice(offset, Math.Min(_chunk.Length, samples.Length - offset));
                piece.CopyTo(_chunk);
                if (_track.Write(_chunk, 0, piece.Length, WriteMode.Blocking) != piece.Length)
                {
                    return false;
                }

                _framesWritten = unchecked(_framesWritten + (uint)(piece.Length / _channels));
            }

            return true;
        }
    }

    /// <inheritdoc/>
    public void Flush()
    {
        lock (_gate)
        {
            if (_track is null)
            {
                return;
            }

            _track.Pause();
            _track.Flush();
            _framesWritten = (uint)_track.PlaybackHeadPosition;
            _track.Play();
        }
    }

    /// <inheritdoc/>
    public void Close()
    {
        lock (_gate)
        {
            Release();
        }
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Dispose() => Close();

    /// <summary>Finds the channel mask for a channel count.</summary>
    /// <param name="channels">The channel count.</param>
    /// <returns>The mask, or <see langword="null"/> when the count is not played.</returns>
    private static ChannelOut? ChannelMaskOf(int channels) => channels switch
    {
        Mono => ChannelOut.Mono,
        Stereo => ChannelOut.Stereo,
        FivePointOne => ChannelOut.FivePointOne,
        _ => null,
    };

    /// <summary>Stops and lets go of the track. Callers hold the gate.</summary>
    private void Release()
    {
        if (_track is null)
        {
            return;
        }

        _track.Stop();
        _track.Release();
        _track.Dispose();
        _track = null;
        _sampleRate = 0;
    }
}
