using System.IO.Compression;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using Eleris.TiffNet.Internal.Codecs;
using Eleris.TiffNet.Internal.Jpeg;
using Eleris.TiffNet.Internal.Structure;

namespace Eleris.TiffNet.Internal.Pixels;

/// <summary>The rectangle of the stored image that holds a region of the delivered one: [X0, X1) x [Y0, Y1).</summary>
internal readonly record struct StoredRect(long X0, long Y0, long X1, long Y1)
{
    /// <summary>The stored rectangle under a region of the delivered image (its corners mapped back).</summary>
    public static StoredRect Of(ImageDescriptor d, PixelRequest q)
    {
        long w = d.Width, h = d.Height;
        (long, long) Stored(long vx, long vy) => d.Orientation switch
        {
            2 => (w - 1 - vx, vy),
            3 => (w - 1 - vx, h - 1 - vy),
            4 => (vx, h - 1 - vy),
            5 => (vy, vx),
            6 => (vy, h - 1 - vx),
            7 => (w - 1 - vy, h - 1 - vx),
            8 => (w - 1 - vy, vx),
            _ => (vx, vy),
        };
        var (ax, ay) = Stored(q.X, q.Y);
        var (bx, by) = Stored(q.X + q.Width - 1, q.Y + q.Height - 1);
        return new StoredRect(Math.Min(ax, bx), Math.Min(ay, by), Math.Max(ax, bx) + 1, Math.Max(ay, by) + 1);
    }
}
