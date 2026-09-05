# TextLabelControl Class

**Namespace:** `IS2Mod.ControlTypes`
**Assembly:** `ModernVintageGUI.dll`

A piece of text. Measures itself from its string and its font and draws one line, or wrapped text
when `WordWrap` is on.

```csharp
public class TextLabelControl : UIControl
```

**Inheritance:** `Object` → [UIControl](UIControl) → **TextLabelControl**

**Implements:** `INotifyPropertyChanged` (inherited)

## Remarks

`Orientation` here is a [`TextOrientation`](Supporting-Types#textorientation-enum) - `Left`,
`Center`, `Right` and the nine `Top`/`Middle`/`Bottom` combinations - and it **hides the inherited
`Orientation`** on purpose. `UIControl.Orientation` is a control's own alignment and is currently
inert; this one decides where the text sits in the label's box.

Text is measured with `TextExtents.XAdvance`, not `Width`: `Width` is the inked bounding box and
leaves out the side bearings, which makes the box too narrow for the text it holds. The vertical
centring is on the **cap height** taken from the font metrics, not on the line box, or the visible
letters end up sitting high.

## Constructors

| | Description |
|---|---|
| `TextLabelControl(string text = "", string fontName = "Arial", int fontSize = 16, FontWeight fontWeight = FontWeight.Normal, FontSlant fontSlant = FontSlant.Normal, ElementColor? textColor = null, TextOrientation orientation = TextOrientation.Left, bool wordWrap = false, int lineHeight = 20, int padding = 0, string _Name = "", PointD? _Size = null, Orientation _Orientation = Orientation.Top, double _Margin = 0, double _Padding = 0, int _Index = 0)` | `textColor` defaults to `ElementColor.White`. |
| `TextLabelControl()` | Arial 16, black, left aligned, `Padding = 5`. |

## Properties

| Name | Type | Description |
|---|---|---|
| `Text` | `string` | The string to draw. |
| `FontName` | `string` | Font family. `GuiStyle.StandardFontName` for the game's own. |
| `FontSize` | `int` | Font size in author units. |
| `FontWeight` | `Cairo.FontWeight` | Normal or bold. |
| `FontSlant` | `Cairo.FontSlant` | Normal, italic or oblique. |
| `TextColor` | [`ElementColor`](Supporting-Types#elementcolor-class) | The ink. |
| `Orientation` | [`TextOrientation`](Supporting-Types#textorientation-enum) | Where the text sits in the box. Declared `new` - it hides `UIControl.Orientation`. |
| `WordWrap` | `bool` | Wrap at the box width instead of drawing one line. |
| `LineHeight` | `int` | Line height in author units, used by wrapped text. |

## Methods

| Name | Returns | Description |
|---|---|---|
| `MeasureNaturalSize()` | `PointD` | The size the text itself needs, whatever the box was stretched to. A container that places the label by hand needs this - [ButtonControl](ButtonControl) reads it to centre an icon and a caption together, and it cannot read that off `Size`, which is whatever the button stretched the label to. |
| `CalculateSize()` | `PointD` | `override`. An explicitly assigned size wins; otherwise the text decides. |
| `GenerateRenderData(ImageSurface surface, Context ctx)` | `void` | `override`. |

## Examples

```csharp
var label = new TextLabelControl(
    text: "in between",
    fontName: GuiStyle.StandardFontName,
    fontSize: 16,
    textColor: new ElementColor(GuiStyle.DialogDefaultTextColor),
    orientation: TextOrientation.MiddleLeft,
    padding: 5);
```

```csharp
// Property changes are not observed.
label.Text = "Hey don't touch my fancy Text!";
label.Dialog?.Refresh();
```

## See also

* [UIControl](UIControl) - the base class
* [TextOrientation](Supporting-Types#textorientation-enum)
* [ButtonControl](ButtonControl) - a composite built around one of these
