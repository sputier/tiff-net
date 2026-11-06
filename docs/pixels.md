# Reading pixels

Pixels are delivered **top-down, left to right**, whatever the `Orientation` of the file, interleaved (`[y][x][c]`) or planar
(`[c][y][x]`, the order of a FITS cube). Palettes are expanded to 16-bit RGB (`ReadIndices` gives the indices themselves, for classification maps and label images), MinIsWhite is inverted, samples of 1 to 16 bits
are unpacked to bytes or 16-bit words with their values unchanged.

| Method | Gives |
| --- | --- |
| `ReadRawPixels<T>` | the stored values, of exactly their type (`Half` for 16-bit floating point) |
| `ReadPixels<T>(Stored)` | the stored values converted to `T`: `float` and `double` always, an integer type when every value fits |
| `ReadPixels<T>(Normalized)` | `float` or `double`: unsigned integers divided by the largest value of their depth (0 to 1), signed ones by their largest positive value, floating point unchanged |

<!-- example: PixelExamples.Conversions -->
```csharp
using var file = TiffFile.Open(path);
var image = file.Primary;                                            // u16 RGB

ushort[] raw = image.ReadRawPixels<ushort>();                         // exactly the stored type
int[] wider = image.ReadPixels<int>();                                // any type that holds every value
float[] unit = image.ReadPixels<float>(TiffPixelScale.Normalized);    // divided by 65535: 0 to 1
Console.WriteLine($"{raw[3]} = {wider[3]} = {unit[3] * 65535:F0}");

try
{
    _ = image.ReadPixels<byte>();                                      // 16-bit values do not fit in a byte
}
catch (InvalidOperationException e)
{
    Console.WriteLine($"Refused: {e.Message}");
}
```
<!-- /example -->

The library never guesses whether floating point data is in [0, 1] or in ADU: it gives what is stored. NaN, infinities and the
sign of zero are kept.

<!-- example: PixelExamples.RegionsAndChannels -->
```csharp
using var file = TiffFile.Open(path);
var image = file.Primary;

// Only the strips or tiles under the region are read.
float[] crop = image.ReadRegion<float>(x: 10, y: 5, width: 8, height: 4);

// One channel: for an image stored in planes, only its plane is read.
float[] green = image.ReadChannel<float>(1);

// [c][y][x], the order of a FITS cube (NAXIS1 = width, NAXIS2 = height, NAXIS3 = channels).
float[] cube = image.ReadPixels<float>(order: TiffPixelOrder.Planar);

Console.WriteLine($"region {crop.Length}, channel {green.Length}, cube {cube.Length} samples");
```
<!-- /example -->

<!-- example: PixelExamples.BuffersAndAsync -->
```csharp
using var file = TiffFile.Open(path);
var image = file.Primary;

// No array allocated for the pixels: the caller's buffer, decoded on every core (a Span<T> is filled on the calling thread).
var buffer = new double[image.Width * image.Height * image.Channels];
image.ReadPixels<double>(buffer);

// On a thread of the pool, cancellable between strips or tiles.
using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30));
float[] pixels = await image.ReadPixelsAsync<float>(cancellationToken: cancellation.Token);
Console.WriteLine($"{buffer.Length} and {pixels.Length} samples");
```
<!-- /example -->

`ReadPixelsAsync`, `ReadRegionAsync` and `ReadChannelAsync` read on the threads of the pool, can be cancelled between strips or
tiles, and report their progress (0 to 1, at most once a percent) to an `IProgress<double>`.

An array holds at most 2 147 483 591 elements, and a read is also bounded by `MaxPixelBytes` (2 147 483 591 bytes by
default, so 536 million `float` samples): raise it for a larger array, or read by region.
