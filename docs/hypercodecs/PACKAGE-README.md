# HyperCodecs

Sound and video containers and decoders written in managed C# for .NET 10 and .NET 11. No native library is loaded. Everything reads and writes spans, works on Linux, Windows and macOS, and is trim and Native AOT safe.

HyperCodecs is part of the [Hyper PDF Viewer](https://github.com/glennawatson/HyperPDF) family and lives at [glennawatson/HyperCodecs](https://github.com/glennawatson/HyperCodecs).

## Supported formats

| Kind | Formats |
|---|---|
| Containers | MP4 and M4A (including fragmented files), Matroska and WebM, Ogg, WAVE, FLAC files, raw ADTS AAC and MP3 |
| Sound | AAC-LC, MP3, FLAC, Opus, Vorbis, PCM |
| Video | H.264, H.265 |

## Decode sound

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

        // Read pcm.Samples here, then clear the buffer for the next packet.
        pcm.Clear();
    }
}
```

`PcmBuffer.Samples` holds interleaved 32-bit float samples between -1 and 1. `decoder.SampleRate` and `decoder.Channels` describe them.

## Decode video

`VideoDecoders.Create` returns a decoder for an H.264 or H.265 track. `Decode` adds finished pictures to a `List<VideoFrame>` in display order, and `Flush` adds the pictures still held back at the end. Give a frame back with `Recycle` when you are done with it so the decoder can reuse its planes.
