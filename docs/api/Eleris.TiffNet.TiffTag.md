# TiffTag Class

**Namespace:** `Eleris.TiffNet`

**Inheritance:** `object` → `TiffTag`

One tag of a TIFF directory: its code, the type of its values and the values, independent of the byte order of the file.

```csharp
public sealed class TiffTag : IEquatable<TiffTag>
```

**Remarks**

Two tags are equal when their code, type and values are.

## Properties

| Member | Description |
| --- | --- |
| [`Code`](#eleris-tiffnet-tifftag-code) | The code of the tag, for example 270 for `ImageDescription`. |
| [`Count`](#eleris-tiffnet-tifftag-count) | The number of values (characters, NUL bytes included, for [`TiffTagType.Ascii`](Eleris.TiffNet.TiffTagType.md#eleris-tiffnet-tifftagtype-ascii)). |
| [`Type`](#eleris-tiffnet-tifftag-type) | How the values are stored. |

## Methods

| Member | Description |
| --- | --- |
| [`AsBytes()`](#eleris-tiffnet-tifftag-asbytes) | The values as bytes, little endian whatever the byte order of the file. |
| [`AsNumbers<T>()`](#eleris-tiffnet-tifftag-asnumbers-1) | The values as numbers of type `T`. |
| [`AsString()`](#eleris-tiffnet-tifftag-asstring) | The text of the tag: up to the first NUL byte, decoded as UTF-8 when it is valid UTF-8 (ASCII is), as Latin-1 otherwise. |
| [`AsStrings()`](#eleris-tiffnet-tifftag-asstrings) | The texts of a tag that holds several, separated by NUL bytes. |
| [`Equals(TiffTag)`](#eleris-tiffnet-tifftag-equals-eleris-tiffnet-tifftag) | Whether two tags have the same code, type and values. |
| [`Equals(object)`](#eleris-tiffnet-tifftag-equals-system-object) | Whether `obj` is a tag with the same code, type and values. |
| [`FromBytes(int, ReadOnlySpan<byte>, TiffTagType)`](#eleris-tiffnet-tifftag-frombytes-system-int32-system-readonlyspan-system-byte-eleris-tiffnet-tifftagtype) | A tag of bytes, as they are. |
| [`FromNumbers<T>(int, ReadOnlySpan<T>)`](#eleris-tiffnet-tifftag-fromnumbers-1-system-int32-system-readonlyspan-0) | A tag of numbers, whose type follows from `T` (for example [`TiffTagType.Short`](Eleris.TiffNet.TiffTagType.md#eleris-tiffnet-tifftagtype-short) for [`UInt16`](https://learn.microsoft.com/dotnet/api/system.uint16), [`TiffTagType.Double`](Eleris.TiffNet.TiffTagType.md#eleris-tiffnet-tifftagtype-double) for [`Double`](https://learn.microsoft.com/dotnet/api/system.double)). |
| [`FromString(int, string)`](#eleris-tiffnet-tifftag-fromstring-system-int32-system-string) | A text tag ([`TiffTagType.Ascii`](Eleris.TiffNet.TiffTagType.md#eleris-tiffnet-tifftagtype-ascii)), in ASCII when the text is, in UTF-8 otherwise, ended by a NUL byte. |
| [`GetHashCode()`](#eleris-tiffnet-tifftag-gethashcode) | A hash of the code, type and number of values. |
| [`ToString()`](#eleris-tiffnet-tifftag-tostring) | The code, type and count of the tag. |

## Property details

<a id="eleris-tiffnet-tifftag-code"></a>
### `Code`

The code of the tag, for example 270 for `ImageDescription`.

```csharp
public int Code { get; }
```

<a id="eleris-tiffnet-tifftag-count"></a>
### `Count`

The number of values (characters, NUL bytes included, for [`TiffTagType.Ascii`](Eleris.TiffNet.TiffTagType.md#eleris-tiffnet-tifftagtype-ascii)).

```csharp
public int Count { get; }
```

<a id="eleris-tiffnet-tifftag-type"></a>
### `Type`

How the values are stored.

```csharp
public TiffTagType Type { get; }
```

## Method details

<a id="eleris-tiffnet-tifftag-asbytes"></a>
### `AsBytes()`

The values as bytes, little endian whatever the byte order of the file.

```csharp
public ReadOnlyMemory<byte> AsBytes()
```

<a id="eleris-tiffnet-tifftag-asnumbers-1"></a>
### `AsNumbers<T>()`

The values as numbers of type `T`.

```csharp
public T[] AsNumbers<T>() where T : unmanaged, INumber<T>
```

**Type parameters**

- `T` — A numeric type that can hold every value: any for integers that fit, [`Single`](https://learn.microsoft.com/dotnet/api/system.single) or [`Double`](https://learn.microsoft.com/dotnet/api/system.double) for floating point values and rationals (numerator divided by denominator).

**Returns**

The values.

**Exceptions**

- [`InvalidOperationException`](https://learn.microsoft.com/dotnet/api/system.invalidoperationexception) — A value cannot be represented as `T`, or the tag holds text.

<a id="eleris-tiffnet-tifftag-asstring"></a>
### `AsString()`

The text of the tag: up to the first NUL byte, decoded as UTF-8 when it is valid UTF-8 (ASCII is), as Latin-1 otherwise.

```csharp
public string AsString()
```

**Exceptions**

- [`InvalidOperationException`](https://learn.microsoft.com/dotnet/api/system.invalidoperationexception) — The tag holds numbers.

<a id="eleris-tiffnet-tifftag-asstrings"></a>
### `AsStrings()`

The texts of a tag that holds several, separated by NUL bytes.

```csharp
public IReadOnlyList<string> AsStrings()
```

**Exceptions**

- [`InvalidOperationException`](https://learn.microsoft.com/dotnet/api/system.invalidoperationexception) — The tag holds numbers.

<a id="eleris-tiffnet-tifftag-equals-eleris-tiffnet-tifftag"></a>
### `Equals(TiffTag)`

Whether two tags have the same code, type and values.

```csharp
public bool Equals(TiffTag? other)
```

**Parameters**

- `other` — The other tag.

<a id="eleris-tiffnet-tifftag-equals-system-object"></a>
### `Equals(object)`

Whether `obj` is a tag with the same code, type and values.

```csharp
public override bool Equals(object? obj)
```

**Parameters**

- `obj` — The other object.

<a id="eleris-tiffnet-tifftag-frombytes-system-int32-system-readonlyspan-system-byte-eleris-tiffnet-tifftagtype"></a>
### `FromBytes(int, ReadOnlySpan<byte>, TiffTagType)`

A tag of bytes, as they are.

```csharp
public static TiffTag FromBytes(int code, ReadOnlySpan<byte> value, TiffTagType type = TiffTagType.Undefined)
```

**Parameters**

- `code` — The code of the tag.
- `value` — The bytes.
- `type` — A type of one-byte values: [`TiffTagType.Undefined`](Eleris.TiffNet.TiffTagType.md#eleris-tiffnet-tifftagtype-undefined), [`TiffTagType.Byte`](Eleris.TiffNet.TiffTagType.md#eleris-tiffnet-tifftagtype-byte), [`TiffTagType.SByte`](Eleris.TiffNet.TiffTagType.md#eleris-tiffnet-tifftagtype-sbyte) or [`TiffTagType.Ascii`](Eleris.TiffNet.TiffTagType.md#eleris-tiffnet-tifftagtype-ascii).

<a id="eleris-tiffnet-tifftag-fromnumbers-1-system-int32-system-readonlyspan-0"></a>
### `FromNumbers<T>(int, ReadOnlySpan<T>)`

A tag of numbers, whose type follows from `T` (for example [`TiffTagType.Short`](Eleris.TiffNet.TiffTagType.md#eleris-tiffnet-tifftagtype-short) for [`UInt16`](https://learn.microsoft.com/dotnet/api/system.uint16), [`TiffTagType.Double`](Eleris.TiffNet.TiffTagType.md#eleris-tiffnet-tifftagtype-double) for [`Double`](https://learn.microsoft.com/dotnet/api/system.double)).

```csharp
public static TiffTag FromNumbers<T>(int code, ReadOnlySpan<T> values) where T : unmanaged
```

**Type parameters**

- `T` — [`Byte`](https://learn.microsoft.com/dotnet/api/system.byte), [`SByte`](https://learn.microsoft.com/dotnet/api/system.sbyte), [`UInt16`](https://learn.microsoft.com/dotnet/api/system.uint16), [`Int16`](https://learn.microsoft.com/dotnet/api/system.int16), [`UInt32`](https://learn.microsoft.com/dotnet/api/system.uint32), [`Int32`](https://learn.microsoft.com/dotnet/api/system.int32), [`UInt64`](https://learn.microsoft.com/dotnet/api/system.uint64), [`Int64`](https://learn.microsoft.com/dotnet/api/system.int64), [`Single`](https://learn.microsoft.com/dotnet/api/system.single) or [`Double`](https://learn.microsoft.com/dotnet/api/system.double).

**Parameters**

- `code` — The code of the tag.
- `values` — The values.

<a id="eleris-tiffnet-tifftag-fromstring-system-int32-system-string"></a>
### `FromString(int, string)`

A text tag ([`TiffTagType.Ascii`](Eleris.TiffNet.TiffTagType.md#eleris-tiffnet-tifftagtype-ascii)), in ASCII when the text is, in UTF-8 otherwise, ended by a NUL byte.

```csharp
public static TiffTag FromString(int code, string value)
```

**Parameters**

- `code` — The code of the tag.
- `value` — The text.

<a id="eleris-tiffnet-tifftag-gethashcode"></a>
### `GetHashCode()`

A hash of the code, type and number of values.

```csharp
public override int GetHashCode()
```

<a id="eleris-tiffnet-tifftag-tostring"></a>
### `ToString()`

The code, type and count of the tag.

```csharp
public override string ToString()
```
