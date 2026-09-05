# ButtonControl Class

**Namespace:** `IS2Mod.ControlTypes`
**Assembly:** `ModernVintageGUI.dll`

A clickable button with a caption, an optional icon and the game's embossed frame.

```csharp
public class ButtonControl : UIControl
```

**Inheritance:** `Object` → [UIControl](UIControl) → **ButtonControl**

**Implements:** `INotifyPropertyChanged` (inherited)

## Remarks

A composite: a [RectangleControl](RectangleControl) for the frame, two more for the embossed light
and dark edges, a fourth for the focus ring, and a [TextLabelControl](TextLabelControl) for the
caption. It sizes itself to the caption unless it is given a fixed size.

A button is an **atomic hit target** - its parts never receive the events themselves - and it sizes
and places them in one internal `LayoutParts()` called from all three layout overrides. That is the
pattern to copy for any composite: one method rather than three copies, because the three passes
have to agree on where the caption sits and three copies of that arithmetic are three chances to
disagree.

It is focusable, so Tab reaches it and Enter or Space raises the same `Clicked` you subscribed to.
Focus draws a fourth overlay in the game's highlight colour rather than changing one of the existing
borders, because a button can be hovered **and** focused at once.

Icon and caption are centred **together**, not each in its own half: the pair is measured, the pair
is placed. `base.CalculateAllPositions()` ends in `NormalizeChildrenByDelta()`, which overwrites a
child's *size* but not its position - so the caption keeps its full width and only its `Position`
is shifted, which survives that pass.

## Constructors

| | Description |
|---|---|
| `ButtonControl(string _Name = "", PointD? _Size = null, Orientation _Orientation = Orientation.Top, double _Margin = 5, double _Padding = 0, int _Index = 0)` | A `_Size` of `null` or `0/0` makes the button auto-size to its caption. |

## Properties

| Name | Type | Description |
|---|---|---|
| `Text` | `string` | The caption. Forwards to the internal label. |
| `IconName` | `string?` | One of the game's GUI icons - see [GuiIcons](GuiIcons). With a caption it sits to the left of it; on its own it is centred, which is the icon-only button. An unknown name draws nothing and warns once. |
| `IconAsset` | `AssetLocation?` | An SVG or texture from the mod's assets, drawn **instead of** `IconName` when both are set. |
| `UnscaledIconSize` | `double` | A fixed icon size in author units. `0` - the default - derives one from the button, so the icon grows with it. |
| `IconHeightFraction` | `double` | The share of the button height a derived icon size takes. Default `0.6`. |
| `UnscaledIconInset` | `double` | How far the icon sits from the left edge when there is a caption too. Default `8.0`. |
| `ShowEmboss` | `bool` | The raised light and dark edges. Default `true`. Setting it re-strokes the two overlays and refreshes the dialog. |

## Methods

| Name | Returns | Description |
|---|---|---|
| `CalculateSize()` | `PointD` | `override`. Caption plus icon plus padding, unless a fixed size was assigned. |
| `NormalizeChildrenByDelta()` | `void` | `override`. Forces every part to the button's own box. |
| `CalculateAllPositions()` | `void` | `override`. Places the frame, the emboss, the ring and the caption. |
| `HitTestRecursive(UIControl, double, double)` | `UIControl?` | `protected override`. Returns the button itself - it is one hit target. |
| `GenerateRenderData(ImageSurface surface, Context ctx)` | `void` | `override`. The button and its parts, then the icon on top of them. |

Inherited from [UIControl](UIControl): `Clicked`, `Enter`, `Exit`, `PerformClick()` and the rest.
A button declares no events of its own.

## Examples

```csharp
var save = new ButtonControl(_Name: "saveButton");
save.Text = "Save";
save.Clicked += (sender, e) => capi.ShowChatMessage("Save clicked");
```

![Two stacked buttons, the upper one hovered](https://raw.githubusercontent.com/DrakenRolle/ModernVintageGUI/master/docs/images/readme-buttons-hover.png)

Plain, focused and hovered are three separate states:

![Three buttons: plain, focused with a ring, and hovered](https://raw.githubusercontent.com/DrakenRolle/ModernVintageGUI/master/docs/images/readme-keyboard-focus.png)

An icon on a button:

```csharp
var button = new ButtonControl(_Name: "open");
button.Text     = "Open a menu";
button.IconName = GuiIcons.MenuIcon;                                     // one of the game's own
button.IconAsset = new AssetLocation("mymod:textures/icons/gear.svg");   // or your own SVG
```

A fixed size. `PointD(0, 0)` passed to a constructor makes a control auto-sizing rather than zero
sized, so assign the size and switch auto-sizing off:

```csharp
button.Size = new PointD(150, 150);
button.IsAutoSize = false;
```

`ShowEmboss = false` is what a dense panel wants - the vanilla emboss is a light stroke around
every button, and a dozen of them in a small space is a grid of white lines.

## See also

* [UIControl](UIControl) - the base class
* [GuiIcons](GuiIcons) - the names `IconName` takes
* [TextLabelControl](TextLabelControl) and [RectangleControl](RectangleControl) - the parts
