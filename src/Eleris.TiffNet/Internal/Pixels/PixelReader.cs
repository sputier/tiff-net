using System.Collections.Concurrent;
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
/// Reads the pixels of an image along a checked <see cref="PixelPlan"/>: decodes its units in parallel into pooled buffers, and
/// puts each sample in its place in the destination (orientation, region, order, channel, conversion), straight from the stored rows.
/// </summary>
/// <remarks>
/// Uncompressed strips are read in chunks of rows, never whole, so that an image stored in one strip costs its destination and a
/// few buffers, not twice its size. The writes of the workers are disjoint: the result does not depend on the number of threads.
/// </remarks>
internal static partial class PixelReader
{
    public static void Read<TD, TOut>(PixelPlan plan, TiffSource source, ResourceBudget budget, int channels, bool littleEndian,
        Span<TOut> destination, Memory<TOut>? memory, ScratchCache? cache = null)
        where TD : unmanaged, INumber<TD>
        where TOut : unmanaged, INumber<TOut>
    {
        // The buffers of the workers come from a cache: the read's own, or the caller's (the writer reads band after band with
        // the same buffers). Parallel.ForEach starts new tasks as it goes; each takes buffers from the cache and puts them back,
        // so that there are never more than one set per worker running at once (as reserved by the plan).
        cache ??= new ScratchCache();
        var context = new Context<TD, TOut>(plan, source, budget, channels, littleEndian);
        if (memory is not { } array || plan.Workers == 1)
        {
            var scratch = Scratch<TD>.Take(cache, plan);
            try
            {
                foreach (var unit in plan.Units())
                {
                    budget.ThrowIfCancelled();
                    context.Process(unit, destination, scratch);
                    plan.Progress?.Add();
                }
            }
            finally
            {
                cache.Put(scratch);
            }

            return;
        }

        try
        {
            // One unit at a time: the default partitioner hands out chunks that grow, and the last worker is left with several
            // units while the others wait. A unit is a block or more (0.3 ms and up): taking each under a lock costs nothing.
            Parallel.ForEach(Partitioner.Create(plan.Units(), EnumerablePartitionerOptions.NoBuffering), new ParallelOptions { MaxDegreeOfParallelism = plan.Workers }, () => Scratch<TD>.Take(cache, plan),
                (unit, _, scratch) =>
                {
                    budget.ThrowIfCancelled();
                    context.Process(unit, array.Span, scratch);
                    plan.Progress?.Add();
                    return scratch;
                },
                cache.Put);
        }
        catch (AggregateException ex) when (ex.InnerExceptions.Count > 0)
        {
            // The first failure, as it was thrown (a TiffException, the caller's IOException, a cancellation).
            ExceptionDispatchInfo.Throw(ex.InnerExceptions[0]);
        }
    }
}
