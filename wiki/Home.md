# Modern Vintage Story UI

A stack-container based GUI framework for Vintage Story. You define the data, the framework handles
positioning, sizing and order - the way WinForms, WPF or UWP work, rather than hand-placed bounds.

![A dialog with a vanilla style title bar](https://raw.githubusercontent.com/DrakenRolle/ModernVintageGUI/master/docs/images/readme-title-bar.png)

## Where to start

| page | what is in it |
|---|---|
| [Layout and Scaling](Layout-and-Scaling) | the two layout passes, the three sizes, stacking, margins, GUI scale |
| [Input, Focus and Rendering](Input-Focus-and-Rendering) | events, mouse capture, keyboard focus, focus driven z-order, depth |
| [Controls](Controls) | the control reference - one page per control, with its members and what it inherits from |
| [Supporting Types](Supporting-Types) | the enums, structs, interfaces and event arguments the controls are written in |
| [Inventories](Inventories) | real inventories, the three ways to own one, and change events |
| [Writing a Custom Control](Writing-a-Custom-Control) | the rules a control has to follow |

Every control has its own reference page:

[UIControl](UIControl) ·
[CustomDialogElement](CustomDialogElement) ·
[RectangleControl](RectangleControl) ·
[TextLabelControl](TextLabelControl) ·
[ButtonControl](ButtonControl) ·
[CheckboxControl](CheckboxControl) ·
[TextInputControl](TextInputControl) ·
[ProgressBarControl](ProgressBarControl) ·
[TabsControl](TabsControl) ·
[ImageControl](ImageControl) ·
[ColorPickerControl](ColorPickerControl) ·
[PixelCanvasControl](PixelCanvasControl) ·
[DropdownControl](DropdownControl) ·
[DropdownItem](DropdownItem) ·
[ItemTypeSelectorControl](ItemTypeSelectorControl) ·
[ContextMenuControl](ContextMenuControl) ·
[ContextMenuItem](ContextMenuItem) ·
[TitleBarControl](TitleBarControl) ·
[InventoryGridControl](InventoryGridControl) ·
[ItemSlotControl](ItemSlotControl) ·
[PopupHost](PopupHost) ·
[GuiIcons](GuiIcons)

## Setup

Declare MVS_UI as a dependency in your `modinfo.json` and add it as a reference to your mod project:

```json
"dependencies": {
    "game": "1.22.0",
    "modernvintagegui": "1.0.0"
}
```

That is all. MVS_UI initialises itself - **do not** apply its Harmony patches or create a `UIManager`
in your own mod, and **do not** bundle a copy of the assembly. Both are explained under
[Input, Focus and Rendering](Input-Focus-and-Rendering).

## A first dialog

```csharp
var dialog = new CustomDialogElement(capi, "MyTestDialog", "My Title");
dialog.Children.Add(new TextLabelControl("Hi im Fancy!"));
dialog.Show();
```

![A dialog with a single text label](https://raw.githubusercontent.com/DrakenRolle/ModernVintageGUI/master/docs/images/readme-simple-dialog.png)

## Where the pictures come from

Every illustration in this wiki is rendered by `ZLayoutHarness` through the real layout and drawing
code, without starting the game:

```
dotnet run --project ModernVintageGUI/ZLayoutHarness -- --docs docs/images
```

So they can be regenerated whenever a control changes instead of being re-screenshotted.
