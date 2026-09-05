# UIControl Class

**Namespace:** `IS2Mod.ControlTypes`
**Assembly:** `ModernVintageGUI.dll`

The base class of every control. Carries the layout box, the tree, the input events and the
keyboard focus flags, and defines the two drawing passes a control can take part in.

```csharp
public abstract class UIControl : INotifyPropertyChanged
```

**Inheritance:** `Object` → **UIControl**

**Implements:** `System.ComponentModel.INotifyPropertyChanged`

**Derived:** [CustomDialogElement](CustomDialogElement) · [RectangleControl](RectangleControl) ·
[TextLabelControl](TextLabelControl) · [ButtonControl](ButtonControl) ·
[CheckboxControl](CheckboxControl) · [TextInputControl](TextInputControl) ·
[ProgressBarControl](ProgressBarControl) · [TabsControl](TabsControl) ·
[ImageControl](ImageControl) · [ColorPickerControl](ColorPickerControl) ·
[DropdownControl](DropdownControl) · [DropdownItem](DropdownItem) ·
[ItemTypeSelectorControl](ItemTypeSelectorControl) · [ContextMenuControl](ContextMenuControl) ·
[ContextMenuItem](ContextMenuItem) · [TitleBarControl](TitleBarControl) ·
[ItemSlotControl](ItemSlotControl)

## Remarks

Everything a caller assigns - `Margin`, `Padding`, `Size`, `MaxSize`, font sizes, border widths -
is in **unscaled author units**. The layout multiplies by [`LayoutScale`](#properties) on the way
to device pixels, so `Position` and `Size` are already device pixels when the renderer and the hit
test read them. That is the same split `GuiElement.scaled()` makes in the vanilla GUI.

Property changes are **not** observed. `Children` is, so adding or removing a child re-lays out and
redraws the dialog by itself; after changing a text, a colour or a flag, call `Dialog?.Refresh()`.

Building a subtree before it is added to a dialog is a normal pattern: `Dialog` returns `null`
while a control is detached instead of throwing, and the next layout pass wires everything up.

## Constructors

| | Description |
|---|---|
| `UIControl(string _Name = "", PointD? _Size = null, Orientation _Orientation = Orientation.None, double _Margin = 0, double _Padding = 0, int _Index = 0)` | `protected`. `_Orientation` sets [`InsideOrientation`](#properties), not the control's own alignment. A `_Size` of `null` **or** `0/0` makes the control auto-sizing. |

## Properties

### Tree

| Name | Type | Description |
|---|---|---|
| `Children` | `ObservableCollection<UIControl>` | The child controls. Observable: adding or removing one re-lays out and redraws the dialog. |
| `Parent` | `UIControl?` | The layout parent. Set by the layout. |
| `Dialog` | `CustomDialogElement?` | The dialog this control belongs to, or `null` while the subtree is detached. |
| `Name` | `string` | Free-form id, yours to use. |
| `Index` | `int` | Ordering hint. |

### Box and layout

| Name | Type | Description |
|---|---|---|
| `Position` | `PointD` | Dialog local position in device pixels. On a dialog root it is the on-screen position instead. |
| `Size` | `PointD` | The size the control occupies, in device pixels. Assigning it declares a wanted size; the layout writes through `SetLayoutSize` so it does not overwrite that wish. |
| `ExplicitSize` | `PointD` | `protected`. The last size assigned from outside - the input of the measure pass. Never written by the layout. |
| `IsAutoSize` | `bool` | Size follows the content instead of the assigned `Size`. |
| `MaxSize` | `PointD` | Upper limit for the measured size, in author units. `0` on an axis means no limit. Needs `ClipsChildren` or scrolling to be useful, and it scales with the GUI slider. |
| `Margin` | `double` | Space around the control, author units. A stacking parent spaces siblings by `2 × Margin`. |
| `Padding` | `double` | Space between the control's border and its children, author units. |
| `InsideOrientation` | [`Orientation`](Supporting-Types#orientation-enum) | How this control stacks **its children**. |
| `Orientation` | [`Orientation`](Supporting-Types#orientation-enum) | The control's own alignment. Currently inert - see [Layout and Scaling](Layout-and-Scaling). |
| `LayoutScale` | `double` | Device pixels per author unit. Only the value on the root matters; every control reports the root's. |
| `MeasuredContentSize` | `PointD` | What the children measured to, in device pixels, padding not included. The "how much content is there" number a scrolling container needs. |
| `CalculatedSize` | `PointD` | `protected`. The measured size before the overflow check. |
| `ScaledMargin`, `ScaledPadding`, `ScaledExplicitSize`, `ScaledMaxSize` | `double` / `PointD` | `protected`. The same values in device pixels. |
| `ClipsChildren` | `bool` | Cut everything the descendants draw at [`ContentBox()`](#methods), and stop the overflow check from shrinking them. Off by default. |

### Input and focus

| Name | Type | Description |
|---|---|---|
| `IsFocusable` | `bool` | Whether the control is in the tab order and can be clicked into focus. Off by default - decoration must never end up in the tab order. |
| `HasKeyboardFocus` | `bool` | Read-only from outside. Owned by [`CustomDialogElement.FocusControl`](CustomDialogElement#methods). |
| `WantsAllKeyboardInput` | `bool` | `virtual`, `false`. Override to take every key while focused - what a text field does. |

### Rendering

| Name | Type | Description |
|---|---|---|
| `IsStaticElement` | `bool` | Marks the control as part of the static composition. |
| `StaticElementsTexture` | `LoadedTexture?` | The texture a control may keep for itself. |

## Methods

| Name | Returns | Description |
|---|---|---|
| `PerformLayout()` | `void` | `virtual`. A full layout pass over this control and everything below it: measure, then arrange. Call it on a root. Idempotent. |
| `CalculateSize()` | `PointD` | `virtual`. The measure pass. Override for a control that sizes itself; run the result through `ClampToMaxSize`. |
| `NormalizeChildrenByDelta()` | `void` | `virtual`. Stretches children across the arrange box. |
| `CalculateAllPositions()` | `void` | `virtual`. Positions this control and its children. |
| `CalculatePosition(UIControl? previousSibling)` | `void` | Positions this control relative to its parent and the sibling before it. |
| `CalculateChildrenRelationship()` | `void` | Re-links `Parent` and `Dialog` down the subtree. |
| `ContentBox()` | `LayoutRect` | `virtual`. The area the children were laid out into - the box inset by the padding. What `ClipsChildren` cuts at. |
| `ArrangeBox()` | `LayoutRect` | `virtual`. How much room the children get. The same as `ContentBox()` unless the control scrolls. |
| `PaddingBox()` | `LayoutRect` | `protected`. The box inset by the padding, with nothing else taken off. The starting point for an override of `ContentBox()`. |
| `EffectiveClip()` | `LayoutRect?` | The region this control may actually paint in: the overlap of the content boxes of every clipping ancestor. Anything that writes pixels behind Cairo's back has to intersect with it. |
| `ClampToMaxSize(PointD measured)` | `PointD` | `protected`. Applies `MaxSize` to a measured size. |
| `SetLayoutSize(PointD size)` | `void` | `protected internal`. Size assignment for the layout passes; leaves `ExplicitSize` alone. |
| `GetScreenPosition()` | `PointD` | Where the control sits on screen: dialog local position plus the dialog position. |
| `ContainsLocalPoint(double localX, double localY)` | `bool` | Bounds test in dialog local coordinates. |
| `HitTest(int screenX, int screenY)` | `UIControl?` | `protected`. The deepest control at a screen position. |
| `HitTestRecursive(UIControl control, double localX, double localY)` | `UIControl?` | `protected virtual`. Override and return `this` to make a composite an atomic hit target. |
| `GenerateRenderData(ImageSurface surface, Context context)` | `void` | `virtual`. Draws the control and its children onto the dialog's Cairo surface. Never upload to the GPU here. |
| `GenerateInteractiveRenderData(ICoreClientAPI api, float deltaTime)` | `void` | `virtual`. The per-frame pass, for what cannot go into a Cairo surface - an item stack. Coordinates here are **screen** coordinates. |
| `OnDialogShown()` | `void` | `virtual`. The dialog was shown. Always call the base so the rest of the subtree is told. |
| `OnDialogHidden()` | `void` | `virtual`. The counterpart. |
| `RecomposeToMain()` | `void` | Re-runs the layout and redraws the dialog. |
| `PerformClick()` | `void` | Raises `Clicked` without a mouse - what Enter and Space on the focused control do. |
| `InvokeEventClicked`, `InvokeEventEnter`, `InvokeEventExit`, `InvokeEventMouseDown`, `InvokeEventMouseUp`, `InvokeEventMouseMove`, `InvokeEventMouseWheel`, `InvokeEventKeyDown`, `InvokeEventKeyUp`, `InvokeEventKeyPress`, `InvokeGotFocus`, `InvokeLostFocus` | `void` | Raise the matching event. Public so the layout harness can drive a control into a visual state without a client. |
| `FocusableControls(UIControl root)` | `IEnumerable<UIControl>` | `static`. Every focusable control below `root`, in tab order - the tree order, depth first. |
| `NextFocusable(UIControl root, UIControl? current, bool backwards)` | `UIControl?` | `static`. What Tab and Shift+Tab move to. Wraps around; `null` when nothing is focusable. |
| `OnPropertyChanged(string?)`, `SetProperty<T>(ref T, T, string?)` | `void` / `bool` | `protected`. Raise `PropertyChanged`. |

## Events

| Name | Type | Description |
|---|---|---|
| `Clicked` | `EventHandler<MouseEventArgs>` | A full press and release on the control. |
| `Enter` / `Exit` | `EventHandler<MouseEventArgs>` | The cursor arrived at / left the control. |
| `MouseDown` / `MouseUp` / `MouseMove` | `EventHandler<MouseEventArgs>` | The raw mouse events. |
| `MouseWheel` | `EventHandler<MouseWheelEventArgs>` | A wheel tick. Offered to the control under the cursor first, then to its ancestors - which is what makes a list inside a list behave. |
| `KeyDown` / `KeyUp` | `EventHandler<KeyEventArgs>` | A key, by raw key code. |
| `KeyPress` | `EventHandler<KeyEventArgs>` | A **typed character**, with the keyboard layout applied - umlauts, accents, dead keys. Exists only because of `ClientMainKeyPressPatch`. |
| `GotFocus` / `LostFocus` | `EventHandler` | This control became, or stopped being, the keyboard focus of its dialog. |
| `PropertyChanged` | `PropertyChangedEventHandler` | From `INotifyPropertyChanged`. |

> Mouse coordinates in the event arguments are **screen** coordinates, while `Position` and `Size`
> are dialog local. A hit test that forgets to convert simply never matches.

## Examples

```csharp
// A control is focusable when the player operates it, and not when it is decoration.
var button = new ButtonControl { Text = "Save" };   // focusable already
myPanel.IsFocusable = false;                        // containers are not

// Property changes are not observed - ask for the redraw yourself.
myLabel.Text = "Changed at runtime";
myLabel.Dialog?.Refresh();
```

## See also

* [Layout and Scaling](Layout-and-Scaling) - the two passes, the three sizes, GUI scale
* [Input, Focus and Rendering](Input-Focus-and-Rendering) - events, capture, focus, depth
* [Writing a Custom Control](Writing-a-Custom-Control) - the rules an override has to follow
* [Controls](Controls) - the control reference index
