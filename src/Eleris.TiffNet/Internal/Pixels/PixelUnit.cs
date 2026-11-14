using System.IO.Compression;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using Eleris.TiffNet.Internal.Codecs;
using Eleris.TiffNet.Internal.Jpeg;
using Eleris.TiffNet.Internal.Structure;

namespace Eleris.TiffNet.Internal.Pixels;

/// <summary>
/// One piece of work: rows [FirstRow, FirstRow + Rows) of one strip or tile (of one plane), at (X0, Y0) in the stored image; with
/// a <paramref name="Group"/> of more than 1, the small compressed strips that follow it in the file too (the rows the read takes
/// from each), read in one call.
/// </summary>
internal readonly record struct PixelUnit(int Block, int Plane, long X0, long Y0, int FirstRow, int Rows, int Group = 1);
