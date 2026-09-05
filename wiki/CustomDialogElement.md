# CustomDialogElement Class

**Namespace:** `IS2Mod.ControlTypes.Custom`
**Assembly:** `ModernVintageGUI.dll`

The root of a control tree: it owns a Cairo surface, a GL texture, a place on screen and the
keyboard focus of everything inside it.

```csharp
public class CustomDialogElement : UIControl, IDisposable
```

**Inheritance:** `Object` → [UIControl](UIControl) → **CustomDialogElement**

**Implements:** `System.IDisposable`, `INotifyPropertyChanged` (inherited)

## Remarks

Children are laid out in **dialog local space** with the root at `0/0` - that is the space the
Cairo surface is drawn in - and only afterwards is the dialog itself moved to its position on
screen. [`UIControl.GetScreenPosition()`](UIControl#methods) adds the two together.

The constructor forces `Padding = 10`. Set it to `0` when the dialog carries a
[TitleBarControl](TitleBarControl) and wrap the rest of the content in a padded container, or the
dialog padding insets the bar and it no longer reaches the edges the way vanilla does.

`Layer` has to be a constructor argument rather than a property: the game sorts its renderer list
when a renderer is registered and never re-sorts it.

> **Dispose it.** The constructor registers two renderers with the game; dropping a dialog without
> `Dispose()` leaks those and its GL texture. Disposing the dialog also disposes every control in
> the tree that owns something - a dropdown owns a popup - so the tree is the dialog to look after
> and not each control in it.

## Constructors

| | Description |
|---|---|
| `CustomDialogElement(ICoreClientAPI capi, string _DialogName, string _Title = "", DialogRenderLayer _Layer = DialogRenderLayer.Normal)` | Registers the renderers and sets `Padding = 10`. `_Layer` picks the render band; popups belong in `Overlay`. |

## Properties

### Identity and state

| Name | Type | Description |
|---|---|---|
| `DialogName` | `string` | The name given at construction; part of the renderer id. |
| `Title` | `string` | The dialog title. |
| `Api` | `ICoreClientAPI` | The client API. Read-only. |
| `IsVisible` | `bool` | Whether the dialog is currently shown. Read-only. |
| `Layer` | [`DialogRenderLayer`](Supporting-Types#dialogrenderlayer-enum) | The render band, fixed at construction. |
| `MousePosition` | `Vec2i` | The last cursor position seen by this dialog. |

### Behaviour

| Name | Type | Description |
|---|---|---|
| `AutoCenter` | `bool` | Re-center on every layout pass. Default `true`. Turn it off for anything positioned by its opener - `ShowAt` does that for you. |
| `DrawsBackground` | `bool` | Draw the vanilla dialog background. Default `true`; `false` gives a fully transparent surface. |
| `IsModal` | `bool` | Swallow clicks that land on the dialog background too. Default `true`. |
| `CloseOnOutsideClick` | `bool` | Dismiss when a mouse button goes down anywhere outside. What makes a popup a popup. |
| `CloseOnEscape` | `bool` | Close on Escape while this dialog owns the keyboard. Default `true`. With it off the key is **not** consumed either, so Escape falls through to the pause menu. |
| `PrefersUngrabbedMouse` | `bool` | Keep the cursor released and suppress world interaction while open. Default `true`. |
| `IsFocused` | `bool` | Whether this dialog has focus. Set by [`UIManager`](Input-Focus-and-Rendering#uimanager). A focused dialog draws above the vanilla GUI and takes clicks in the overlap. |
| `MaxScreenFraction` | `PointD` | How much of the window the dialog may fill, per axis. Default `1.0, 1.0`. This is the screen limit and deliberately not `MaxSize`, which is in author units and scales. A clamped dialog also clips. |

### Focus and input

| Name | Type | Description |
|---|---|---|
| `FocusedControl` | `UIControl?` | The control inside this dialog that receives keys, or `null`. Read-only; set through `FocusControl`. |
| `HoveredControl` | `UIControl?` | The control the cursor is on, or `null`. Read-only. |
| `CapturedControl` | `UIControl?` | The control receiving all movement and the next release regardless of where the cursor is. Read-only; set through `CaptureMouse`. |

### Depth

| Name | Type | Description |
|---|---|---|
| `SurfaceRenderZ` | `float` | The depth the Cairo surface was drawn at this frame. Everything in the interactive pass has to go in front of it. |

## Fields

Constants that keep the per-frame layers apart. A larger `z` is nearer.

| Name | Type | Value | Description |
|---|---|---|---|
| `SlotItemZOffset` | `const float` | `10` | How far in front of the surface a stack sitting in a slot is drawn. |
| `StackSizeZOffset` | `const float` | `100` | The stack size a stack draws next to itself, relative to the stack. Anything meant to cover a stack has to clear it by more than this. |
| `HeldItemZOffset` | `const float` | `SlotItemZOffset + 360` | The stack on the cursor, which has to cover the slots and their numbers both. |
| `TooltipZOffset` | `const float` | `SlotItemZOffset + StackSizeZOffset + 40` | Where the vanilla item tooltip is drawn again, in front of the dialog that would otherwise hide it. |
| `UnscaledMouseStackOffset` | `const double` | `5 - 12 + 24` | Where the carried stack sits relative to the pointer, in author units. Straight out of `HudMouseTools`. |

## Methods

| Name | Returns | Description |
|---|---|---|
| `Show()` | `void` | Lays out, registers with the [`UIManager`](Input-Focus-and-Rendering#uimanager) and draws. Raises `Shown`. |
| `Hide()` | `void` | The counterpart. Raises `Hidden`. |
| `Toggle()` | `void` | One or the other. |
| `ShowAt(double screenX, double screenY)` | `void` | Opens at a screen position instead of centered, and switches `AutoCenter` off. |
| `SetPosition(double x, double y)` | `void` | Places the dialog at a screen position. The value is remembered across layout passes. |
| `CenterOnScreen()` | `void` | Centers the dialog. |
| `PerformLayout()` | `void` | `override`. Full layout pass; picks up the current GUI scale. |
| `OnGuiScaleChanged(double newScale)` | `void` | Re-lays out and redraws for a changed GUI scale while the dialog is open. The value is passed in rather than read from `RuntimeEnv.GUIScale`, because watchers run in registration order. |
| `Refresh()` | `void` | Asks for a redraw. Only sets a flag - the surface is rebuilt once, at the start of the next frame, so several handlers for one gesture cost one rebuild. |
| `RenderDialog()` | `void` | Draws the tree onto the surface and uploads it. |
| `DrawDialogBackground(Context context)` | `void` | `protected virtual`. The vanilla dialog background. |
| `ContainsScreenPoint(double screenX, double screenY)` | `bool` | Screen space bounds test. |
| `CaptureMouse(UIControl control)` | `void` | Route all movement and the next release to one control - what anything dragged needs. |
| `ReleaseMouseCapture()` | `void` | Give it back. |
| `FocusControl(UIControl? control)` | `void` | Moves the keyboard focus, raising `LostFocus` and `GotFocus`. `null` takes it away from everything. |
| `MoveFocus(bool backwards)` | `bool` | What Tab and Shift+Tab do. Returns `false` when the dialog has nothing focusable, so the caller can let the key through. |
| `HandleMouseDown/Up/Move(MouseEvent e)` | `void` | Routes a mouse event into the tree. |
| `HandleMouseWheel(MouseWheelEventArgs e)` | `void` | Offers a tick to the control under the cursor and then to each of its ancestors. |
| `HandleKeyDown/Up(KeyEventArgs e)` | `void` | Routes a key: first to the focused control, then to the dialog's own bindings. Only keys that did something are marked handled. |
| `HandleKeyPress(KeyEventArgs e)` | `void` | A typed character on its way to a control that asked for every key. |
| `ClearHoverState(MouseEvent e)` | `void` | Sends `Exit` to the hovered control and forgets it. |
| `CancelPress()` | `void` | Forgets an in-progress press without completing it as a click. |
| `GenerateInteractiveRenderData(ICoreClientAPI api, float deltaTime)` | `void` | `override`. Draws the per-frame controls, then the carried stack and the item tooltip, which the game draws behind this dialog. |
| `Dispose()` | `void` | Unregisters the renderers, frees the texture and disposes the tree. |

## Events

| Name | Type | Description |
|---|---|---|
| `Shown` | `EventHandler` | Raised after the dialog has been shown and laid out. |
| `Hidden` | `EventHandler` | Raised after it has been hidden. |

## Examples

```csharp
var dialog = new CustomDialogElement(capi, "myDialog", "My Title");
dialog.Children.Add(new TextLabelControl("Hello"));
dialog.Show();
```

![A dialog with a single text label](https://raw.githubusercontent.com/DrakenRolle/ModernVintageGUI/master/docs/images/readme-simple-dialog.png)

A popup that dismisses itself, in the band above ordinary dialogs:

```csharp
var popup = new CustomDialogElement(capi, "myPopup", "", DialogRenderLayer.Overlay)
{
    DrawsBackground       = false,
    AutoCenter            = false,
    CloseOnOutsideClick   = true,
    PrefersUngrabbedMouse = false
};

popup.ShowAt(anchor.GetScreenPosition().X, anchor.GetScreenPosition().Y);
```

## See also

* [UIControl](UIControl) - the base class
* [PopupHost](PopupHost) - the panel machinery built on this
* [Input, Focus and Rendering](Input-Focus-and-Rendering) - render bands, depth, mouse capture
* [Controls](Controls) - the control reference index
