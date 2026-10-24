# HyperCodecs

Sound and video containers and decoders written in managed C# for .NET 10 and .NET 11. No native library is loaded. Everything reads and writes spans, runs on Linux, Windows and macOS, and is safe to trim and compile ahead of time (Native AOT).

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

## Build and test

The repository needs the .NET 11 SDK. Everything builds for `net10.0` and `net11.0`.

```console
cd src
dotnet build HyperCodecs.slnx
dotnet test --solution HyperCodecs.slnx
```

Benchmarks run with BenchmarkDotNet: `dotnet run -c Release -f net11.0 --project src/benchmarks/HyperCodecs.Benchmarks -- --filter '*Opus*'`. Contributor notes are in [CLAUDE.md](CLAUDE.md).

## Licence

MIT. See [LICENSE](LICENSE).
