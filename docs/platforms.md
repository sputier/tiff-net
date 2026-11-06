# Platforms

The library targets `net10.0`, in managed code (Deflate is that of the runtime), without dependency: it runs wherever .NET 10
runs on a little-endian processor (x64, ARM64; a big-endian one gets a `PlatformNotSupportedException`). Three uses are
checked.

| Platform | How it is checked |
| --- | --- |
| Windows and Linux, x64 | the whole test suite, on every change (continuous integration) |
| NativeAOT, Windows and Linux | `samples/Eleris.TiffNet.AotSample` is published with NativeAOT, warnings as errors, and run: it writes and reads back every compression and layout (continuous integration, job `aot`) |
| .NET MAUI, Android | `samples/Eleris.TiffNet.MauiSample` is built for Android on every change (job `maui`); it is run on an emulator by hand (below) |

The library is trimmable and AOT-compatible (`IsTrimmable`, `IsAotCompatible`): reflection is not used, and the analyzers
of trimming and AOT run on every build. On processors without AVX2 (ARM included), the JPEG decoder uses its inverse DCT on
128-bit vectors (SSE2 or AdvSimd), which gives the same samples bit for bit.

## Android, by hand

The continuous integration builds the Android application but does not start an emulator (too slow and unreliable on the
hosted runners). The check, last run on 2026-11-06 on a Pixel 7 emulator (Android 16, API 36, x86_64):

```text
adb logcat -c
dotnet build samples/Eleris.TiffNet.MauiSample -f net10.0-android -t:Run
adb logcat -d | grep TIFFNET-CHECK
```

At start, the application writes a 640 x 480 16-bit image with each compression into its data folder, reads it back, and
logs one line per compression; all read back `ok`:

```text
TIFFNET-CHECK None: ok (600 KB, 1470 ms)
TIFFNET-CHECK PackBits: ok (604 KB, 60 ms)
TIFFNET-CHECK Lzw: ok (821 KB, 61 ms)
TIFFNET-CHECK Deflate: ok (600 KB, 183 ms)
```

(The first line includes the start of the runtime and the JIT.) The same check runs when the button of the page is pressed.
