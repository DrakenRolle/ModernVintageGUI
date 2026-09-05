# CheckboxControl Class

**Namespace:** `ModernVintageGUI.ControlTypes`
**Assembly:** `ModernVintageGUI.dll`

A box the player switches on and off, with an optional caption beside it.

```csharp
public class CheckboxControl : UIControl
```

**Inheritance:** `Object` → [UIControl](UIControl) → **CheckboxControl**

**Implements:** `INotifyPropertyChanged` (inherited)

## Remarks

Drawn the way `GuiElementSwitch` draws itself: a sunken square, and when it is on an inner square
filled with the water pattern the game uses for exactly this. The caption is a
[TextLabelControl](TextLabelControl) of ours rather than part of the box, so it wraps, aligns and
measures like every other piece of text in a dialog.

The whole control is the hit target, caption included - a checkbox whose label does not toggle it
is a small daily annoyance. It is focusable, so Tab reaches it and Space toggles it.

## Constructors

| | Description |
|---|---|
| `CheckboxControl(string text = "", bool isChecked = false, string _Name = "", double _Margin = 5)` | |

## Fields

| Name | Type | Value | Description |
|---|---|---|---|
| `UnscaledBoxSize` | `const double` | `30.0` | `GuiElementSwitch`'s default box size, in author units. |

## Properties

| Name | Type | Description |
|---|---|---|
| `IsChecked` | `bool` | Whether the box is ticked. Setting it raises `CheckedChanged`, so a handler sees a change made from code the same way it sees one made by the player. |
| `Text` | `string` | The caption beside the box. Empty leaves a bare box. Setting it re-lays out. |

## Methods

| Name | Returns | Description |
|---|---|---|
| `CalculateSize()` | `PointD` | `override`. The box plus the gap plus the caption. |
| `NormalizeChildrenByDelta()` | `void` | `override`. |
| `CalculateAllPositions()` | `void` | `override`. |
| `HitTestRecursive(UIControl, double, double)` | `UIControl?` | `protected override`. One hit target - the caption must not take the click on its way to the box. |
| `GenerateRenderData(ImageSurface surface, Context ctx)` | `void` | `override`. |

## Events

| Name | Type | Description |
|---|---|---|
| `CheckedChanged` | `EventHandler<bool>` | Raised whenever the tick changes - by click, by keyboard or from code. |

## Examples

```csharp
var box = new CheckboxControl("Show advanced options", isChecked: true);
box.CheckedChanged += (sender, isChecked) => settings.ShowAdvanced = isChecked;
```

## See also

* [UIControl](UIControl) - the base class
* [TextInputControl](TextInputControl) · [DropdownControl](DropdownControl) - the other input controls
