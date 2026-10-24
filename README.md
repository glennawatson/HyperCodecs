# HyperCodecs

Sound and video containers and decoders written in managed C# for .NET 10 and .NET 11. The `HyperCodecs` package loads no native library. Everything reads and writes spans, runs on Linux, Windows and macOS, and is safe to trim and compile ahead of time (Native AOT).

[![Build](https://github.com/glennawatson/HyperCodecs/actions/workflows/ci.yml/badge.svg)](https://github.com/glennawatson/HyperCodecs/actions/workflows/ci.yml)

## Install

```console
dotnet add package HyperCodecs
```

## Supported formats

| Kind | Formats |
|---|---|
| Containers | MP4 and M4A (including fragmented files), Matroska and WebM, Ogg, WAVE, FLAC files, raw ADTS AAC and MP3 |
| Sound | AAC-LC, MP3, FLAC, Opus, Vorbis, PCM |
| Video | H.264 (CAVLC and CABAC, intra, predicted and B pictures, deblocking), H.265 |

The standards each decoder follows are listed in [docs/standards](docs/standards/README.md).

## Use

Open a file, read its packets and give them to a decoder.

```csharp
using HyperCodecs;

using var container = MediaContainers.Open("song.m4a") ?? throw new InvalidDataException("Unsupported file.");
var index = container.FindTrack(MediaTrackKind.Audio);
using var decoder = AudioDecoders.Create(container.Tracks[index]) ?? throw new InvalidDataException("Unsupported codec.");

var pcm = new PcmBuffer();
var packet = new MediaPacket();
while (container.ReadPacket(packet))
{
    if (packet.Track == index)
    {
        decoder.Decode(packet, pcm);

        // Read pcm.Samples here (interleaved floats between -1 and 1), then clear the buffer for the next packet.
        pcm.Clear();
    }
}
```

Video works the same way with `VideoDecoders.Create` and a `List<VideoFrame>` for the output. Give frames back with `VideoDecoder.Recycle` so the decoder can reuse their planes.

The managed H.264 and H.265 decoders use one thread by default. Set `VideoDecoder.MaxThreads` to let them decode several pictures at once, each picture waiting only for the rows of the pictures it refers to. Pictures are the same whatever the number, but each comes back a few calls to `Decode` later, so call `Flush` at the end of the stream. A decoder keeps its threads until it is disposed.

## Sound output and hardware video

Two optional packages talk to the operating system. A missing library or device is reported and the managed decoders stay as the fallback.

```console
dotnet add package HyperCodecs.Platform
dotnet add package HyperCodecs.Platform.Linux
```

`HyperCodecs.Platform` has one build for each target framework, and each build holds only that system's code. Linux has no target framework of its own, so it has its own package.

| Package and target framework | Sound output (`IStreamingAudioOutput`) | Hardware video |
|---|---|---|
| `HyperCodecs.Platform` `net10.0-windows10.0.19041.0`, `net11.0-windows10.0.19041.0` | WASAPI | Media Foundation |
| `HyperCodecs.Platform` `-ios`, `-tvos`, `-macos`, `-maccatalyst` | Audio Toolbox | VideoToolbox |
| `HyperCodecs.Platform` `net10.0-android36.0`, `net11.0-android37.0` | AudioTrack | MediaCodec |
| `HyperCodecs.Platform.Linux` `net10.0`, `net11.0` | PulseAudio or PipeWire (`LinuxAudioOutputs.Create`) | VA-API (`LinuxVideoDecoders.Create`) |

Hardware decoders cover H.264 and H.265. `PlatformVideoDecoders.Create` and `LinuxVideoDecoders.Create` use the hardware first for large pictures and the managed decoder first for small ones. The plain `net10.0` and `net11.0` builds of `HyperCodecs.Platform` have no system code.

## Build and test

The repository needs the .NET 11 SDK. The core library, tests and benchmarks build for `net10.0` and `net11.0`. `HyperCodecs.Platform` also builds for Windows and Android, and for Apple systems on macOS and Windows. Android and Apple builds need their workloads (`dotnet workload restore`).

```console
cd src
dotnet build HyperCodecs.slnx
dotnet test --solution HyperCodecs.slnx
```

Benchmarks run with BenchmarkDotNet: `dotnet run -c Release -f net11.0 --project src/benchmarks/HyperCodecs.Benchmarks -- --filter '*Opus*'`. Contributor notes are in [CLAUDE.md](CLAUDE.md).

## Licence

MIT. See [LICENSE](LICENSE).
