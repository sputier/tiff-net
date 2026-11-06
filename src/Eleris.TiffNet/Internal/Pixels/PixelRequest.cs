using System.IO.Compression;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using Eleris.TiffNet.Internal.Codecs;
using Eleris.TiffNet.Internal.Jpeg;
using Eleris.TiffNet.Internal.Structure;

namespace Eleris.TiffNet.Internal.Pixels;

/// <summary>What a read asks for: a region of the delivered (oriented) image, its channels, their order and scale.</summary>
internal readonly record struct PixelRequest(int X, int Y, int Width, int Height, int? Channel, TiffPixelOrder Order, TiffPixelScale Scale)
{
    /// <summary>The number of samples the read delivers; more than <see cref="long.MaxValue"/> saturates rather than wraps.</summary>
    public long Count(int channels)
    {
        Int128 count = (Int128)Width * Height * (Channel is null ? channels : 1);
        return count > long.MaxValue ? long.MaxValue : (long)count;
    }
}
