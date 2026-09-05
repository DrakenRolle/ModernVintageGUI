# ProgressBarControl Class

**Namespace:** `ModernVintageGUI.ControlTypes`
**Assembly:** `ModernVintageGUI.dll`

A bar that fills up: a furnace burning down, a recipe in progress, a loading step.

```csharp
public class ProgressBarControl : UIControl
```

**Inheritance:** `Object` → [UIControl](UIControl) → **ProgressBarControl**

**Implements:** `INotifyPropertyChanged` (inherited)

## Remarks

Drawn like `GuiElementStatbar` - a dark trough with a raised bevel and a filled bar over the part
that is done - because that is the bar players already read as "how far along is this".

Nothing here polls. Set `Value` from a tick listener or from the change events of whatever it is
measuring: a control that reads the world every frame keeps redrawing a dialog nobody is looking at.

## Constructors

| | Description |
|---|---|
| `ProgressBarControl(string _Name = "", double _Margin = 5)` | Defaults to 200 x 26 author units with auto-sizing off. |

## Fields

| Name | Type | Value | Description |
|---|---|---|---|
| `UnscaledDefaultHeight` | `const double` | `26.0` | Vanilla's default statbar height, in author units. |

## Properties

| Name | Type | Description |
|---|---|---|
| `Value` | `double` | Where the bar stands. Clamped into `Min`..`Max`; raises `ValueChanged` when it actually moves. |
| `Min` | `double` | The bottom of the range. Default `0`. Setting it re-clamps `Value`. |
| `Max` | `double` | The top of the range. Default `1`. Setting it re-clamps `Value`. |
| `Fraction` | `double` | How full it is, `0` to `1`. Read-only, and what the drawing actually uses. |
| `BarColor` | [`ElementColor`](Supporting-Types#elementcolor-class) | The fill. The game's health red by default. |
| `RightToLeft` | `bool` | Fill from the other end, for something draining. |
| `Text` | `string` | Drawn centred over the bar. Empty draws none. |

## Methods

| Name | Returns | Description |
|---|---|---|
| `CalculateSize()` | `PointD` | `override`. |
| `NormalizeChildrenByDelta()` | `void` | `override`. |
| `CalculateAllPositions()` | `void` | `override`. |
| `GenerateRenderData(ImageSurface surface, Context ctx)` | `void` | `override`. Trough, bevel, bar, caption. |

## Events

| Name | Type | Description |
|---|---|---|
| `ValueChanged` | `EventHandler<double>` | Raised when the value changes, whoever changed it. Carries the clamped value. |

## Examples

```csharp
var bar = new ProgressBarControl(_Name: "burn");
bar.Min  = 0;
bar.Max  = burnDurationSeconds;
bar.Text = "Burning";

capi.Event.RegisterGameTickListener(dt => bar.Value = remaining, 200);
```

## See also

* [UIControl](UIControl) - the base class
