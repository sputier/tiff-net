using System.IO.Compression;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using Eleris.TiffNet.Internal.Codecs;
using Eleris.TiffNet.Internal.Jpeg;
using Eleris.TiffNet.Internal.Structure;

namespace Eleris.TiffNet.Internal.Pixels;

internal static partial class PixelReader
{
    /// <summary>
    /// The buffers of one worker, allocated when first needed, of the sizes the plan reserved, and reused for every unit the worker
    /// decodes. Their contents are never read before they are written.
    /// </summary>
    private sealed class Scratch<TD> where TD : unmanaged
    {
        private PixelPlan _plan;
        private byte[]? _read;
        private byte[]? _decoded;
        private TD[]? _row;
        private byte[]? _predictor;

        private Scratch(PixelPlan plan) => _plan = plan;

        public byte[] Read => _read = Fit(_read, _plan.ReadBytes);

        public byte[] Decoded => _decoded = Fit(_decoded, _plan.DecodedBytes);

        public TD[] Row => _row is { } row && row.Length >= _plan.RowSamples ? row : _row = GC.AllocateUninitializedArray<TD>((int)_plan.RowSamples);

        public byte[] Predictor => _predictor = Fit(_predictor, _plan.PredictorBytes);

        /// <summary>Buffers from the cache (large enough ones are reused for another plan of the same read or writer), or new ones.</summary>
        public static Scratch<TD> Take(ScratchCache cache, PixelPlan plan)
        {
            // Buffers of another sample type are left to the collector: a cache serves one image, of one type.
            if (cache.TryTake(out object? item) && item is Scratch<TD> scratch)
            {
                scratch._plan = plan;
                return scratch;
            }

            return new Scratch<TD>(plan);
        }

        private static byte[] Fit(byte[]? buffer, long size) =>
            buffer is not null && buffer.Length >= size ? buffer : GC.AllocateUninitializedArray<byte>((int)size);
    }
}
