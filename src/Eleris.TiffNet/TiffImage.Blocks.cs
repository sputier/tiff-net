using System.Collections;

namespace Eleris.TiffNet;

public sealed partial class TiffImage
{
    private IReadOnlyList<TiffBlock>? _blocks;

    /// <summary>
    /// The strips or tiles of the image as the file stores them, in file order (<see cref="TiffStorage.Layout"/> tells which): the
    /// order of the <c>StripOffsets</c> or <c>TileOffsets</c> table, plane after plane when the image is planar.
    /// </summary>
    /// <remarks>
    /// Valid for every image of a file, including one the library cannot decode (<see cref="UnsupportedReason"/>): nothing is
    /// decoded. The offsets and byte counts are those the library itself reads with, after its checks: a block that starts outside
    /// the file is refused when the file is opened, and a last block cut by a truncated file is shortened to the end of the file
    /// (refused with <see cref="TiffReadOptions.Strict"/>). The list is a view of tables the image already holds: nothing is
    /// copied, and nothing is done until it is first used.
    /// <para>
    /// An image created in memory (<see cref="Create{T}"/>, or whose pixels were replaced by <see cref="SetPixels{T}"/>) stores
    /// nothing in a file: its list is empty.
    /// </para>
    /// </remarks>
    public IReadOnlyList<TiffBlock> Blocks => InMemory ? [] : _blocks ??= new BlockList(_descriptor.Offsets, _descriptor.ByteCounts);

    /// <summary>
    /// Copies the stored bytes of a strip or tile, still compressed, without decoding them.
    /// </summary>
    /// <param name="index">The position of the block in <see cref="Blocks"/>.</param>
    /// <param name="destination">Receives the bytes; at least <see cref="TiffBlock.ByteCount"/> long.</param>
    /// <returns>The number of bytes copied: the byte count of the block.</returns>
    /// <remarks>Allocates nothing, and can be called from several threads at once, whatever the source of the file.</remarks>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is not the position of a block.</exception>
    /// <exception cref="ArgumentException"><paramref name="destination"/> is shorter than the block.</exception>
    /// <exception cref="InvalidOperationException">The image was created in memory: it has no stored blocks.</exception>
    /// <exception cref="IOException">The source no longer holds the bytes (a file or stream that shrank since the file was opened).</exception>
    /// <exception cref="ObjectDisposedException">The file is disposed.</exception>
    public int ReadBlock(int index, Span<byte> destination)
    {
        var (source, block) = BlockOf(index, destination.Length);
        int count = (int)block.ByteCount;
        source.ReadExactly(block.Offset, destination[..count]);
        return count;
    }

    /// <summary>
    /// Copies the stored bytes of a strip or tile, still compressed, asynchronously (see <see cref="ReadBlock"/>).
    /// </summary>
    /// <param name="index">The position of the block in <see cref="Blocks"/>.</param>
    /// <param name="destination">Receives the bytes; at least <see cref="TiffBlock.ByteCount"/> long, and left alone by the caller
    /// until the task ends.</param>
    /// <param name="cancellationToken">Cancels the read before it starts.</param>
    /// <returns>The number of bytes copied. Every failure is reported by the task, never thrown by the call.</returns>
    /// <remarks>A file held in memory completes at once; a file or a stream is read on a pool thread.</remarks>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is not the position of a block.</exception>
    /// <exception cref="ArgumentException"><paramref name="destination"/> is shorter than the block.</exception>
    /// <exception cref="InvalidOperationException">The image was created in memory: it has no stored blocks.</exception>
    /// <exception cref="IOException">The source no longer holds the bytes.</exception>
    /// <exception cref="ObjectDisposedException">The file is disposed.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    public ValueTask<int> ReadBlockAsync(int index, Memory<byte> destination, CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return ValueTask.FromCanceled<int>(cancellationToken);
        }

        try
        {
            var (source, _) = BlockOf(index, destination.Length);
            return source.Memory is not null
                ? new ValueTask<int>(ReadBlock(index, destination.Span))
                : new ValueTask<int>(Task.Run(() => ReadBlock(index, destination.Span), cancellationToken));
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or ObjectDisposedException or IOException)
        {
            return ValueTask.FromException<int>(e);
        }
    }

    /// <summary>
    /// The stored bytes of a strip or tile as a view of the memory the file is held in, without a copy.
    /// </summary>
    /// <param name="index">The position of the block in <see cref="Blocks"/>.</param>
    /// <returns>The bytes of the block, or <see langword="null"/> when the file is read on demand from a file or a stream (use
    /// <see cref="ReadBlock"/>). The view stays valid as long as the memory it points into (also after the file is disposed): it must not be written to.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is not the position of a block.</exception>
    /// <exception cref="InvalidOperationException">The image was created in memory: it has no stored blocks.</exception>
    public ReadOnlyMemory<byte>? TryGetBlockMemory(int index)
    {
        var (source, block) = BlockOf(index, long.MaxValue);
        return source.Memory is { } memory ? memory.Slice((int)block.Offset, (int)block.ByteCount) : default(ReadOnlyMemory<byte>?);
    }

    /// <summary>The block at <paramref name="index"/> and the source of its bytes; <paramref name="destination"/> is the length of the buffer meant for it.</summary>
    private (Internal.TiffSource Source, TiffBlock Block) BlockOf(int index, long destination)
    {
        if (InMemory || File is not { } file || Storage is null)
        {
            throw new InvalidOperationException("The image was created in memory: it has no stored blocks.");
        }

        var offsets = _descriptor.Offsets;
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, offsets.Length);
        var block = new TiffBlock(offsets[index], _descriptor.ByteCounts[index]);
        if (destination < block.ByteCount)
        {
            throw new ArgumentException($"Block {index} holds {block.ByteCount} bytes; the destination holds {destination}.", nameof(destination));
        }

        return (file.Source, block);
    }

    /// <summary>A view of the two tables of the descriptor, without a copy.</summary>
    private sealed class BlockList(long[] offsets, long[] counts) : IReadOnlyList<TiffBlock>
    {
        public int Count => offsets.Length;

        public TiffBlock this[int index] => (uint)index < (uint)offsets.Length
            ? new TiffBlock(offsets[index], counts[index])
            : throw new ArgumentOutOfRangeException(nameof(index));

        public IEnumerator<TiffBlock> GetEnumerator()
        {
            for (int i = 0; i < offsets.Length; i++)
            {
                yield return new TiffBlock(offsets[i], counts[i]);
            }
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
