namespace Eleris.TiffNet;

public sealed partial class TiffFile
{
    /// <summary>Adds a page at the end of the file.</summary>
    /// <param name="image">The page: created in memory, or read from this file or another one (which must stay open until saved).</param>
    /// <returns>This file, to chain the calls.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="image"/> is null.</exception>
    public TiffFile Add(TiffImage image)
    {
        ArgumentNullException.ThrowIfNull(image);
        _images.Add(image);
        return this;
    }

    /// <summary>Inserts a page.</summary>
    /// <param name="index">Where the page goes, 0 to <see cref="Count"/>.</param>
    /// <param name="image">The page.</param>
    /// <returns>This file, to chain the calls.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="image"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is not within 0 to <see cref="Count"/>.</exception>
    public TiffFile Insert(int index, TiffImage image)
    {
        ArgumentNullException.ThrowIfNull(image);
        _images.Insert(index, image);
        return this;
    }

    /// <summary>Removes a page.</summary>
    /// <param name="index">The index of the page.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is not the index of a page.</exception>
    public void RemoveAt(int index) => _images.RemoveAt(index);

    /// <summary>Removes a page.</summary>
    /// <param name="image">The page.</param>
    /// <returns>Whether the page was in the file.</returns>
    public bool Remove(TiffImage image) => _images.Remove(image);
}
