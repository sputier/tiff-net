# TiffExtraChannel Record

**Namespace:** `Eleris.TiffNet`

**Inheritance:** `object` → `TiffExtraChannel`

An extra channel of an image, after its colour channels.

```csharp
public sealed record TiffExtraChannel : IEquatable<TiffExtraChannel>
```

## Constructors

| Member | Description |
| --- | --- |
| [`TiffExtraChannel(TiffExtraChannelKind)`](#eleris-tiffnet-tiffextrachannel-ctor-eleris-tiffnet-tiffextrachannelkind) | An extra channel of an image, after its colour channels. |

## Properties

| Member | Description |
| --- | --- |
| [`Kind`](#eleris-tiffnet-tiffextrachannel-kind) | What the channel holds. |

## Constructor details

<a id="eleris-tiffnet-tiffextrachannel-ctor-eleris-tiffnet-tiffextrachannelkind"></a>
### `TiffExtraChannel(TiffExtraChannelKind)`

An extra channel of an image, after its colour channels.

```csharp
public TiffExtraChannel(TiffExtraChannelKind Kind)
```

**Parameters**

- `Kind` — What the channel holds.

## Property details

<a id="eleris-tiffnet-tiffextrachannel-kind"></a>
### `Kind`

What the channel holds.

```csharp
public TiffExtraChannelKind Kind { get; init; }
```
