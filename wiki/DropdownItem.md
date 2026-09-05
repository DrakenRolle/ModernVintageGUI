# DropdownItem Class

**Namespace:** `ModernVintageGUI.ControlTypes`
**Assembly:** `ModernVintageGUI.dll`

One entry of a [DropdownControl](DropdownControl) - a caption, an optional icon, and whatever
payload the caller wants back out of the selection.

```csharp
public class DropdownItem : UIControl, IItemTooltipSource
```

**Inheritance:** `Object` → [UIControl](UIControl) → **DropdownItem**

**Implements:** [`IS2Mod.Interfaces.IItemTooltipSource`](Supporting-Types#iitemtooltipsource-interface),
`INotifyPropertyChanged` (inherited)

## Remarks

Built like a [ContextMenuItem](ContextMenuItem) - a hover fill plus a label on the shared list
background, the way `GuiElementListMenu` draws its rows - with two additions: an icon column, and
the item tooltip that comes with an entry made from a stack.

`new DropdownItem(stack)` takes its caption from the stack and brings the game's own item tooltip
with it, because the entry implements `IItemTooltipSource`: hovering it shows what hovering the
item in a chest shows. **Stack sizes are not drawn** - an entry is a type on offer, not an amount
held.

The highlight covers the whole row, icon and caption both, and bleeds over the item's margin to do
it. A highlight that stops at the text is the one thing a mod developer looking at this called out.

An entry is an atomic hit target, or the label would take the `Enter`, `Exit` and `Clicked` meant
for the entry - so it would never light up, never show a tooltip and never fire.

## Constructors

| | Description |
|---|---|
| `DropdownItem(string text, object? value = null, string? iconName = null)` | A plain text entry, optionally with one of the game's [GUI icons](GuiIcons). |
| `DropdownItem(ItemStack stack, object? value = null, string? text = null)` | An entry that stands for an item stack: the stack is its icon, its name is the caption unless one is given, and hovering it shows the item tooltip. |

## Properties

| Name | Type | Description |
|---|---|---|
| `Text` | `string` | The caption. |
| `Value` | `object?` | Whatever the caller wants to get back out of the selection. Read-only. |
| `Stack` | `ItemStack?` | The stack this entry stands for, if it was built from one. Read-only. |
| `IconName` | `string?` | A vanilla GUI icon name, for entries that are not items. Read-only. |
| `HasIcon` | `bool` | `true` when the entry brings anything to put in the icon column. Read-only. |
| `IsSelected` | `bool` | Whether this is the picked entry. Read-only; set by the owning dropdown. |
| `TooltipSlot` | `ItemSlot?` | From `IItemTooltipSource`: the slot the game's item tooltip describes. Read-only. |

## Methods

| Name | Returns | Description |
|---|---|---|
| `CalculateSize()` | `PointD` | `override`. Icon column plus caption, at the metrics of the list it is in. |
| `NormalizeChildrenByDelta()` / `CalculateAllPositions()` | `void` | `override`. |
| `HitTestRecursive(UIControl, double, double)` | `UIControl?` | `protected override`. One hit target. |
| `GenerateRenderData(ImageSurface, Context)` | `void` | `override`. Hover fill and caption. |
| `GenerateInteractiveRenderData(ICoreClientAPI, float)` | `void` | `override`. The stack, which cannot go into a Cairo surface. |

## Examples

```csharp
new DropdownItem("Granite", value: "granite");
new DropdownItem("Roll",    value: "roll", iconName: GuiIcons.Dice);
new DropdownItem(new ItemStack(flint), value: "flint");   // icon and item tooltip
```

## See also

* [DropdownControl](DropdownControl) - the list these sit in
* [IItemTooltipSource](Supporting-Types#iitemtooltipsource-interface) - what puts the item tooltip on a control
