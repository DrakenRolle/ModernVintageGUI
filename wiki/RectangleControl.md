# RectangleControl Class

**Namespace:** `IS2Mod.ControlTypes`
**Assembly:** `ModernVintageGUI.dll`

A box - background fill, per-side borders, rounded corners and a Gaussian blur on the border area -
and the general purpose container, since any control can hold children.

```csharp
public class RectangleControl : UIControl, IScrollable
```

**Inheritance:** `Object` → [UIControl](UIControl) → **RectangleControl**

**Implements:** [`IS2Mod.Interfaces.IScrollable`](Supporting-Types#iscrollable-interface),
`INotifyPropertyChanged` (inherited)

**Derived:** [InventoryGridControl](InventoryGridControl)

## Remarks

This is the container to reach for. It stacks its children along
[`InsideOrientation`](UIControl#box-and-layout), clips them at its own edge on request, and grows a
vanilla styled scrollbar on either axis.

The blur reads the pixel buffer directly, so a control tree always has to be drawn at the origin of
its own surface. Compositing several trees onto a shared canvas with a context transform smears the
blur across the neighbours.

### Clipping and scrolling

Switching a bar on switches `ClipsChildren` on with it, because scrolling without clipping just
draws the content over whatever surrounds the container. Switching the last bar off again does
**not** switch clipping off: it may have been wanted on its own.

The children live in the **viewport** rather than in the whole padding box, so the strip a bar
occupies is not theirs to be stretched into. The container still measures its content in full -
that measurement is what tells it whether there is anything to scroll - so a scrolling container
needs a size of its own. An auto-sizing one grows to fit its content and there is nothing left
to scroll.

## Constructors

| | Description |
|---|---|
| `RectangleControl(int borderWidth = 1, int roundedCorners = 0, ElementColor? borderColor = null, ElementColor? backgroundColor = null, SurfacePattern? pattern = null, RectangleBorderStyle[]? hiddenBorders = null, double blurRange = 0, int blurEdgeWidth = 0, string _Name = "", PointD? _Size = null, Orientation _Orientation = Orientation.Top, double _Margin = 0, double _Padding = 0, int _Index = 0)` | Both colours default to `ElementColor.Transparent`. |
| `RectangleControl()` | Border width `1`, transparent colours, and `Padding = BorderWidth`. |

## Properties

### Appearance

| Name | Type | Description |
|---|---|---|
| `BackgroundColor` | [`ElementColor`](Supporting-Types#elementcolor-class) | The fill. |
| `BorderColor` | [`ElementColor`](Supporting-Types#elementcolor-class) | The stroke. |
| `BorderWidth` | `int` | Stroke width in author units. |
| `RoundedCorners` | `int` | Corner radius in author units. `0` draws square borders. |
| `HiddenBorders` | [`RectangleBorderStyle[]`](Supporting-Types#rectangleborderstyle-enum) | Sides to leave out. |
| `Pattern` | `SurfacePattern?` | A Cairo pattern to fill with instead of a flat colour. |
| `BlurRange` | `double` | Gaussian blur radius over the border area. |
| `BlurEdgeWidth` | `int` | How wide a strip is blurred. Together with `BlurRange` this is how the embossed look is made. |

### Scrolling - [`IScrollable`](Supporting-Types#iscrollable-interface)

| Name | Type | Description |
|---|---|---|
| `EnableVerticalScrollbar` | `bool` | Allow scrolling up and down, and show a bar on the right when needed. Switches `ClipsChildren` on. |
| `EnableHorizontalScrollbar` | `bool` | The same for left and right. |
| `ScrollOffset` | `PointD` | How far the content is currently shifted, device pixels, never negative. Read-only. |
| `MaxScrollOffset` | `PointD` | The furthest it can be shifted: content minus viewport. Both zero means everything fits. Read-only. |
| `ContentSize` | `PointD` | The full size of the content in device pixels. Read-only; the same as `MeasuredContentSize`. |
| `ViewportSize` | `PointD` | The visible area: the content box minus what the visible bars reserve. Read-only. |

## Methods

| Name | Returns | Description |
|---|---|---|
| `ScrollTo(double offsetX, double offsetY)` | `bool` | Scrolls to an absolute offset, clamped to `MaxScrollOffset`. `true` when the offset actually changed - which is how a consumed wheel tick is told from one that hit the end. |
| `ScrollBy(double deltaX, double deltaY)` | `bool` | Shifts the current offset by a delta. Same return. |
| `ContentBox()` | `LayoutRect` | `override`. The viewport, not the whole padding box. |
| `ArrangeBox()` | `LayoutRect` | `override`. On a scrolling axis the children get the whole content to spread over; on one that does not scroll, the viewport. |
| `ApplyScrollOffsetToChildren()` | `void` | `protected`. Shifts the laid out children by the clamped offset. Separate from `CalculateAllPositions()` so a container that places its children itself can position first and scroll second. |
| `CalculateSize()` | `PointD` | `override`. |
| `CalculateAllPositions()` | `void` | `override`. |
| `GenerateRenderData(ImageSurface surface, Context ctx)` | `void` | `override`. Draws the box, then the children, then the bars. |

## Examples

A panel:

```csharp
var panel = new RectangleControl(
    borderWidth: 2,
    borderColor: new ElementColor(0.0, 0.0, 0.0, 0.5),
    backgroundColor: new ElementColor(GuiStyle.DialogStrongBgColor));

panel.InsideOrientation = Orientation.Top;
panel.Padding = 10;
```

A row:

```csharp
var row = new RectangleControl();
row.InsideOrientation = Orientation.Left;

foreach (string caption in new[] { "One", "Two", "Three" })
    row.Children.Add(new ButtonControl { Text = caption });
```

![A vertical child above a horizontal row of three buttons](https://raw.githubusercontent.com/DrakenRolle/ModernVintageGUI/master/docs/images/readme-stacking.png)

A scrolling viewport:

```csharp
panel.Size = new PointD(300, 200);
panel.IsAutoSize = false;
panel.EnableVerticalScrollbar = true;   // switches ClipsChildren on with it
```

## See also

* [UIControl](UIControl) - the base class
* [IScrollable](Supporting-Types#iscrollable-interface) and [ScrollbarStyle](Supporting-Types#scrollbarstyle-class)
* [ElementColor](Supporting-Types#elementcolor-class)
* [Layout and Scaling](Layout-and-Scaling)
