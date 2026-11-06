namespace Eleris.TiffNet.Internal;

/// <summary>
/// The progress of a long operation (the units of a read, the blocks of a save), counted from several threads and reported to the
/// application as a fraction of 0 to 1, at most once a percent.
/// </summary>
internal sealed class ProgressCounter(IProgress<double> progress, long total)
{
    private long _done;
    private int _reported = -1;

    /// <summary>Counts <paramref name="count"/> more steps done.</summary>
    public void Add(long count = 1)
    {
        long done = Interlocked.Add(ref _done, count);
        int percent = total <= 0 ? 100 : (int)Math.Min(100, done * 100 / total);
        int reported = Volatile.Read(ref _reported);
        if (percent > reported && Interlocked.CompareExchange(ref _reported, percent, reported) == reported)
        {
            progress.Report(percent / 100.0);
        }
    }

    /// <summary>A counter for <paramref name="progress"/>, or none.</summary>
    public static ProgressCounter? Of(IProgress<double>? progress, long total) => progress is null ? null : new(progress, total);
}
