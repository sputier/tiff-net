namespace Eleris.TiffNet.Internal.Writing;

/// <summary>Encodes the blocks of an image and hands them to the writer in order.</summary>
internal static class BlockPipeline
{
    /// <summary>Encodes blocks 0 to <paramref name="count"/> - 1 and appends each, in order.</summary>
    public static void Run(int count, int degree, Func<int, byte[]> encode, Action<int, byte[]> append, CancellationToken cancellationToken)
    {
        _ = degree;
        for (int i = 0; i < count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            append(i, encode(i));
        }
    }
}
