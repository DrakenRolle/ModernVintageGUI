# Controls

Every control has a page of its own, laid out the way a .NET API reference is: what it inherits
from, its constructors, properties, methods and events, then the remarks and an example.

For the rules behind sizes and positions see [Layout and Scaling](Layout-and-Scaling); for events,
focus and depth see [Input, Focus and Rendering](Input-Focus-and-Rendering). The enums, structs and
event arguments they are written in terms of are on [Supporting Types](Supporting-Types).

![A dialog showing every control](https://raw.githubusercontent.com/DrakenRolle/ModernVintageGUI/master/docs/images/readme-showcase.png)

*Every control in one dialog. Rendered from the same code the test hotkey opens in game, so it
cannot show a screen that no longer exists.*

## The reference

### Roots and containers

| class | inherits from | what it is |
|---|---|---|
| [CustomDialogElement](CustomDialogElement) | [UIControl](UIControl) | The root of a tree: a surface, a texture, a place on screen, and the keyboard focus of everything in it. |
| [UIControl](UIControl) | `Object` | The base class. The layout box, the tree, the input events, the two drawing passes. |
| [RectangleControl](RectangleControl) | [UIControl](UIControl) | A box - fill, borders, rounded corners, blur - and the general purpose container. Clips and scrolls. |

### Text and pictures

| class | inherits from | what it is |
|---|---|---|
| [TextLabelControl](TextLabelControl) | [UIControl](UIControl) | One line of text, or wrapped text. Measures itself from its font. |
| [ImageControl](ImageControl) | [UIControl](UIControl) | A picture from any mod's assets, or one of the game's GUI icons. |
| [ProgressBarControl](ProgressBarControl) | [UIControl](UIControl) | A bar that fills up. |

### Things the player operates

| class | inherits from | what it is |
|---|---|---|
| [ButtonControl](ButtonControl) | [UIControl](UIControl) | A caption, an optional icon, the vanilla embossed frame. |
| [CheckboxControl](CheckboxControl) | [UIControl](UIControl) | A box that switches on and off, caption included in the hit target. |
| [TextInputControl](TextInputControl) | [UIControl](UIControl) | A single line field, with the keyboard layout applied. |
| [DropdownControl](DropdownControl) | [UIControl](UIControl) | A closed box and a list that opens under it. Text, icons or item stacks. |
| [DropdownItem](DropdownItem) | [UIControl](UIControl) | One entry of that list. |
| [ColorPickerControl](ColorPickerControl) | [UIControl](UIControl) | Saturation and brightness square, hue strip, swatch. |
| [TabsControl](TabsControl) | [UIControl](UIControl) | A row of tabs with one page showing at a time. |
| [PixelCanvasControl](PixelCanvasControl) | [UIControl](UIControl) | A grid of coloured pixels the player can paint in. |

### Chrome and menus

| class | inherits from | what it is |
|---|---|---|
| [TitleBarControl](TitleBarControl) | [UIControl](UIControl) | Title, burger menu, close cross, and the handle the dialog is dragged by. |
| [ContextMenuControl](ContextMenuControl) | [UIControl](UIControl) | A menu that hangs on another control. Cascades. |
| [ContextMenuItem](ContextMenuItem) | [UIControl](UIControl) | One entry of that menu. |

### Items

| class | inherits from | what it is |
|---|---|---|
| [InventoryGridControl](InventoryGridControl) | [RectangleControl](RectangleControl) | A grid of slots onto a **real** inventory. Scrolls. |
| [ItemSlotControl](ItemSlotControl) | [UIControl](UIControl) | One slot: frame, stack, count, hover ring, item tooltip. |
| [ItemTypeSelectorControl](ItemTypeSelectorControl) | [UIControl](UIControl) | A slot that picks an item *type* rather than holding one. |

### Helpers

| class | inherits from | what it is |
|---|---|---|
| [PopupHost](PopupHost) | `Object` | The panel machinery behind the menu, the dropdown and the type selector. Not a control. |
| [GuiIcons](GuiIcons) | `Object` (static) | Icons by name - the game's own, and any a mod adds. |
| [Supporting Types](Supporting-Types) | | The enums, structs, interfaces and event arguments. |

## The class hierarchy

```
Object
├─ UIControl                          abstract, INotifyPropertyChanged
│  ├─ CustomDialogElement             IDisposable
│  ├─ RectangleControl                IScrollable
│  │  └─ InventoryGridControl
│  ├─ TextLabelControl
│  ├─ ButtonControl
│  ├─ CheckboxControl
│  ├─ TextInputControl
│  ├─ ProgressBarControl
│  ├─ TabsControl
│  ├─ ImageControl
│  ├─ ColorPickerControl
│  ├─ PixelCanvasControl
│  ├─ DropdownControl                 IDisposable
│  ├─ DropdownItem                    IItemTooltipSource
│  ├─ ItemTypeSelectorControl         IDisposable
│  ├─ ContextMenuControl              IDisposable
│  ├─ ContextMenuItem
│  ├─ TitleBarControl
│  └─ ItemSlotControl                 IItemTooltipSource
├─ PopupHost                          sealed, IDisposable
└─ GuiIcons                           static
```

## Namespaces

The framework grew out of an earlier project, so the namespaces are not all the same. What matters
in practice is the pair of `using` lines at the top of a file:

```csharp
using IS2Mod.ControlTypes;          // UIControl, RectangleControl, TextLabelControl,
                                    // ButtonControl, ItemSlotControl, InventoryGridControl,
                                    // GuiIcons, PopupHost, ElementColor, LayoutRect
using IS2Mod.ControlTypes.Custom;   // CustomDialogElement
using IS2Mod.ControlTypes.Events;   // MouseEventArgs, KeyEventArgs, MouseWheelEventArgs
using IS2Mod.Enums;                 // Orientation, DialogRenderLayer
using IS2Mod.Interfaces;            // IScrollable, IItemTooltipSource

using ModernVintageGUI.ControlTypes;  // CheckboxControl, TextInputControl, ProgressBarControl,
                                      // TabsControl, ImageControl, ColorPickerControl,
                                      // DropdownControl, ItemTypeSelectorControl,
                                      // ContextMenuControl, TitleBarControl, PixelCanvasControl
using ModernVintageGUI.Enums;         // ContextMenuAnchor
using ModernVintageGUI.Inventory;     // ModInventory, ModInventoryAccess, ModInventorySystem
```

Each control's page names its own namespace at the top.

## Things that hold for every control

* **Author units in, device pixels out.** Everything you assign - `Margin`, `Padding`, `Size`,
  `MaxSize`, font sizes, border widths - is in unscaled author units. `Position` and `Size` come
  back in device pixels, and so do the coordinates in the mouse event arguments. See
  [Layout and Scaling](Layout-and-Scaling).
* **Property changes are not observed.** `Children` is, so adding or removing a child re-lays out
  and redraws by itself. After changing a text, a colour or a flag, call `Dialog?.Refresh()`.
* **Mouse coordinates are screen coordinates**, while `Position` and `Size` are dialog local. A hit
  test that forgets to convert never matches.
* **A composite is one hit target.** Anything built from parts overrides `HitTestRecursive` and
  returns itself, or the parts take the events meant for the control.
* **Dispose the dialog, not the controls.** [`CustomDialogElement.Dispose()`](CustomDialogElement#methods)
  disposes everything in the tree that owns something.

## See also

* [Layout and Scaling](Layout-and-Scaling) - the two passes, the three sizes, stacking, GUI scale
* [Input, Focus and Rendering](Input-Focus-and-Rendering) - events, capture, keyboard focus, depth
* [Inventories](Inventories) - what makes an inventory grid real
* [Writing a Custom Control](Writing-a-Custom-Control) - the rules a control of your own has to follow
