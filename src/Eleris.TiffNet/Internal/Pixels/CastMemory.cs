using System.Buffers;
using System.Runtime.InteropServices;

namespace Eleris.TiffNet.Internal.Pixels;

/// <summary>Bytes seen as memory of another unmanaged type, without copying them (a band of the writer read by the parallel reader).</summary>
internal sealed class CastMemory<T>(Memory<byte> bytes) : MemoryManager<T> where T : unmanaged
{
    public override Span<T> GetSpan() => MemoryMarshal.Cast<byte, T>(bytes.Span);

    public override MemoryHandle Pin(int elementIndex = 0) => throw new NotSupportedException("Not pinned.");

    public override void Unpin()
    {
    }

    protected override void Dispose(bool disposing)
    {
    }
}
