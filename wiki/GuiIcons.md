# GuiIcons Class

**Namespace:** `IS2Mod.ControlTypes`
**Assembly:** `ModernVintageGUI.dll`

Icons by name - the game's own, and any a mod adds. This is what
[`ButtonControl.IconName`](ButtonControl#properties), [`ImageControl.IconName`](ImageControl#properties)
and [`DropdownItem.IconName`](DropdownItem#properties) take.

```csharp
public static class GuiIcons
```

**Inheritance:** `Object` → **GuiIcons**

## Remarks

Adding an icon is a line, because the game already has the machinery for it:
`IconUtil.CustomIcons` is a public dictionary of name to renderer, and `IconUtil.SvgIconSource`
turns an SVG asset into such a renderer. So a mod ships an SVG, registers it, and from then on the
name works everywhere a name works.

The constants below are shortcuts for the ones the game ships, nothing more. **They are not a list
of what is allowed:** `Exists` asks the running game, so an icon added by a later game version or
by another mod counts as real without anything here changing. Nothing has to be extended to use a
new icon.

An unknown name warns **once** into the log rather than every frame. `IconUtil.DrawIconInt` fails
silently, so without that warning a mistyped icon name looks like a control that simply refuses to
draw one.

## Fields

Constants for the built-in names, so a typo is a build error rather than a blank button.

| | | | |
|---|---|---|---|
| `Airbrush` | `Apple` | `Basket` | `Belt` |
| `Boots` | `Bracers` | `Brush` | `Cape` |
| `Cursor` | `Dice` | `Eraser` | `Erode` |
| `FloodFill` | `Gloves` | `GrowShrink` | `Handheld` |
| `Hat` | `Health` | `Import` | `Lake` |
| `Left` | `Line` | `Mask` | `Medal` |
| `MenuIcon` | `Necklace` | `None` | `Offhand` |
| `Pullover` | `RaiseLower` | `Redo` | `Repeat` |
| `Right` | `Ring` | `Select` | `Shirt` |
| `Tree` | `Trousers` | `Undo` | |

All are `public const string`; the value is the lower case name the game knows, e.g.
`MenuIcon = "menuicon"`.

## Properties

| Name | Type | Description |
|---|---|---|
| `BuiltIn` | `IReadOnlyCollection<string>` | `static`. The game's own names, read out of the **running game** rather than written down here: `IconUtil` keeps one `Draw<name>_svg` method per icon, and reading those cannot go stale the way a copy would. Should a future version rename them this comes back empty, and the only thing lost is the warning about a typo. |

## Methods

| Name | Returns | Description |
|---|---|---|
| `Register(ICoreClientAPI capi, string name, AssetLocation svg)` | `void` | `static`. Registers an SVG from the mod's assets under a name. Overwrites a name that is already taken, which is what lets a mod replace one of the game's icons on purpose. |
| `Register(ICoreClientAPI capi, string name, IconRendererDelegate renderer)` | `void` | `static`. The same for an icon drawn by hand rather than loaded from an asset. |
| `Available(ICoreClientAPI? capi)` | `IEnumerable<string>` | `static`. Every name that will draw something right now: the game's own plus everything registered. Handy for a gallery, or for checking a configured name. |
| `Exists(ICoreClientAPI? capi, string? name)` | `bool` | `static`. Whether this name is likely to draw. Used only to decide whether a missing icon is worth a warning - it never stops one from being drawn. |
| `IsCustom(ICoreClientAPI? capi, string? name)` | `bool` | `static`. Whether a name has been registered by this mod or any other. |

## Examples

```csharp
button.IconName = GuiIcons.MenuIcon;    // a constant, so a typo is a build error
```

Your own icon:

```csharp
GuiIcons.Register(capi, "gear", new AssetLocation("mymod:textures/icons/gear.svg"));

var button = new ButtonControl { Text = "Settings", IconName = "gear" };
```

A gallery of everything that will draw - a name does not tell you what an icon looks like:

```csharp
foreach (string name in GuiIcons.Available(capi))
    row.Children.Add(new ImageControl(_Name: name) { IconName = name });
```

## See also

* [ButtonControl](ButtonControl) · [ImageControl](ImageControl) · [DropdownItem](DropdownItem) - the three that take a name
