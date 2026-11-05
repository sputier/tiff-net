# TiffResolution Record

**Namespace:** `Eleris.TiffNet`

**Inheritance:** `object` → `TiffResolution`

The resolution of an image: pixels per unit, horizontally and vertically.

```csharp
public sealed record TiffResolution : IEquatable<TiffResolution>
```

## Constructors

| Member | Description |
| --- | --- |
| [`TiffResolution(double, double, TiffResolutionUnit)`](#eleris-tiffnet-tiffresolution-ctor-system-double-system-double-eleris-tiffnet-tiffresolutionunit) | The resolution of an image: pixels per unit, horizontally and vertically. |

## Properties

| Member | Description |
| --- | --- |
| [`Unit`](#eleris-tiffnet-tiffresolution-unit) | The unit. |
| [`X`](#eleris-tiffnet-tiffresolution-x) | Pixels per unit along a row. |
| [`Y`](#eleris-tiffnet-tiffresolution-y) | Pixels per unit along a column. |

## Constructor details

<a id="eleris-tiffnet-tiffresolution-ctor-system-double-system-double-eleris-tiffnet-tiffresolutionunit"></a>
### `TiffResolution(double, double, TiffResolutionUnit)`

The resolution of an image: pixels per unit, horizontally and vertically.

```csharp
public TiffResolution(double X, double Y, TiffResolutionUnit Unit)
```

**Parameters**

- `X` — Pixels per unit along a row.
- `Y` — Pixels per unit along a column.
- `Unit` — The unit.

## Property details

<a id="eleris-tiffnet-tiffresolution-unit"></a>
### `Unit`

The unit.

```csharp
public TiffResolutionUnit Unit { get; init; }
```

<a id="eleris-tiffnet-tiffresolution-x"></a>
### `X`

Pixels per unit along a row.

```csharp
public double X { get; init; }
```

<a id="eleris-tiffnet-tiffresolution-y"></a>
### `Y`

Pixels per unit along a column.

```csharp
public double Y { get; init; }
```
