# HyperCodecs.Platform.Linux

PulseAudio (also provided by PipeWire) sound output and VA-API hardware video decoding for [HyperCodecs](https://github.com/glennawatson/HyperCodecs) on Linux. Linux has no platform target framework, so it has its own package. Windows, Apple and Android code is in [HyperCodecs.Platform](https://www.nuget.org/packages/HyperCodecs.Platform).

```csharp
using HyperCodecs.Platform.Audio;
using HyperCodecs.Platform.Video;

// The hardware decoder when the picture is large enough to pay for it, otherwise the managed one.
var decoder = LinuxVideoDecoders.Create(track);

// A sound output, or null when the system is not Linux.
using var output = LinuxAudioOutputs.Create();
```

A missing library or device makes `Start` return `false` or `LinuxVideoDecoders.TryCreate` give a reason, and the managed decoders stay as the fallback.
