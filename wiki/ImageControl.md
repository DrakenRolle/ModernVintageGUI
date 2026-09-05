# ImageControl Class

**Namespace:** `ModernVintageGUI.ControlTypes`
**Assembly:** `ModernVintageGUI.dll`

A picture from any mod's assets, or one of the game's GUI icons, scaled into the space it is given.

```csharp
public class ImageControl : UIControl
```

**Inheritance:** `Object` → [UIControl](UIControl) → **ImageControl**

**Implements:** `INotifyPropertyChanged` (inherited)

## Remarks

Both go into the Cairo surface rather than being drawn per frame, because a picture does not change
between frames - the per-frame pass is for things that cannot be a bitmap, and a bitmap is exactly
what this is.

A broken or missing icon draws nothing and is **remembered as broken** rather than being retried
every frame. That is not caution for its own sake: `IconUtil.DrawIconInt` runs whatever renderer
another mod registered under that name, and one of the game's own - the waypoint map layer's -
throws when it is asked to draw outside the map. An icon gallery that walks every registered name
would otherwise take the client down with it.

## Constructors

| | Description |
|---|---|
| `ImageControl(AssetLocation? asset = null, string _Name = "", PointD? _Size = null, double _Margin = 0)` | Defaults to 32 x 32 author units with auto-sizing off. |

## Properties

| Name | Type | Description |
|---|---|---|
| `Asset` | `AssetLocation?` | The file to draw. Wins over `IconName`. |
| `IconName` | `string?` | One of the game's own GUI icons - see [GuiIcons](GuiIcons). Drawn in `IconColor`. |
| `IconColor` | [`ElementColor`](Supporting-Types#elementcolor-class) | The single colour a named icon is drawn in. Ignored for an asset. |
| `Fit` | [`ImageFit`](Supporting-Types#imagefit-enum) | How the picture is fitted into the box: `Contain` (default), `Cover`, `Stretch` or `None`. |
| `Opacity` | `double` | `0` to `1`. For a disabled look, or a fade. Default `1`. |

## Methods

| Name | Returns | Description |
|---|---|---|
| `CalculateSize()` | `PointD` | `override`. |
| `GenerateRenderData(ImageSurface surface, Context ctx)` | `void` | `override`. |

## Examples

```csharp
var logo = new ImageControl(new AssetLocation("mymod:textures/gui/logo.svg"));
logo.Size = new PointD(64, 64);
logo.IsAutoSize = false;

var gear = new ImageControl(_Name: "gear") { IconName = GuiIcons.MenuIcon };
```

## See also

* [UIControl](UIControl) - the base class
* [GuiIcons](GuiIcons) - the names `IconName` takes
* [ImageFit](Supporting-Types#imagefit-enum)
