# TiffException Class

**Namespace:** `Eleris.TiffNet`

**Inheritance:** `object` → `Exception` → `TiffException`

Data that is not valid TIFF: a bad structure, truncated or damaged data, or a limit of the reading options exceeded.

```csharp
public sealed class TiffException : Exception
```

**Remarks**

A damaged or hostile file fails with this exception or with a [`NotSupportedException`](https://learn.microsoft.com/dotnet/api/system.notsupportedexception), never with an index, overflow or out-of-memory error. A failure of the caller's own file or stream is not a `TiffException`: its exception is rethrown as it is.

## Constructors

| Member | Description |
| --- | --- |
| [`TiffException()`](#eleris-tiffnet-tiffexception-ctor) | Creates an exception for invalid data. |
| [`TiffException(string)`](#eleris-tiffnet-tiffexception-ctor-system-string) | Creates an exception for invalid data. |
| [`TiffException(string, Exception)`](#eleris-tiffnet-tiffexception-ctor-system-string-system-exception) | Creates an exception for invalid data revealed by another error. |
| [`TiffException(string, string)`](#eleris-tiffnet-tiffexception-ctor-system-string-system-string) | Creates an exception for a limit of the reading options that the data would exceed. |

## Properties

| Member | Description |
| --- | --- |
| [`Limit`](#eleris-tiffnet-tiffexception-limit) | The name of the limit of the reading options that the data would exceed, for example `MaxPixelBytes`, or `null` when the data is damaged rather than too large. |

## Constructor details

<a id="eleris-tiffnet-tiffexception-ctor"></a>
### `TiffException()`

Creates an exception for invalid data.

```csharp
public TiffException()
```

<a id="eleris-tiffnet-tiffexception-ctor-system-string"></a>
### `TiffException(string)`

Creates an exception for invalid data.

```csharp
public TiffException(string message)
```

**Parameters**

- `message` — What is wrong, naming the file by its file name only.

<a id="eleris-tiffnet-tiffexception-ctor-system-string-system-exception"></a>
### `TiffException(string, Exception)`

Creates an exception for invalid data revealed by another error.

```csharp
public TiffException(string message, Exception innerException)
```

**Parameters**

- `message` — What is wrong, naming the file by its file name only.
- `innerException` — The error that revealed it.

<a id="eleris-tiffnet-tiffexception-ctor-system-string-system-string"></a>
### `TiffException(string, string)`

Creates an exception for a limit of the reading options that the data would exceed.

```csharp
public TiffException(string message, string limit)
```

**Parameters**

- `message` — What is wrong, naming the file by its file name only.
- `limit` — The name of the limit, for example `MaxPixelBytes`.

## Property details

<a id="eleris-tiffnet-tiffexception-limit"></a>
### `Limit`

The name of the limit of the reading options that the data would exceed, for example `MaxPixelBytes`, or `null` when the data is damaged rather than too large.

```csharp
public string? Limit { get; }
```

**Remarks**

Lets an application tell "too large for this device" from "damaged".
