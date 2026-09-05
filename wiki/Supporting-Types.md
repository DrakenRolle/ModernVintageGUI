# Supporting Types

The enums, structs, event arguments and helpers the controls are written in terms of. Each control
has its own page - see [Controls](Controls); this is everything they refer to.

* Enums: [Orientation](#orientation-enum) · [TextOrientation](#textorientation-enum) ·
  [RectangleBorderStyle](#rectangleborderstyle-enum) · [ImageFit](#imagefit-enum) ·
  [DropdownRowStyle](#dropdownrowstyle-enum) · [ContextMenuAnchor](#contextmenuanchor-enum) ·
  [DialogRenderLayer](#dialogrenderlayer-enum)
* Values: [ElementColor](#elementcolor-class) · [LayoutRect](#layoutrect-struct) ·
  [DropdownRowMetrics](#dropdownrowmetrics-struct) · [ScrollLayout](#scrolllayout-struct)
* Interfaces: [IScrollable](#iscrollable-interface) · [IItemTooltipSource](#iitemtooltipsource-interface)
* Event arguments: [MouseEventArgs](#mouseeventargs-class) · [KeyEventArgs](#keyeventargs-class) ·
  [MouseWheelEventArgs](#mousewheeleventargs-class)
* Drawing helpers: [ScrollbarStyle](#scrollbarstyle-class) · [VanillaDraw](#vanilladraw-class) ·
  [ItemTooltip](#itemtooltip-class)

---

## Orientation Enum

**Namespace:** `IS2Mod.Enums`

How a control stacks **its children** - assigned to [`UIControl.InsideOrientation`](UIControl#box-and-layout).

| Member | Description |
|---|---|
| `Top` | Children stack downwards, all stretched to the content width. The default for most containers. |
| `Bottom` | The same, positioned from the bottom. |
| `Left` | Children stack sideways, all stretched to the content height. |
| `Right` | The same, from the right. |
| `Fill` | **Not implemented.** |
| `None` | Children overlay each other, no stretching. |

> The constructor parameter named `_Orientation` sets `InsideOrientation`, not the control's own
> alignment. `UIControl.Orientation` - which would be that alignment - is currently never assigned
> and has no effect.

---

## TextOrientation Enum

**Namespace:** `IS2Mod.ControlTypes`

Where the text sits in a [TextLabelControl](TextLabelControl)'s box.

`Left` · `Center` · `Right` · `TopLeft` · `TopCenter` · `TopRight` · `MiddleLeft` ·
`MiddleCenter` · `MiddleRight` · `BottomLeft` · `BottomCenter` · `BottomRight`

---

## RectangleBorderStyle Enum

**Namespace:** `IS2Mod.ControlTypes`

Which side of a [RectangleControl](RectangleControl) a `HiddenBorders` entry leaves out.

`Top` · `Bottom` · `Left` · `Right`

---

## ImageFit Enum

**Namespace:** `ModernVintageGUI.ControlTypes`

How an [ImageControl](ImageControl) fits a picture into a box that is not its own shape.

| Member | Description |
|---|---|
| `Contain` | Whole image, aspect kept, letterboxed in the box. The default. |
| `Cover` | Fills the box, aspect kept, the overflow cut off. |
| `Stretch` | Fills the box exactly, aspect ignored. |
| `None` | Drawn at its own size, centred. |

---

## DropdownRowStyle Enum

**Namespace:** `ModernVintageGUI.ControlTypes`

How the rows of a [DropdownControl](DropdownControl) are laid out.

| Member | Description |
|---|---|
| `Auto` | Item lists get `ItemList`, everything else `Menu`. The default. |
| `Menu` | Tight text rows, the way `GuiElementListMenu` draws a menu. |
| `ItemList` | The airy rows of the survival handbook's Blocks and Items list: a large icon, the name well clear of it, and room between the rows. |

---

## ContextMenuAnchor Enum

**Namespace:** `ModernVintageGUI.Enums`

Which corner of its owner a [ContextMenuControl](ContextMenuControl) places its popup at.

`TopLeft` · `TopRight` · `TopCenter` · `LeftCenter` · `RightCenter` · `BottomLeft` ·
`BottomCenter` · `BottomRight`

---

## DialogRenderLayer Enum

**Namespace:** `IS2Mod.Enums`

Which band of the Ortho render stage a [CustomDialogElement](CustomDialogElement) draws in. Higher
draws later, i.e. on top. Fixed at construction, because the game sorts its renderer list when a
renderer is registered and never re-sorts it.

| Member | Description |
|---|---|
| `Normal` | Ordinary dialogs. |
| `Overlay` | Popups that have to cover ordinary dialogs - context menus, dropdowns, tooltips. |

Vanilla registers its own GUI at 1.0 and the crosshair at 1.02, so **both** bands stay below that on
purpose: vanilla dialogs are meant to cover ours, which matches the input rule in the `UIManager`
that yields to an open vanilla dialog.

---

## ElementColor Class

**Namespace:** `IS2Mod.ControlTypes`

An RGBA colour, in bytes. Constructible from bytes, from doubles or from the `double[]` the game's
own `GuiStyle` constants come as.

```csharp
public class ElementColor
```

| Member | Type | Description |
|---|---|---|
| `R`, `G`, `B`, `A` | `byte` | The channels. |
| `RNormalized`, `GNormalized`, `BNormalized`, `ANormalized` | `double` | The same as `0`..`1`. Read-only. |
| `Transparent` | `static ElementColor` | White at alpha 0. |
| `White` | `static ElementColor` | |
| `Black` | `static ElementColor` | |

**Constructors:** `ElementColor(byte r, byte g, byte b, byte a)` ·
`ElementColor(double r, double g, double b, double a)` · `ElementColor(double[] colors)` -
the last one falls back to `1.0` for any channel the array does not carry.

```csharp
new ElementColor(GuiStyle.DialogDefaultTextColor)   // straight from the game's palette
new ElementColor(0.0, 0.0, 0.0, 0.5)
new ElementColor(220, 60, 60, 255)
```

---

## LayoutRect Struct

**Namespace:** `IS2Mod.ControlTypes`

An axis aligned rectangle in dialog local **device pixels**, used for clip regions - where a pair of
`PointD` would leave it ambiguous whether the second one is a size or a corner.

```csharp
public readonly struct LayoutRect
```

| Member | Type | Description |
|---|---|---|
| `X`, `Y`, `Width`, `Height` | `double` | The rectangle. Read-only. |
| `Right`, `Bottom` | `double` | `X + Width` and `Y + Height`. Read-only. |
| `IsEmpty` | `bool` | `true` when it has no area, i.e. nothing inside it is visible. |
| `LayoutRect(double x, double y, double width, double height)` | | The constructor. |
| `Intersect(LayoutRect other)` | `LayoutRect` | The overlap of two rectangles. Empty when they do not touch - the answer for a control scrolled completely out of its viewport. |
| `Contains(double x, double y)` | `bool` | Bounds test. |

---

## DropdownRowMetrics Struct

**Namespace:** `ModernVintageGUI.ControlTypes`

The measurements of one [DropdownControl](DropdownControl) row, in author units. Both presets are
taken from the game rather than chosen.

```csharp
public readonly struct DropdownRowMetrics
```

| Member | Type | Description |
|---|---|---|
| `RowHeight` | `double` | How tall a row is. |
| `RowSpacing` | `double` | The gap between two rows. Half of it also sits above the first and below the last. |
| `IconSize` | `double` | The size an icon is drawn at. |
| `IconLeft` | `double` | How far the icon sits from the left edge of the row. |
| `TextLeft` | `double` | Where the caption starts on a row that has an icon. |
| `TextLeftWithoutIcon` | `double` | And on a row that has none. |
| `AlwaysIconColumn` | `bool` | Keep the icon column even on rows with nothing to put in it. `true` for an item list, where one iconless entry would otherwise pull its caption left. |
| `Menu` | `static readonly DropdownRowMetrics` | `GuiElementListMenu`: 30 high rows, text indented by 5, nothing between them. |
| `ItemList` | `static readonly DropdownRowMetrics` | The handbook list: 25 plus 4 above and below, 10 between rows, always an icon column. |

---

## ScrollLayout Struct

**Namespace:** `IS2Mod.ControlTypes`

Everything about a scrolling container's geometry for one layout pass: which bars are showing, how
much room is left for the content, and how far it may be shifted.

```csharp
public readonly struct ScrollLayout
```

It is computed fresh from the container every time anybody asks, never stored. The clip region, the
stretching of children, the hit test and the drawing all need the same answer, and a cached one
could disagree with the layout that produced it; being a pure function of (box, content size,
switches, thickness) also keeps the layout idempotent.

| Member | Type | Description |
|---|---|---|
| `Viewport` | `LayoutRect` | The area the content is laid out into and clipped to. |
| `VerticalBarVisible`, `HorizontalBarVisible` | `bool` | Which bars are showing. |
| `MaxOffset` | `PointD` | How far the content may be shifted on each axis, never negative. |
| `Thickness` | `double` | Bar width in device pixels. |
| `FullBox` | `LayoutRect` | The full padding box, before any bar was taken off. |
| `Resolve(...)` | `static ScrollLayout` | Works out which bars are needed. **Two rounds**, because the axes depend on each other - a vertical bar costs width, which can tip the content into needing a horizontal one. A third round could only flip a bar back off, which reads as flicker, so it stops there. |
| `VerticalTrack(double scale)` | `LayoutRect` | The groove the vertical handle runs in, stopping short of the horizontal bar so the two do not overlap in the corner. |
| `HorizontalTrack(double scale)` | `LayoutRect` | Mirrored. |

---

## IScrollable Interface

**Namespace:** `IS2Mod.Interfaces`

A container that can show more content than fits and scroll through it. Implemented by
[RectangleControl](RectangleControl), and therefore by everything derived from it.

```csharp
public interface IScrollable
```

The scrollbars are **not controls**. They are drawn by the container itself, in the strip it
reserves along its own edge, and they are not part of its `Children` - so they cannot be hit by the
layout, cannot end up in the tab order and cannot be dragged out of place. That is the whole reason
this is an interface on the container rather than a `ScrollbarControl`: a scrollbar has no meaning
without the thing it scrolls.

Implementing it alone changes nothing. It unlocks the two switches, and a bar only appears once one
of them is on **and** the content on that axis is actually larger than the viewport.

| Member | Type | Description |
|---|---|---|
| `EnableVerticalScrollbar` | `bool` | Allow scrolling up and down, and show a bar on the right when needed. |
| `EnableHorizontalScrollbar` | `bool` | The same for left and right. |
| `ScrollOffset` | `PointD` | How far the content is currently shifted, device pixels, never negative. `X` of 30 means the content is 30 pixels to the left of where it would sit unscrolled. |
| `ContentSize` | `PointD` | The full size of the content - what it would need if nothing were cut. Measured by the layout, not by the caller. |
| `ViewportSize` | `PointD` | The visible area: the content box minus what the visible bars reserve. |
| `MaxScrollOffset` | `PointD` | Content minus viewport, never below zero. Both zero means everything fits and no bar is shown. |
| `ScrollTo(double offsetX, double offsetY)` | `bool` | Absolute, clamped. `true` when the offset actually changed - which is how a consumed wheel tick is told from one that hit the end and should be passed on. |
| `ScrollBy(double deltaX, double deltaY)` | `bool` | Relative. Same return. |

---

## IItemTooltipSource Interface

**Namespace:** `IS2Mod.Interfaces`

A control that stands for an item stack and wants the game's own item tooltip on hover - an
inventory slot, an entry of an item dropdown, a recipe output preview. Implemented by
[ItemSlotControl](ItemSlotControl) and [DropdownItem](DropdownItem).

```csharp
public interface IItemTooltipSource
```

| Member | Type | Description |
|---|---|---|
| `TooltipSlot` | `ItemSlot?` | The slot to describe, or `null` when there is nothing to describe. May be a `DummySlot` - the tooltip only reads the stack out of it. |

Two things hang off this, and both are why it is an interface rather than a check for
`ItemSlotControl`:

1. The control announces the slot with [`ItemTooltip.Announce`](#itemtooltip-class) when the cursor
   arrives and leaves. That is what fills `HudMouseTools`, which is what draws the tooltip.
2. `UIManager.IsItemSlotHovered` reports it, and the `GuiManagerHoverSlotPatch` needs that report:
   the game takes the hovered slot back on every mouse movement unless one of its own windows
   claims it, and none of ours is one of its windows.

A control that skips this looks right and shows no tooltip.

---

## MouseEventArgs Class

**Namespace:** `IS2Mod.ControlTypes.Events`

```csharp
public class MouseEventArgs : EventArgs
```

| Member | Type | Description |
|---|---|---|
| `X`, `Y` | `int` | **Screen** coordinates - not dialog local. |
| `DeltaX`, `DeltaY` | `int` | Movement since the last event. |
| `Button` | `EnumMouseButton` | Which button. |
| `Modifiers` | `int` | The modifier keys held. |
| `Handled` | `bool` | Set to stop the event going further. |

Besides the constructor taking the game's `MouseEvent`, there are overloads taking the coordinates
directly, for driving a control from code or from the layout harness.

---

## KeyEventArgs Class

**Namespace:** `IS2Mod.ControlTypes.Events`

```csharp
public class KeyEventArgs : EventArgs
```

Unlike `MouseEventArgs` this is created **once per event** by the dialog and handed down unchanged,
because `Handled` has to travel back up: the dialog copies it onto the game's `KeyEvent`, and only
then does the game stop forwarding the key to its hotkey manager and client systems.

| Member | Type | Description |
|---|---|---|
| `KeyCode` | `int` | The raw key code. Compare against `Key` instead. |
| `KeyCode2` | `int?` | The second key when two were pressed in quick succession. |
| `Key` | `GlKeys` | The key that was pressed. |
| `KeyChar` | `char` | The character the game associated with the key. **Not usable for text input** - it comes from the raw key and not from the keyboard layout, so no umlauts and no dead keys. Listen on [`UIControl.KeyPress`](UIControl#events) for those. |
| `CtrlPressed`, `ShiftPressed`, `AltPressed`, `CommandPressed` | `bool` | The modifiers. |
| `Handled` | `bool` | Set this to stop the key reaching anything else. **Leave it alone for keys you do not use**, or the player cannot open their inventory while one of our dialogs is focused. |

---

## MouseWheelEventArgs Class

**Namespace:** `IS2Mod.ControlTypes.Events`

```csharp
public class MouseWheelEventArgs : EventArgs
```

| Member | Type | Description |
|---|---|---|
| `delta`, `deltaPrecise` | `int` / `float` | How far the wheel moved. |
| `value`, `valuePrecise` | `int` / `float` | The running total. |
| `IsHandled` | `bool` | Whether somebody already used this tick. Read-only. |
| `SetHandled(bool value = true)` | `void` | Mark it used. |

One instance travels from the control under the cursor up through its ancestors, so each of them can
see whether somebody below already used the tick. That is what lets a list inside a list behave:
the inner one scrolls until it hits its end, and only then does the outer one take over.

---

## ScrollbarStyle Class

**Namespace:** `IS2Mod.ControlTypes`

```csharp
public static class ScrollbarStyle
```

Draws a scrollbar exactly the way the vanilla GUI does. Every value comes from
`GuiElementScrollbar` and the emboss helper in `GuiElement`. Static, so the layout harness can
render a bar without a client.

| Member | Type | Value | Description |
|---|---|---|---|
| `UnscaledWidth` | `const double` | `20.0` | Bar width in author units. Add it to a container's width when you size one that will scroll. |
| `UnscaledPadding` | `const double` | `2.0` | The inset of the track. |
| `MinimumHandleLength` | `const double` | `10.0` | Vanilla never lets the handle get shorter, in device pixels. |
| `UnscaledWheelStep` | `const double` | `102.0` | How far one wheel tick moves the content, in author units. |
| `DrawTrack(Context ctx, LayoutRect track, double scale)` | `static void` | | The sunken groove. |
| `DrawHandle(Context ctx, LayoutRect handle)` | `static void` | | The handle. |
| `HandleLength(double trackLength, double viewportLength, double contentLength)` | `static double` | | The visible fraction of the content, clamped. |
| `HandlePosition(...)` | `static double` | | Where the handle sits for a given scroll offset. |
| `ScrollOffsetForHandlePosition(...)` | `static double` | | The inverse, used while dragging. |

---

## VanillaDraw Class

**Namespace:** `IS2Mod.ControlTypes`

```csharp
public static class VanillaDraw
```

Cairo routines the game draws its own widgets with, as statics. `GuiElement` carries these as
instance methods, which puts them out of reach of anything that is not a `GuiElement` - and nothing
in this framework is one. They are ported rather than approximated, because the numbers are exactly
what makes a control read as part of the game.

| Member | Description |
|---|---|
| `EmbossRoundRectangle(...)` | The bevel vanilla puts on buttons, dropdowns and inset boxes: a light edge along the top left and a dark one along the bottom right, one pixel per pass, fading out over `depth` passes. `inverse` swaps the two, which is what turns a raised button into a sunken well. A second overload exposes the knobs vanilla keeps to itself. |

---

## ItemTooltip Class

**Namespace:** `IS2Mod.ControlTypes`

```csharp
public static class ItemTooltip
```

Tells the game which item slot the cursor is on, the way `GuiElementItemSlotGridBase` does it.
Shared by every [`IItemTooltipSource`](#iitemtooltipsource-interface), because getting it half right
- announcing the arrival but not the departure - leaves a tooltip standing over a slot the cursor
has long left.

| Member | Description |
|---|---|
| `Announce(ICoreClientAPI? api, ItemSlot? slot, bool entered)` | `static`. Triggers `OnMouseEnterSlot` or `OnMouseLeaveSlot`. Null-safe on both arguments. |

## See also

* [Controls](Controls) - the control reference index
* [Layout and Scaling](Layout-and-Scaling) · [Input, Focus and Rendering](Input-Focus-and-Rendering)
