using System.Runtime.ExceptionServices;

namespace Eleris.TiffNet.Internal.Writing;

/// <summary>
/// Encodes the blocks of an image on several threads and hands them to the writer in order, so that the file does not depend on
/// the number of threads; at most 2 × the degree of parallelism blocks are encoded or waiting at once, which bounds the memory
/// of a write.
/// </summary>
/// <remarks>
/// The calling thread encodes too, and never waits for a block that no thread is encoding: when the thread pool is busy (many
/// saves at once on a server), the write goes on, at the pace of the calling thread, instead of waiting for pool threads.
/// Helpers are started on the pool only while there is a block to take, and stop as soon as there is none.
/// </remarks>
internal static class BlockPipeline
{
    private const byte Empty = 0, Encoding = 1, Done = 2;

    /// <summary>Encodes blocks 0 to <paramref name="count"/> - 1 and appends each, in order.</summary>
    /// <param name="count">The number of blocks.</param>
    /// <param name="degree">The threads that encode at once, the calling one included; 1 encodes on the calling thread only.</param>
    /// <param name="encode">Encodes a block; called from several threads at once.</param>
    /// <param name="append">Writes a block; called on the calling thread, in the order of the blocks.</param>
    /// <param name="discard">Releases a block encoded but not appended (when the write fails).</param>
    /// <param name="cancellationToken">Stops between blocks.</param>
    public static void Run<T>(int count, int degree, Func<int, T> encode, Action<int, T> append, Action<T> discard,
        CancellationToken cancellationToken)
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

        new Pipeline<T>(count, degree, encode, discard).Run(append, cancellationToken);
    }

    private sealed class Pipeline<T>(int count, int degree, Func<int, T> encode, Action<T> discard)
    {
        private readonly object _sync = new();
        private readonly int _window = 2 * degree;
        private readonly T[] _slots = new T[2 * degree];
        private readonly byte[] _states = new byte[2 * degree];
        private int _claimed;
        private int _appended;
        private int _active;
        private int _helpers;
        private ExceptionDispatchInfo? _failure;

        public void Run(Action<int, T> append, CancellationToken cancellationToken)
        {
            try
            {
                for (int i = 0; i < count; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    StartHelpers();
                    T value = Next(i);
                    append(i, value);
                    Appended();
                }
            }
            catch (Exception ex)
            {
                lock (_sync)
                {
                    _failure ??= ExceptionDispatchInfo.Capture(ex);
                }

                Stop();
                throw;
            }
        }

        /// <summary>Block <paramref name="index"/>: taken when ready, encoded here when no thread has taken it, waited for otherwise.</summary>
        private T Next(int index)
        {
            int slot = index % _window;
            while (true)
            {
                int mine;
                lock (_sync)
                {
                    _failure?.Throw();
                    if (_states[slot] == Done && _claimed > index)
                    {
                        T value = _slots[slot];
                        _slots[slot] = default!;
                        _states[slot] = Empty;
                        return value;
                    }

                    if (!TryClaim(out mine))
                    {
                        // The block is being encoded by a helper: wait for it (or for a failure).
                        Monitor.Wait(_sync);
                        continue;
                    }
                }

                // A block nobody encodes yet (this one, or one after it): encode it here rather than wait.
                Encode(mine);
            }
        }

        /// <summary>
        /// A block has been appended: its place in the window is free. It is counted only now, not when the writer takes the block,
        /// so that the block the writer is holding is inside the window and at most 2 × the degree blocks are alive at once.
        /// </summary>
        private void Appended()
        {
            lock (_sync)
            {
                _appended++;
                Monitor.PulseAll(_sync);
            }
        }

        /// <summary>Takes the next block to encode, if the window allows it. Called under the lock.</summary>
        private bool TryClaim(out int index)
        {
            if (_failure is null && _claimed < count && _claimed < _appended + _window)
            {
                index = _claimed++;
                _states[index % _window] = Encoding;
                _active++;
                return true;
            }

            index = -1;
            return false;
        }

        private void Encode(int index)
        {
            T value = default!;
            Exception? error = null;
            try
            {
                value = encode(index);
            }
            catch (Exception ex)
            {
                error = ex;
            }

            lock (_sync)
            {
                _active--;
                if (error is not null)
                {
                    _failure ??= ExceptionDispatchInfo.Capture(error);
                }
                else if (_failure is not null)
                {
                    discard(value);
                    _states[index % _window] = Empty;
                }
                else
                {
                    _slots[index % _window] = value;
                    _states[index % _window] = Done;
                }

                Monitor.PulseAll(_sync);
            }
        }

        /// <summary>Starts helpers on the pool while there is work for them, up to the degree (the calling thread being one).</summary>
        private void StartHelpers()
        {
            int start;
            lock (_sync)
            {
                int available = Math.Min(count, _appended + _window) - _claimed;
                start = Math.Min(degree - 1 - _helpers, available);
                if (start <= 0)
                {
                    return;
                }

                _helpers += start;
            }

            for (int i = 0; i < start; i++)
            {
                ThreadPool.UnsafeQueueUserWorkItem(static pipeline => pipeline.Help(), this, preferLocal: false);
            }
        }

        /// <summary>A helper: encodes blocks while there are some to take, then stops (it never waits).</summary>
        private void Help()
        {
            while (true)
            {
                int index;
                lock (_sync)
                {
                    if (!TryClaim(out index))
                    {
                        _helpers--;
                        return;
                    }
                }

                Encode(index);
            }
        }

        /// <summary>After a failure: waits for the blocks being encoded, then releases every block not appended.</summary>
        private void Stop()
        {
            lock (_sync)
            {
                while (_active > 0)
                {
                    Monitor.Wait(_sync);
                }

                for (int s = 0; s < _window; s++)
                {
                    if (_states[s] == Done)
                    {
                        discard(_slots[s]);
                        _slots[s] = default!;
                        _states[s] = Empty;
                    }
                }
            }
        }
    }
}
