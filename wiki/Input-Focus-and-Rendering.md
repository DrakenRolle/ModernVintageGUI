# Input, Focus and Rendering

## Events

Every `UIControl` exposes `Clicked`, `Enter`, `Exit`, `MouseDown`, `MouseUp`, `MouseMove` and
`MouseWheel`. `Clicked` only fires when press and release happened on the same control.

```csharp
button.Enter   += (s, e) => { /* hover in  */ };
button.Exit    += (s, e) => { /* hover out */ };
button.Clicked += (s, e) => { /* e.X, e.Y, e.Button */ };
```

Coordinates in the arguments are **screen** coordinates, while `Position` and `Size` are dialog
local.

### Atomic hit targets

A composite control that should behave as one piece overrides `HitTestRecursive` to return itself.
Without it the hit test descends into its parts, and they receive the events instead - the control
never lights up and never fires:

```csharp
protected override UIControl? HitTestRecursive(UIControl control, double localX, double localY)
{
    return control.ContainsLocalPoint(localX, localY) ? control : null;
}
```

`ButtonControl`, `ContextMenuItem` and `TitleBarControl` all do this.

### Mouse capture

Anything that is dragged needs capture, because the cursor leaves the control almost immediately:

```csharp
Dialog?.CaptureMouse(this);      // on MouseDown
// ... every MouseMove now arrives here, wherever the cursor is
Dialog?.ReleaseMouseCapture();   // on MouseUp
```

While a control holds capture, `UIManager` routes movement and the release straight to it - past the
hit test, past the vanilla dialog check and past every other dialog. Capture is released
automatically when the dialog hides.

## Keyboard

Every `UIControl` also exposes `KeyDown`, `KeyUp`, `GotFocus` and `LostFocus`. A control that the
player operates sets `IsFocusable`; decoration leaves it off and stays out of the tab order.

```csharp
var button = new ButtonControl { Text = "Save" };   // focusable already
myPanel.IsFocusable = false;                        // containers are not
```

| key | what it does |
|---|---|
| Tab / Shift+Tab | next / previous focusable control, wrapping at the ends |
| Down / Up | the same movement - in a stacking layout it is the same axis |
| Enter / Space | `PerformClick()` on the focused control, which raises `Clicked` |
| Escape | closes the dialog, unless `CloseOnEscape` is off |

![Three buttons: plain, focused with a ring, and hovered](https://raw.githubusercontent.com/DrakenRolle/ModernVintageGUI/master/docs/images/readme-keyboard-focus.png)

Hover and focus are separate states and a control can be in both, so they must not write to the
same colour. `ButtonControl` keeps them apart by giving the ring its own overlay rectangle;
`ContextMenuItem` has only one highlight and drives it from both flags.

### Tab order

The order of the tree, depth first: a control comes before its children and before its later
siblings. In a stacking layout that is the same as reading the dialog top to bottom, left to right,
so there is no second ordering to maintain - moving a control in the tree moves it in the tab order.

`UIControl.FocusableControls(root)` and `UIControl.NextFocusable(root, current, backwards)` are
static and take the root, so `ZLayoutHarness` checks the order without a dialog and therefore
without the game.

### Which keys are consumed

Only the ones that actually did something. That is not politeness - `TriggerKeyDown` runs ahead of
the vanilla hotkey manager, so consuming a key we have no use for would stop the player from
opening their inventory while a dialog is on screen.

Nothing is focused when a dialog opens, so a fresh dialog costs the player Escape and Tab and
nothing else. Enter and Space only become ours once they have tabbed into it or clicked a control.

`WantsAllKeyboardInput` is the override for a control that wants everything anyway - a text field.
Escape stays exempt from it, otherwise a text field would trap the player in the dialog.

### Typed characters

`ClientMain.OnKeyPress` - the one carrying typed characters with the keyboard layout applied, so
umlauts and dead keys - has no `TriggerKeyPress` and never touches `IClientEventAPI`:

```csharp
public void OnKeyPress(KeyEvent eventArgs)
{
    ...
    for (int i = 0; i < array.Length; i++)
    {
        array[i].OnKeyPress(eventArgs);
```

`KeyDown` carries a `KeyChar`, but it comes from the raw key rather than from the layout, so it is
not usable for text: it cannot tell an "a" from an "A" and cannot produce an umlaut at all. A text
field built on it works for ASCII and silently fails for half of Europe.

So typed characters need a patch of their own, and `ClientMainKeyPressPatch` is it - a **prefix**
that offers the character to the focused control first and returns false when the control took it:

```csharp
[HarmonyPrefix]
public static bool Prefix(KeyEvent eventArgs)
{
    UIManager? manager = UIManager.Current;

    if (manager == null || eventArgs == null)
        return true;

    return !manager.HandleKeyPress(eventArgs);   // false skips the original
}
```

A prefix rather than a postfix because a key we consumed must not also reach the chat box or fire a
vanilla hotkey.

The character arrives at the control as `KeyPress`, alongside `KeyDown` and `KeyUp`:

```csharp
var field = new TextInputControl(_Name: "search");
field.PlaceholderText = "Search items...";
field.TextChanged += (s, text) => Filter(text);
```

`TextInputControl` is the ready-made one - see [TextInputControl](TextInputControl). A control of
your own gets the same event; override `WantsAllKeyboardInput` if it should keep every key while
focused, so that typing an E does not open the inventory mid-word.

## UIManager

One per client session, created by the framework mod. It routes mouse input into the open dialogs
and owns focus.

Input goes through `api.Event.MouseDown` / `MouseUp` / `MouseMove` / `MouseWheelMove`, **not**
through `ClientPlatformWindows.mouseEventHandlers`. The platform hands every entry in that list its
own freshly allocated `MouseEvent`, so setting `Handled` there cannot stop the game from also
processing the click. `ClientMain` triggers the event API *before* forwarding to its client systems
and aborts as soon as `Handled` is set, so that is the only hook that can actually swallow input.

Routing order per mouse event:

1. a dialog holding mouse capture, if any - it gets everything
2. popups that should be dismissed, topmost first, stopping at the first dialog the click landed in
3. the dialog under the cursor, topmost first

### What gets swallowed, and what does not

Only the **press** is swallowed. Movement and the **release** are routed to our controls and then
handed on to the game with `Handled` cleared, and both had to be learned the hard way.

| event | swallowed? | why |
|---|---|---|
| `MouseDown` | yes, when it landed on one of ours | otherwise the click also places a block behind the dialog |
| `MouseUp` | **never** | `ClientMain` clears `InWorldMouseState` only for an unhandled release |
| `MouseMove` | **never** | `HudMouseTools` moves the carried stack from its own `OnMouseMove` |

The release is the one that bites hardest, because nothing looks wrong until later:

```csharp
api.eventapi.TriggerMouseUp(mouseEvent);
if (mouseEvent.Handled) return true;
UpdateMouseButtonState(button, InWorldMouseState, value: false);
```

Swallow it and the game still believes that button is held. Nothing happens while the dialog is
open - world interaction only runs while the mouse is grabbed - but the moment the dialog closes,
the mouse is grabbed again and the first frame after that finds a still pressed right button and a
block under the crosshair. `SystemMouseInWorldInteractions` does what it is told and interacts with
it. Closing a block's dialog with Escape therefore opened it straight back up, and clicking
anywhere in the world first "fixed" it - that click's release was the one that got through.

The asymmetry is the rule: **down belongs to whoever was clicked, up belongs to everyone**, because
everyone has state to let go of.

Keys go through `api.Event.KeyDown` / `KeyUp` and follow the same idea, but there is no cursor to
ask, so ownership is decided per dialog rather than per point:

1. an open popup - it is transient and drawn on top, and Escape has to close it before it closes
   the dialog underneath. Deepest first, so Escape in a cascade closes one sub menu per press
2. the focused dialog - the same one that draws above the vanilla GUI
3. nobody. With none of ours focused the player clicked a vanilla window or is looking at the
   world, and the keyboard is not ours to take

One consequence to know about: focus only changes on a click, and there is no event for a vanilla
dialog opening. Open one of our dialogs, then open the inventory on top of it, and Escape still
closes ours - clicking the inventory first hands focus over.

### Why you must not set this up yourself

MVS_UI needs two things per client session, and its own `ModSystem` does both:

```csharp
// this is the framework's job, not yours
harmony = new Harmony(HarmonyId);
harmony.PatchAll(typeof(ModernVintageGUIModSystem).Assembly);

uiManager = new UIManager(api);
```

**Harmony patches are process-wide.** Once `ClientMain.UpdateFreeMouse` is patched it is patched for
every caller in the game - there is no per-mod scope. The patches MVS_UI applies already cover every
mod that uses it. There are three:

| patch | on | why |
|---|---|---|
| `ClientMainUpdateFreeMousePatch` | `ClientMain.UpdateFreeMouse` | keep the cursor free while one of our dialogs is open |
| `ClientMainKeyPressPatch` | `ClientMain.OnKeyPress` | typed characters, which reach nothing else - see [Typed characters](#typed-characters) |
| `GuiManagerHoverSlotPatch` | `GuiManager.OnMouseMoveOver` | keep the item tooltip alive over our slots |

The last one is worth a sentence. `GuiManager.OnMouseMove` clears the hovered slot on every single
move and lets the vanilla dialogs claim it back; a slot of ours is in none of them, so the tooltip
appeared once and vanished on the next pixel of movement. The prefix sets the flag that says "a slot
is hovered" while the cursor is over one of ours, and does nothing at all otherwise. It checks that
its target method and field exist before it patches, so a game update that renames either turns the
tooltip off rather than taking the client down.

If several mods each set this up anyway, you would get one Harmony instance per mod registering the
same prefixes on the same methods, and one `UIManager` per mod all subscribed to the same mouse
events while `UIManager.Current` - a static - only ever points at the last one created.

Bundling a copy of the assembly is worse. The game loads mod assemblies per path
(`Assembly.UnsafeLoadFrom`), so two copies mean two sets of types with the same names: `UIControl`
from copy A is not the same type as `UIControl` from copy B, each copy has its own static
`UIManager.Current`, its own patch and its own dialog registry. Depend on the mod, do not ship
the DLL.

### The mouse grab

`ClientMain.UpdateFreeMouse()` recomputes `MouseGrabbed` once per rendered frame purely from the
number of open **vanilla** `GuiDialog`s. A custom dialog does not count, so the game re-grabs the
cursor on the next frame. `ClientMainUpdateFreeMousePatch` is a Harmony **prefix** that replaces the
method while one of our dialogs is open.

It has to replace rather than correct it: both `MouseGrabbed` setters have side effects on every
change of value - the platform warps the cursor to the window center, and `ClientMain` drops the
item held on the mouse - so flipping the value back in a postfix would fire those twice per frame.

## Focus and z-order

Focus follows the same rule the game applies to its own windows: **whoever is drawn on top gets the
click.** Clicking one of our dialogs focuses it and brings it above the vanilla GUI; clicking a
vanilla window that covers us drops our focus and we go back below it. A dialog that just opened is
focused.

Two independent things decide what ends up on top.

### Render order

Vanilla registers its whole GUI - dialogs and HUDs - in one renderer at `1.0` in the Ortho stage.
Each dialog of ours registers **two** renderers, one below that band and one above it, and exactly
one of them draws per frame depending on `IsFocused`.

| | Normal | Overlay |
|---|---|---|
| unfocused (below vanilla) | 0.5 | 0.6 |
| **vanilla GUI** | **1.0** | |
| focused (above vanilla) | 1.1 | 1.2 |

It has to be two registrations rather than one whose order changes: the game sorts its renderer list
when a renderer is registered and never re-sorts it, so moving a dialog between bands would mean
unregistering and registering again in the middle of input handling, while the render loop may be
walking that very list.

That is also why `DialogRenderLayer` is a constructor argument and not a property.

### Depth

Render order alone does not decide what is visible. The Ortho stage runs with the depth test on and
`GlDepthFunc(Lequal)`, and `ClientMain.OrthoMode` moves the model to `z = -19849` in a frustum of
0.4 to 20001 - so a **larger z is nearer**. Vanilla stacks its dialogs with
`GlTranslate(0, 0, ZSize)` at `ZSize = 150` each, plus the offsets its elements add on top.

`IRenderAPI.RenderTexture` defaults to `z = 50`, which is behind almost all of the vanilla GUI. Our
renderer therefore passes an explicit z:

| state | z |
|---|---|
| unfocused | 50 - a vanilla dialog covers us |
| focused | 10000 - clear of anything vanilla stacks |
| overlay (popups) | 11000 - above the dialog it belongs to, its slot numbers included |

A focused dialog therefore also covers the HUD elements, because vanilla draws dialogs and HUDs in
the same renderer.

### Depth inside one dialog

Item stacks are not in the Cairo surface - they are drawn per frame, in front of it. How far in
front is not a matter of taste: the game draws a stack's count with
`Render2DLoadedTexture(..., posZ + 100)`, a hundred nearer than the stack it belongs to, so
anything meant to cover a stack has to clear it by **more than a hundred** or the model lands in
front while the number stays behind. That is what "the count of the slot underneath printed over
the item on the cursor" is.

The offsets are constants on `CustomDialogElement`, relative to `SurfaceRenderZ`:

| constant | value | what sits there |
|---|---|---|
| `SlotItemZOffset` | 10 | a stack in a slot |
| `StackSizeZOffset` | 100 | the count that stack draws next to itself |
| `HeldItemZOffset` | 370 | the stack on the cursor - vanilla's own gap is 360, for this reason |
| `TooltipZOffset` | 150 | the item tooltip, which `GuiElementItemstackInfo` lifts another 1000 |

The overlay band's thousand above `FocusedZ` is the same arithmetic one level up: a popup only a
hundred above its dialog would have the slot counts of the grid underneath printed through it.

## Rendering

The whole control tree draws itself onto **one** Cairo surface, which is uploaded to a single GL
texture once per refresh. Controls draw into the shared surface in `GenerateRenderData` and must not
upload anything themselves.

One constraint follows from `RectangleControl`'s border blur: `SurfaceTransformBlur` works in
absolute surface pixels and does not see the context transform, so a control tree always has to be
drawn at the origin of its own surface. Compositing several trees onto a shared canvas with a
translate smears the blur across the neighbours - render each to its own surface and composite the
surfaces instead.
