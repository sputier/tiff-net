# TiffBlock Struct

**Namespace:** `Eleris.TiffNet`

**Inheritance:** `object` → `ValueType` → `TiffBlock`

Where one strip or tile of an image is stored in its file: the position and the size of its bytes, still compressed as the file holds them. See [`TiffImage.Blocks`](Eleris.TiffNet.TiffImage.md#eleris-tiffnet-tiffimage-blocks).

```csharp
public readonly struct TiffBlock : IEquatable<TiffBlock>
```

## Constructors

| Member | Description |
| --- | --- |
| [`TiffBlock(long, long)`](#eleris-tiffnet-tiffblock-ctor-system-int64-system-int64) | Where one strip or tile of an image is stored in its file: the position and the size of its bytes, still compressed as the file holds them. |

## Properties

| Member | Description |
| --- | --- |
| [`ByteCount`](#eleris-tiffnet-tiffblock-bytecount) | The number of bytes stored for the block; the block ends within the file. |
| [`Offset`](#eleris-tiffnet-tiffblock-offset) | The position of the first byte of the block, from the start of the TIFF data; within the file. |

## Constructor details

<a id="eleris-tiffnet-tiffblock-ctor-system-int64-system-int64"></a>
### `TiffBlock(long, long)`

Where one strip or tile of an image is stored in its file: the position and the size of its bytes, still compressed as the file holds them. See [`TiffImage.Blocks`](Eleris.TiffNet.TiffImage.md#eleris-tiffnet-tiffimage-blocks).

```csharp
public TiffBlock(long Offset, long ByteCount)
```

**Parameters**

- `Offset` — The position of the first byte of the block, from the start of the TIFF data; within the file.
- `ByteCount` — The number of bytes stored for the block; the block ends within the file.

## Property details

<a id="eleris-tiffnet-tiffblock-bytecount"></a>
### `ByteCount`

The number of bytes stored for the block; the block ends within the file.

```csharp
public long ByteCount { get; init; }
```

<a id="eleris-tiffnet-tiffblock-offset"></a>
### `Offset`

The position of the first byte of the block, from the start of the TIFF data; within the file.

```csharp
public long Offset { get; init; }
```
