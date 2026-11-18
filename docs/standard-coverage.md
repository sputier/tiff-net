# Coverage of the standard

What the library reads and writes of TIFF, and what it refuses. **R** and **W** mean implemented **and tested**: every row is
exercised by the tests listed at the end of this page.

References: TIFF Revision 6.0 (Adobe, 1992); TIFF Technical Note 3 (floating point predictor, Adobe 2005); Adobe PageMaker 6.0
TIFF Technical Notes (SubIFDs); BigTIFF (libtiff design, 2004–2007); TIFF Technical Note 2 (JPEG in TIFF, 1995); ITU-T T.81
(JPEG); Astro-TIFF 1.0 (2022); EXIF 2.32 (tag structure only).

Legend: **R** read, **W** write, — not supported (`NotSupportedException` with a message that names the feature).

## Overview

| Part of the specification | Read | Write |
| --- | --- | --- |
| Header and directories, both byte orders (`II`, `MM`) | R | W (little endian) |
| BigTIFF (version 43, 64-bit offsets), both byte orders | R | W (when needed or asked) |
| Multi-page files (chain of directories) | R | W |
| Images larger than one array (mosaics), by region | R | W (`TiffImageWriter`) |
| Sub-directories `SubIFDs` (330) | R | — |
| EXIF and GPS sub-directories | R | — (dropped on writing) |
| Grey (`MinIsBlack`), RGB | R | W |
| Grey `MinIsWhite` | R (inverted to `MinIsBlack` values) | — |
| Palette | R (expanded to RGB 16-bit) | — |
| Samples of 1 to 16 bits other than 8 and 16 (bilevel, 2, 4, 12, 14 bits…) | R (unpacked to 8 or 16 bits) | — |
| Extra samples (data, associated and unassociated alpha) | R | W |
| Strips and tiles | R | W |
| Interleaved and separate planes (`PlanarConfiguration` 1 and 2) | R | W |
| Samples: unsigned and signed integers of 8, 16, 32, 64 bits; floating point of 16, 32, 64 bits | R | W |
| Compression: none, PackBits, LZW, Deflate (8 and 32946) | R | W |
| Compression: JPEG (7, Technical Note 2), baseline and progressive, 8-bit | R | — |
| Predictors: horizontal (2) for integers, floating point (3, Technical Note 3) | R | W |
| Orientation (1 to 8) | R (applied) | W (always 1) |
| Common metadata tags, ICC, XMP, IPTC, unknown tags | R | W |

## Structure

- Header: byte order `II` or `MM`, version 42 (classic) or 43 (BigTIFF, offset size 8). Anything else is a `TiffException`.
- Directories: entries in any order (the specification asks for ascending order; files that do not follow it are accepted,
  and refused with `Strict`), all the field types of TIFF 6.0 (`BYTE` to `DOUBLE`, `IFD`) and of BigTIFF (`LONG8`, `SLONG8`,
  `IFD8`). Unknown types are skipped. Duplicate tags: the first wins (refused with `Strict`).
- Chains: loops in the chain of directories or between sub-directories are detected and end the chain (a `TiffException` with
  `Strict`). The number of directories is bounded by `MaxDirectories`.
- Values: every count and offset is checked against the size of the file before anything is allocated. A tag whose values do
  not fit in the file is a `TiffException` for the structural tags, and is dropped (refused with `Strict`) for the others.
- Writing: classic TIFF, little endian, directories after the image data, tags in ascending order, values word-aligned.
  BigTIFF when an offset would exceed 4 GB (`TiffBigTiff.Auto`) or when asked.

## Images

- Required tags: `ImageWidth`, `ImageLength`, `StripOffsets`/`TileOffsets`, and `StripByteCounts`/`TileByteCounts` (a missing
  byte count is computed for uncompressed data, as libtiff does, refused with `Strict`). A missing `BitsPerSample` is 1, as the
  specification says; a missing `PhotometricInterpretation` is grey or RGB from the samples (refused with `Strict`).
- `SamplesPerPixel` at most 65 535 (the field is 16-bit); `BitsPerSample` the same for all samples (otherwise
  `NotSupportedException`).
- `NewSubfileType`: bits 0 (reduced resolution) and 2 (transparency mask) are exposed and decide which image is
  `TiffFile.Primary`. `SubfileType` (the old tag) is mapped onto it.
- Planar files: each plane has its own strips or tiles; they are assembled into the requested pixel order without an
  intermediate copy of the whole image.
- Tiles: width and length multiples of 16 (the specification; refused with `Strict` only); edge tiles are cropped.
- Orientation: the 8 values are applied on reading, including the transpositions (5 to 8), so that pixels are always delivered
  top-down, left to right.

## Samples

| `SampleFormat` | `BitsPerSample` | `TiffSampleType` | Notes |
| --- | --- | --- | --- |
| 1 (unsigned) | 8, 16, 32, 64 | `UInt8` … `UInt64` | |
| 2 (signed) | 8, 16, 32, 64 | `Int8` … `Int64` | |
| 3 (IEEE float) | 16, 32, 64 | `Float16`, `Float32`, `Float64` | `Float16` as `System.Half`; `ReadPixels<float>` converts |
| 4 (undefined) | 8, 16, 32, 64 | as unsigned | as libtiff |
| 1 (unsigned), 4 | 1 to 7 | `UInt8` | unpacked, values unchanged; `Storage.BitsPerSample` keeps the depth |
| 1 (unsigned), 4 | 9 to 15 | `UInt16` | unpacked (12 and 14-bit camera data), values unchanged |
| 2 (signed) | other than 8, 16, 32, 64 | — | `NotSupportedException` |
| 3 | 24 | — | `NotSupportedException` |
| 5, 6 (complex) | any | — | `NotSupportedException` |

`MinIsWhite` grey images, at every depth, are read with their values inverted (`max − value` for unsigned integers, where
`max` is `2^bits − 1` of the stored depth, so 1 − value for a bilevel mask; `−value` for signed integers and floating point),
so that they behave like `MinIsBlack`; `TiffStorage` keeps what the file said.

## Colour

| `PhotometricInterpretation` | Read | Write |
| --- | --- | --- |
| 0 `MinIsWhite` | R (see above) | — |
| 1 `MinIsBlack` | R | W |
| 2 `RGB` | R | W |
| 6 `YCbCr` | R with JPEG compression only, delivered as RGB | — |
| 3 palette | R, expanded to RGB `UInt16` from the `ColorMap` | — |
| 4 mask, 5 separated (CMYK), 8/9/10 Lab, 32803 CFA, 34892 LinearRaw, LogL/LogLuv | — | — |

CFA and LinearRaw (DNG raw data) are refused with a message that points to a raw decoder. Their description (size, CFA tags, storage, the code 32803 as `RawPhotometricCode`) stays readable: see [images the library cannot decode](unsupported-images.md).

## Compression

| Code | Name | Read | Write | Notes |
| --- | --- | --- | --- | --- |
| 1 | none | R | W | |
| 32773 | PackBits | R | W | Expands noisy 16 and 32-bit data: documented |
| 5 | LZW | R | W | Including the "old-style" bit order of libtiff before 1990 on reading |
| 8, 32946 | Deflate (zlib) | R | W | `System.IO.Compression`; level from `TiffCompressionLevel` |
| 7 | JPEG (Technical Note 2) | R | — | Baseline and progressive Huffman, 8-bit samples, 1 or 3 components, YCbCr (with subsampling 1×1, 2×1, 2×2, 4×1, 4×2, 4×4… as declared), RGB, grey; `JPEGTables` abbreviated streams; restart markers. Bounded by `MaxJpegScans` and `MaxPixelBytes` |
| 6 | old-style JPEG | — | — | Deprecated since 1995, not supported ([decision 1](design/architecture.md#decisions)) |
| 2, 3, 4 | CCITT (fax) | — | — | Not supported ([decision 4](design/architecture.md#decisions)); a bilevel image is read when uncompressed, LZW, Deflate or PackBits |
| others (ZSTD 50000, LZMA 34925, WebP 50001, JPEG XL 50002, JPEG 2000 34712…) | — | — | — | Recognised and named in the error message |

A JPEG image is delivered as libtiff and tifffile deliver it: each strip or tile is decoded by libjpeg's rules (the islow
integer IDCT, "fancy" upsampling, the integer YCbCr to RGB tables; the library gives exactly the samples of libjpeg-turbo), its
frame must be as wide as the strip or tile and cover its rows (a last strip may be higher, as some writers make it). YCbCr is
converted to RGB with the coefficients of JPEG (as libjpeg does): `ReferenceBlackWhite` and `YCbCrCoefficients` are left in the
metadata, not applied, as libtiff and tifffile do with JPEG. `PhotometricInterpretation = RGB` gives the components as they are.
A strip whose data runs out decodes to grey for the rest of its blocks, as libjpeg does.

JPEG is not written: it is lossy, and a TIFF meant for processing should not be. A JPEG image that is read and saved again is
written with `TiffWriteOptions.From(storage)`, which maps JPEG to Deflate.

Arithmetic-coded JPEG, 12-bit and lossless JPEG (ITU-T T.81 processes other than baseline/progressive Huffman, 8-bit) are
`NotSupportedException`.

## Predictors

| `Predictor` | Read | Write |
| --- | --- | --- |
| 1 none | R | W |
| 2 horizontal differencing, 8 to 64-bit samples (floating point too, on their bits, as libtiff reads them) | R | W (`Auto` for integers, except 64-bit on writing where the gain is nil) |
| 3 floating point (Technical Note 3), 16, 32, 64-bit | R | W (`Auto` for floating point) |

As libtiff does, a predictor is undone after LZW and Deflate only: with no compression or PackBits, the tag is ignored.

## Metadata

| Tag | Name | Exposed as | Read | Write |
| --- | --- | --- | --- | --- |
| 269 | DocumentName | `DocumentName` | R | W |
| 270 | ImageDescription | `Description` (Astro-TIFF FITS header) | R | W |
| 285 | PageName | `PageName` | R | W |
| 282, 283, 296 | X/YResolution, ResolutionUnit | `Resolution` | R | W |
| 305 | Software | `Software` | R | W |
| 306 | DateTime | `DateTime` | R | W |
| 315 | Artist | `Artist` | R | W |
| 316 | HostComputer | `HostComputer` | R | W |
| 33432 | Copyright | `Copyright` | R | W |
| 340, 341 | SMin/SMaxSampleValue | `SampleRange` | R | W |
| 34675 | ICC profile | `IccProfile` (bytes) | R | W |
| 700 | XMP | `Xmp` (bytes) | R | W |
| 33723 | IPTC | `Iptc` (bytes) | R | W |
| 34665, 34853 | EXIF, GPS sub-directories | `Exif`, `Gps` (tag lists) | R | — |
| other | | `TiffMetadata[code]` | R | W, except offset-bearing tags |

Tags not written back: the structure, which the library computes (254, 256 to 259, 262, 266, 273, 274, 277 to 279, 284, 292, 293,
317, 320, 322 to 325, 338, 339, 347, 512 to 521, 529 to 532, 32997, 32998), and the tags that hold offsets into the file, which
would point nowhere in a new file: `FreeOffsets` and `FreeByteCounts` (288, 289), `SubIFDs` (330), the EXIF, GPS and
Interoperability sub-directories (34665, 34853, 40965), and every tag of type `IFD` or `IFD8`. Sub-images (`SubIFDs`) are read
only in this version: a saved file holds its pages.

Text values: ASCII as the specification says, and UTF-8 accepted on reading (common in practice); multiple strings in one tag
(NUL-separated) are kept. Writing: ASCII when possible, UTF-8 otherwise, without BOM.

## Astro-TIFF

The library reads and writes the FITS header of an Astro-TIFF file as the text of `ImageDescription`, unchanged (lines of up to
80 characters, LF or CRLF). It does not interpret it: converting between FITS and TIFF (ignoring `SIMPLE`, `BITPIX`, `NAXISn`,
`BZERO`, `BSCALE`, `END`; flipping rows, since Astro-TIFF stores the last FITS row first; adapting `WCS` and `BAYERPAT`) is the job
of the bridge with FitsNet, and is documented there.

## Limits

| Limit | Value |
| --- | --- |
| Pixels read in one array | 2 147 483 591 elements, within `MaxPixelBytes` (2 147 483 591 bytes by default); larger images by region |
| Image written | one array per image with `TiffImage`; any size with `TiffImageWriter` |
| File | 2⁶³ bytes (BigTIFF) |
| Width, height | 2³¹ − 1 read and written (the fields allow 2³² − 1: larger images are listed as unsupported) |
| Channels | 65 535 |
| Configurable | see `TiffReadOptions` in the [API](design/api.md#options) |

## Not planned

Writing JPEG, CMYK, Lab, palette and low bit depths; the fax compressions (CCITT); ZSTD/LZMA/WebP/JPEG XL/JPEG 2000 codecs, GeoTIFF interpretation
(its tags are kept as unknown tags), DNG raw decoding, Photoshop layers (tag 37724 is kept as an unknown tag; the composite image
is what is read), writing EXIF/GPS sub-directories.

## How it is tested

| Part | Tests |
| --- | --- |
| Every sample type × 1 to 4 channels (extra channels of each kind) × none, PackBits, LZW, Deflate, ZSTD and LZMA (refused) × predictors × strips and tiles × interleaved and planar × classic and BigTIFF × both byte orders, written by tifffile (about 6 000 files) | read with exactly the samples of tifffile (SHA-256), or refused by design |
| JPEG: grey, YCbCr 4:4:4, 4:2:2, 4:2:0, progressive, restart intervals, 16-bit tables, JPEGTables, strips and tiles (Pillow, libjpeg-turbo, tifffile) | the samples of libjpeg-turbo, bit for bit |
| Siril 1.4.4 (Astro-TIFF header, ICC, Deflate), Pillow and tifffile files: palettes, 1 to 14-bit samples, MinIsWhite, the 8 orientations, pyramids, multi-page, metadata | samples and metadata as tifffile reads them; Astro-TIFF header and ICC kept byte for byte when saved again |
| Every combination the library writes (11 sample types × 1 to 4 channels × 4 compressions × predictors × layouts × planar × BigTIFF: 1 848 files) | read back by tifffile with the same samples, and by Pillow for the types it supports |
| A file beyond 4 GB written with TiffImageWriter | read back by region, sample by sample (dedicated CI job) |
| 35 hostile files (the audit of the first implementation, crafted attacks) and continuous fuzzing of the structure, the codecs and JPEG | the expected failure within bounds of time and memory; nothing but `TiffException` or `NotSupportedException` |
