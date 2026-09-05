# TitleBarControl Class

**Namespace:** `ModernVintageGUI.ControlTypes`
**Assembly:** `ModernVintageGUI.dll`

The bar across the top of a dialog: title, burger menu, close cross - and the handle the dialog is
dragged by.

```csharp
public class TitleBarControl : UIControl
```

**Inheritance:** `Object` → [UIControl](UIControl) → **TitleBarControl**

**Implements:** `INotifyPropertyChanged` (inherited)

## Remarks

The bar spans the full width of its parent. Put it in a dialog with `Padding = 0` and wrap the
content below it in a padded container - otherwise the dialog padding insets the bar and it no
longer reaches the edges the way vanilla does.

Clicks are dispatched **by region** rather than by child control: the cross closes, the burger
opens the menu, the rest of the bar is drag surface. Dragging keeps a strip of the dialog on screen
so it cannot be pulled out of reach.

Drawn to match `GuiElementDialogTitleBar` step for step, including its quirks: the light inset
stroke is in raw pixels while everything around it scales, and the soft edge comes from blurring
the surface after the stroke rather than from a gradient. The two icons are drawn by the game's own
`IconUtil`, so they are the same shapes vanilla uses rather than a lookalike.

## Constructors

| | Description |
|---|---|
| `TitleBarControl(string title = "")` | No margin, no padding; the bar is 31 author units high. |

## Properties

| Name | Type | Description |
|---|---|---|
| `Title` | `string` | The caption drawn in the bar. |
| `IsMovable` | `bool` | Whether the dialog can be dragged by this bar. Switching it **on** also switches [`CustomDialogElement.AutoCenter`](CustomDialogElement#behaviour) off, because a dragged dialog must keep where it was put; switching it off re-lays the dialog out so it snaps back. |
| `Menu` | [`ContextMenuControl?`](ContextMenuControl) | The Fixed / Movable menu behind the burger icon, built on first use. Read-only. |

## Methods

| Name | Returns | Description |
|---|---|---|
| `CalculateSize()` | `PointD` | `override`. Full parent width, fixed bar height. |
| `HitTestRecursive(UIControl, double, double)` | `UIControl?` | `protected override`. A bar is one piece. |
| `GenerateRenderData(ImageSurface surface, Context ctx)` | `void` | `override`. |

## Events

| Name | Type | Description |
|---|---|---|
| `CloseRequested` | `EventHandler` | Raised by the close cross. Hides the dialog when nothing handles it. |

## Examples

```csharp
dialog.Padding = 0;                                   // the bar has to reach the edges

var titleBar = new TitleBarControl("My Title");
dialog.Children.Add(titleBar);

var content = new RectangleControl();                 // padded container for everything else
content.InsideOrientation = Orientation.Top;
content.Padding = 10;
dialog.Children.Add(content);
```

![A dialog with a vanilla style title bar](https://raw.githubusercontent.com/DrakenRolle/ModernVintageGUI/master/docs/images/readme-title-bar.png)

Doing something else with the cross:

```csharp
titleBar.CloseRequested += (sender, e) => SaveAndClose();
```

## See also

* [UIControl](UIControl) - the base class
* [CustomDialogElement](CustomDialogElement) - the dialog this bar moves
* [ContextMenuControl](ContextMenuControl) - what the burger opens
