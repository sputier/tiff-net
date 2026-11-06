using System.IO.Compression;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using Eleris.TiffNet.Internal.Codecs;
using Eleris.TiffNet.Internal.Jpeg;
using Eleris.TiffNet.Internal.Structure;

namespace Eleris.TiffNet.Internal.Pixels;

/// <summary>Buffers of the workers of reads, kept between the reads of one caller (the bands of a write).</summary>
internal sealed class ScratchCache
{
    private readonly System.Collections.Concurrent.ConcurrentBag<object> _items = [];

    public bool TryTake(out object? item) => _items.TryTake(out item);

    public void Put(object item) => _items.Add(item);
}
