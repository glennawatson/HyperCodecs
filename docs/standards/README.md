# Standards

The specifications each decoder is written from. The files live in the shared standards folder `docs/PdViewerLite/media` of the `glennawatson` checkout, which sits next to this repository (`../glennawatson/docs/PdViewerLite/media`). They are not copied here.

| Format | Specification | File in the standards folder |
|---|---|---|
| H.264 | ITU-T H.264 (2021, 2024 and 2026 editions) | `T-REC-H.264-202108-S.pdf`, `T-REC-H.264-202408-S.pdf`, `T-REC-H.264-202606-I.pdf` |
| H.265 | ITU-T H.265 (2026 edition) | `T-REC-H.265-202601-I.pdf` |
| Opus | RFC 6716, RFC 7845 (Ogg encapsulation), RFC 8251 (updates) | `rfc6716.txt`, `rfc7845.txt`, `rfc8251.txt` |
| Ogg | RFC 3533 | `rfc3533.txt` |
| Vorbis | Vorbis I specification | `Vorbis_I_spec.html`, `Vorbis_I_spec.pdf` |
| FLAC | RFC 9639 | `rfc9639.txt` |
| Matroska and WebM | RFC 8794 (EBML), RFC 9559 (Matroska) | `rfc8794.txt`, `rfc9559.txt` |
| MP4 and M4A | QuickTime file format, 2001 | `qtff-2001.pdf` |
| MP3 tags | ID3v2.4.0 structure and frames | `id3v2.4.0-structure.html`, `id3v2.4.0-frames.html` |

Not in the folder: ISO/IEC 14496-3 (AAC), ISO/IEC 14496-12 and 14496-15 (the MP4 file format and its `avcC` and `hvcC` records) and ISO/IEC 11172-3 (MP3). They are not freely published. The tests check the AAC, MP3 and MP4 code against files made by independent encoders.

The folder also holds specifications for formats this library does not decode (VP8 in `rfc6386.txt`, VP9, AV1 and WebVTT).
