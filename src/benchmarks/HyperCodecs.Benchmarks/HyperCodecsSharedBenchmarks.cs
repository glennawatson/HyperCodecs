// Copyright (c) 2026 Glenn Watson. All rights reserved.
// Glenn Watson licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Security.Cryptography;
using BenchmarkDotNet.Attributes;
using HyperCodecs.Audio;
using HyperCodecs.Checksums;
using HyperCodecs.Dsp;
using HyperCodecs.Video;

namespace HyperCodecs.Benchmarks;

/// <summary>Measures the pieces the decoders share: resampling, mixing, colour conversion, scaling, checksums and the inverse MDCT.</summary>
public class HyperCodecsSharedBenchmarks
{
    /// <summary>The input rate of the resampler.</summary>
    private const int InputRate = 44_100;

    /// <summary>The output rate of the resampler.</summary>
    private const int OutputRate = 48_000;

    /// <summary>The channels of the resampled sound.</summary>
    private const int StereoChannels = 2;

    /// <summary>The channels of the mixed sound.</summary>
    private const int SurroundChannels = 6;

    /// <summary>The frames of sound in each audio run.</summary>
    private const int AudioFrames = 4_096;

    /// <summary>The picture width.</summary>
    private const int Width = 1_280;

    /// <summary>The picture height.</summary>
    private const int Height = 720;

    /// <summary>The width of the scaled picture.</summary>
    private const int ScaledWidth = 640;

    /// <summary>The height of the scaled picture.</summary>
    private const int ScaledHeight = 360;

    /// <summary>The bytes in a pixel of the converted picture.</summary>
    private const int BgraBytes = 4;

    /// <summary>The bytes the checksums read.</summary>
    private const int ChecksumBytes = 65_536;

    /// <summary>The coefficients in a block of the inverse MDCT.</summary>
    private const int MdctSize = 1_024;

    /// <summary>The factor between a plane and its subsampled chroma, and between coefficients and samples.</summary>
    private const int ChromaScale = 2;

    /// <summary>The middle of a byte's range, which maps a random byte to a sample between -1 and 1.</summary>
    private const float ByteMidpoint = 127.5F;

    /// <summary>The resampler.</summary>
    private AudioResampler? _resampler;

    /// <summary>The resampled sound.</summary>
    private PcmBuffer? _resampled;

    /// <summary>The input sound for the resampler.</summary>
    private float[]? _stereo;

    /// <summary>The surround sound to mix.</summary>
    private float[]? _surround;

    /// <summary>The left channel of the mix.</summary>
    private float[]? _left;

    /// <summary>The right channel of the mix.</summary>
    private float[]? _right;

    /// <summary>The picture to convert.</summary>
    private VideoFrame? _frame;

    /// <summary>The converted picture.</summary>
    private byte[]? _bgra;

    /// <summary>The scaled picture.</summary>
    private byte[]? _scaled;

    /// <summary>The scaler.</summary>
    private VideoScaler? _scaler;

    /// <summary>The interleaved chroma of an NV12 picture.</summary>
    private byte[]? _pairs;

    /// <summary>The blue chroma plane of the NV12 picture.</summary>
    private byte[]? _cb;

    /// <summary>The red chroma plane of the NV12 picture.</summary>
    private byte[]? _cr;

    /// <summary>The bytes the checksums read.</summary>
    private byte[]? _bytes;

    /// <summary>The inverse MDCT.</summary>
    private InverseMdct? _mdct;

    /// <summary>The coefficients of the inverse MDCT.</summary>
    private float[]? _spectrum;

    /// <summary>The samples the inverse MDCT makes.</summary>
    private float[]? _samples;

    /// <summary>Makes the data.</summary>
    [GlobalSetup]
    public void Setup()
    {
        _resampler = new(InputRate, StereoChannels, OutputRate);
        _resampled = new();
        _stereo = Floats(AudioFrames * StereoChannels);
        _surround = Floats(AudioFrames * SurroundChannels);
        _left = new float[AudioFrames];
        _right = new float[AudioFrames];
        _frame = new(Width, Height, 0);
        RandomNumberGenerator.Fill(_frame.Y);
        RandomNumberGenerator.Fill(_frame.U);
        RandomNumberGenerator.Fill(_frame.V);
        _bgra = new byte[Width * Height * BgraBytes];
        _scaler = new(ScaledWidth, ScaledHeight);
        _scaled = new byte[_scaler.ByteCount];
        _pairs = new byte[Width * (Height / ChromaScale)];
        RandomNumberGenerator.Fill(_pairs);
        _cb = new byte[_pairs.Length / ChromaScale];
        _cr = new byte[_pairs.Length / ChromaScale];
        _bytes = new byte[ChecksumBytes];
        RandomNumberGenerator.Fill(_bytes);
        _mdct = new(MdctSize);
        _spectrum = Floats(MdctSize);
        _samples = new float[MdctSize * ChromaScale];
    }

    /// <summary>Resamples 44.1 kHz stereo sound to 48 kHz.</summary>
    /// <returns>The samples made.</returns>
    [Benchmark]
    public int Resample()
    {
        _resampler!.Reset();
        _resampled!.Clear();
        _resampler.Process(_stereo!, _resampled);
        return _resampled.Count;
    }

    /// <summary>Mixes 5.1 sound down to stereo.</summary>
    /// <returns>The first left sample.</returns>
    [Benchmark]
    public float MixSurround()
    {
        ChannelMixer.Mix(_surround!, SurroundChannels, _left!, _right!);
        return _left![0];
    }

    /// <summary>Converts a 720p picture to BGRA.</summary>
    /// <returns>The first byte.</returns>
    [Benchmark]
    public byte ConvertToBgra()
    {
        YuvConverter.ToBgra(_frame!, ColorMatrix.Bt709Limited, _bgra!);
        return _bgra![0];
    }

    /// <summary>Scales a 720p picture to 360p and converts it to BGRA.</summary>
    /// <returns>The first byte.</returns>
    [Benchmark]
    public byte ScalePicture()
    {
        _scaler!.Scale(_frame!, _scaled!);
        return _scaled![0];
    }

    /// <summary>Splits the interleaved chroma of a 720p NV12 picture into two planes.</summary>
    /// <returns>The first blue sample.</returns>
    [Benchmark]
    public byte DeinterleaveChroma()
    {
        Nv12Planes.Deinterleave(_pairs!, _cb!, _cr!);
        return _cb![0];
    }

    /// <summary>Runs CRC-8 over 64 KiB.</summary>
    /// <returns>The checksum.</returns>
    [Benchmark]
    public byte Crc8Block() => Crc8.Compute(_bytes!);

    /// <summary>Runs CRC-16 over 64 KiB.</summary>
    /// <returns>The checksum.</returns>
    [Benchmark]
    public ushort Crc16Block() => Crc16.Compute(_bytes!);

    /// <summary>Runs one inverse MDCT of 1024 coefficients.</summary>
    /// <returns>The first sample.</returns>
    [Benchmark]
    public float InverseTransform()
    {
        _mdct!.Inverse(_spectrum!, _samples!);
        return _samples![0];
    }

    /// <summary>Makes random samples between -1 and 1.</summary>
    /// <param name="count">The number of samples.</param>
    /// <returns>The samples.</returns>
    private static float[] Floats(int count)
    {
        var bytes = RandomNumberGenerator.GetBytes(count);
        var samples = new float[count];
        for (var index = 0; index < samples.Length; index++)
        {
            samples[index] = (bytes[index] - ByteMidpoint) / ByteMidpoint;
        }

        return samples;
    }
}
