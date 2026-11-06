namespace Eleris.TiffNet.Internal.Writing;

/// <summary>
/// Encodes the blocks of an image on several threads and hands them to the writer in order, so that the file does not depend on
/// the number of threads; at most 2 × the degree of parallelism blocks are encoded or waiting at once, which bounds the memory
/// of a write.
/// </summary>
internal static class BlockPipeline
{
    /// <summary>Encodes blocks 0 to <paramref name="count"/> - 1 and appends each, in order.</summary>
    /// <param name="count">The number of blocks.</param>
    /// <param name="degree">The threads that encode at once; 1 encodes on the calling thread.</param>
    /// <param name="encode">Encodes a block; called from several threads at once.</param>
    /// <param name="append">Writes a block; called on the calling thread, in the order of the blocks.</param>
    /// <param name="cancellationToken">Stops between blocks.</param>
    public static void Run(int count, int degree, Func<int, byte[]> encode, Action<int, byte[]> append, CancellationToken cancellationToken)
    {
        if (degree <= 1 || count <= 1)
        {
            for (int i = 0; i < count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                append(i, encode(i));
            }

            return;
        }

        int window = 2 * degree;
        var pending = new Task<byte[]>?[window];
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        using var workers = new SemaphoreSlim(degree);
        int next = 0;
        try
        {
            for (int i = 0; i < count; i++)
            {
                // Keep the window full: the blocks after this one are encoded while it waits to be written.
                while (next < count && next < i + window)
                {
                    int block = next++;
                    pending[block % window] = Task.Run(async () =>
                    {
                        await workers.WaitAsync(stop.Token).ConfigureAwait(false);
                        try
                        {
                            stop.Token.ThrowIfCancellationRequested();
                            return encode(block);
                        }
                        finally
                        {
                            workers.Release();
                        }
                    }, stop.Token);
                }

                byte[] data = pending[i % window]!.GetAwaiter().GetResult();
                pending[i % window] = null;
                append(i, data);
            }
        }
        catch
        {
            // Stop the blocks in flight and wait for them, so that none runs once the write has failed.
            stop.Cancel();
            foreach (var task in pending)
            {
                if (task is not null)
                {
                    try
                    {
                        task.Wait(CancellationToken.None);
                    }
                    catch (AggregateException)
                    {
                        // Their failure (or cancellation) is not the one to report.
                    }
                }
            }

            throw;
        }
    }
}
