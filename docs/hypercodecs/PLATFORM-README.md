# HyperCodecs.Platform

Operating system sound output and hardware video decoding for [HyperCodecs](https://github.com/glennawatson/HyperCodecs). The managed decoders in `HyperCodecs` need no native library. This package adds the parts that talk to the system.

Every part loads its system library at run time. When a library or a device is missing, the part says so and the managed decoders do the work.

## Sound output

`IStreamingAudioOutput` plays a continuous stream of 32-bit float samples and reports its latency, so a player can keep the picture in step with what is heard.

| System | Class | Library |
|---|---|---|
| Windows | `WasapiStreamingAudioOutput` | WASAPI |
| macOS | `AudioQueueStreamingOutput` | Audio Toolbox |
| Linux | `PulseStreamingAudioOutput` | PulseAudio, also provided by PipeWire |

## Hardware video decoding

`PlatformVideoDecoders.Create` returns a decoder for a picture track. It uses the system decoder when the picture is large enough to pay for it, and the managed decoder otherwise.

| System | API | Codings |
|---|---|---|
| Windows | Media Foundation | H.264, H.265 |
| macOS | VideoToolbox | H.264, H.265 |
| Linux | VA-API | H.264, H.265 |
