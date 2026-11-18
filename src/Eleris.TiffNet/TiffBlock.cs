namespace Eleris.TiffNet;

/// <summary>
/// Where one strip or tile of an image is stored in its file: the position and the size of its bytes, still compressed as the
/// file holds them. See <see cref="TiffImage.Blocks"/>.
/// </summary>
/// <param name="Offset">The position of the first byte of the block, from the start of the TIFF data; within the file.</param>
/// <param name="ByteCount">The number of bytes stored for the block; the block ends within the file.</param>
public readonly record struct TiffBlock(long Offset, long ByteCount);
