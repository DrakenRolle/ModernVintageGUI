# ColorPickerControl Class

**Namespace:** `ModernVintageGUI.ControlTypes`
**Assembly:** `ModernVintageGUI.dll`

A square for saturation and brightness, a strip of hues beside it, and a swatch showing what came
out.

```csharp
public class ColorPickerControl : UIControl
```

**Inheritance:** `Object` → [UIControl](UIControl) → **ColorPickerControl**

**Implements:** `INotifyPropertyChanged` (inherited)

## Remarks

The square is drawn the way every picker draws it, and the way Cairo makes cheap: the chosen hue
flat, a white to transparent gradient across it, and a transparent to black gradient down it. Three
fills instead of a per-pixel loop, and the same picture.

Pressing inside either part **captures the mouse**, so a drag keeps working when the cursor leaves
the control - letting go of a colour halfway through because the pointer slipped over the edge is
what makes a picker feel broken.

> Mouse coordinates in the event arguments are **screen** coordinates while `Position` and `Size`
> are dialog local, and a hit test that forgets to convert simply never matches. Worth repeating
> here, because it is the bug this control shipped with: it drew correctly and ignored every click.

## Constructors

| | Description |
|---|---|
| `ColorPickerControl(string _Name = "", PointD? _Size = null, double _Margin = 5)` | Defaults to 166 x 140 author units (square plus gap plus hue strip) with auto-sizing off. Focusable. |

## Properties

| Name | Type | Description |
|---|---|---|
| `SelectedColor` | [`ElementColor`](Supporting-Types#elementcolor-class) | The picked colour. Setting it moves the marks to match and raises `ColorChanged`. |
| `Hue` | `double` | The hue of the pick, `0` to `1`. Read-only. |
| `Saturation` | `double` | The saturation, `0` to `1`. Read-only. |
| `Brightness` | `double` | The brightness, `0` to `1`. Read-only. |

## Methods

| Name | Returns | Description |
|---|---|---|
| `FromHsv(double hue, double saturation, double value)` | `ElementColor` | `static`. Hue, saturation and brightness to a colour. All three are `0` to `1`. |
| `ToHsv(ElementColor color)` | `(double Hue, double Saturation, double Value)` | `static`. And back again - what a mod stores instead of a colour object. |
| `CalculateSize()` | `PointD` | `override`. |
| `HitTestRecursive(UIControl, double, double)` | `UIControl?` | `protected override`. One hit target. |
| `GenerateRenderData(ImageSurface surface, Context ctx)` | `void` | `override`. Square, hue strip, swatch, marks. |

## Events

| Name | Type | Description |
|---|---|---|
| `ColorChanged` | `EventHandler<ElementColor>` | Raised whenever the pick changes, by dragging or from code. |

## Examples

```csharp
var picker = new ColorPickerControl(_Name: "tint");
picker.SelectedColor = new ElementColor(0.2, 0.6, 1.0, 1.0);
picker.ColorChanged += (sender, color) =>
{
    preview.BackgroundColor = color;
    preview.Dialog?.Refresh();
};
```

Storing a pick as three numbers:

```csharp
(double h, double s, double v) = ColorPickerControl.ToHsv(picker.SelectedColor);
picker.SelectedColor = ColorPickerControl.FromHsv(h, s, v);
```

## See also

* [UIControl](UIControl) - the base class
* [ElementColor](Supporting-Types#elementcolor-class)
* [Input, Focus and Rendering](Input-Focus-and-Rendering#mouse-capture) - mouse capture
