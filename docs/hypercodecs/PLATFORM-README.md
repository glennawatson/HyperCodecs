# HyperCodecs.Platform

Operating system sound output and hardware video decoding for [HyperCodecs](https://github.com/glennawatson/HyperCodecs). The managed decoders in `HyperCodecs` need no native library. This package adds the parts that talk to the system.

The package has one build for each system, and each build holds only that system's code. NuGet picks the build that matches your target framework.

| Your target framework | Sound output | Hardware video (H.264, H.265) |
|---|---|---|
| `net10.0-windows10.0.19041.0`, `net11.0-windows10.0.19041.0` | WASAPI | Media Foundation |
| `net10.0-ios`, `-tvos`, `-macos`, `-maccatalyst` and the `net11.0` ones | Audio Toolbox | VideoToolbox |
| `net10.0-android36.0`, `net11.0-android37.0` | AudioTrack | MediaCodec (hardware decoders only) |
| `net10.0`, `net11.0` (Linux) | PulseAudio, also provided by PipeWire | VA-API |

A Windows or macOS app that targets plain `net10.0` gets the Linux build, which finds nothing to use on those systems. Target the Windows or Apple framework to get the system code.

## Use

```csharp
using HyperCodecs.Platform.Audio;
using HyperCodecs.Platform.Video;

// A decoder for a picture track: the system's when the picture is large enough to pay for it, otherwise the managed one.
var decoder = PlatformVideoDecoders.Create(track);

// A sound output for the running system, or null when this build has none.
using var output = PlatformAudioOutputs.Create();
```

`IStreamingAudioOutput` plays interleaved 32-bit float samples and reports its latency, so a player can keep the picture in step with what is heard. A missing library or device makes `Start` return `false`, and the managed decoders stay as the fallback.
