namespace Eleris.TiffNet.Internal;

/// <summary>
/// The progress of a long operation (the units of a read, the blocks of a save), counted from several threads and reported to the
/// application as a fraction of 0 to 1: at most once a percent, in increasing order, and 1 only by <see cref="Done"/>, once the
/// operation has delivered its result (the array returned, the file in place).
/// </summary>
/// <remarks>
/// The application's <see cref="IProgress{T}"/> is called under a lock, from the thread that crossed a percent (a thread of the
/// pool); an exception it throws fails the operation.
/// </remarks>
internal sealed class ProgressCounter(IProgress<double> progress, long total)
{
    private readonly Lock _gate = new();
    private long _done;
    private int _reported = -1;

    /// <summary>Counts <paramref name="count"/> more steps done; reports up to 99 % (100 % is for <see cref="Done"/>).</summary>
    public void Add(long count = 1)
    {
        long done = Interlocked.Add(ref _done, count);
        int percent = total <= 0 ? 99 : (int)Math.Min(99, done * 100 / total);
        Report(percent);
    }

    /// <summary>The operation has delivered its result: 100 %.</summary>
    public void Done() => Report(100);

    private void Report(int percent)
    {
        if (percent <= Volatile.Read(ref _reported))
        {
            return;
        }

        lock (_gate)
        {
            // The claim and the report together: a percent is never reported after a larger one.
            if (percent <= _reported)
            {
                return;
            }

            _reported = percent;
            progress.Report(percent / 100.0);
        }
    }

    /// <summary>A counter for <paramref name="progress"/>, or none.</summary>
    public static ProgressCounter? Of(IProgress<double>? progress, long total) => progress is null ? null : new(progress, total);
}
