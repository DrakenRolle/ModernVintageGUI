# PixelCanvasControl Class

**Namespace:** `ModernVintageGUI.ControlTypes`
**Assembly:** `ModernVintageGUI.dll`

A grid of coloured pixels the player can paint in, in the spirit of r/place.

```csharp
public class PixelCanvasControl : UIControl
```

**Inheritance:** `Object` → [UIControl](UIControl) → **PixelCanvasControl**

**Implements:** `INotifyPropertyChanged` (inherited)

## Remarks

A pixel is `UnscaledPixelSize` author units across, so it grows with the GUI scale like everything
else - down to a floor of **one screen pixel**, because below that it is not a pixel any more. It is
also rounded to whole screen pixels: a fractional size has Cairo smear one canvas pixel across two
screen ones, and the whole point of this control is that they stay square and hard edged.

The colours live in an array and are blitted as **one image** scaled with a nearest neighbour
filter, not as one rectangle per pixel. That is what makes a large canvas affordable: a 200x200
canvas is forty thousand rectangles per redraw the other way round, and a dialog redraws whenever
anything in it changes. The image is only rebuilt when a pixel actually changed, and painting a
pixel the colour it already is changes nothing - which is what keeps a stroke from redrawing the
dialog once per report of the mouse.

The canvas owns an image the size of itself, so it is disposable - and disposing the
[dialog](CustomDialogElement) disposes it along with the rest of the tree.

## Constructors

| | Description |
|---|---|
| `PixelCanvasControl(int columns, int rows, double unscaledPixelSize)` | The size of the canvas in pixels, and how wide one of them is in author units. |

## Properties

| Name | Type | Description |
|---|---|---|
| `Columns` | `int` | The width of the canvas in pixels. |
| `Rows` | `int` | Its height. |
| `UnscaledPixelSize` | `double` | How wide one pixel is, in author units. |
| `PixelSize` | `double` | What that comes to in screen pixels: whole pixels, never below one. Read-only. |
| `DrawMode` | `bool` | Let the player paint. **Off by default**, so a canvas that is only showing something cannot be scribbled on. |
| `DrawColor` | [`ElementColor`](Supporting-Types#elementcolor-class) | What the player paints with. |
| `PaintButton` | `EnumMouseButton` | Which button paints. The right one, leaving the left for whatever the dialog around it wants. |
| `ShowGrid` | `bool` | Lines between the pixels, drawn only while there is room for them. |
| `GridColor` | [`ElementColor`](Supporting-Types#elementcolor-class) | Their colour. |
| `HighlightColor` | [`ElementColor`](Supporting-Types#elementcolor-class) | The outline colour. Changing it repaints straight away. |
| `UnscaledHighlightWidth` | `double` | How thick the outline is. Never thicker than a third of a pixel. |
| `HasHighlight` | `bool` | Whether an area is outlined. Read-only. |
| `HighlightedArea` | `Vec2i[]` | Which pixels that is. Read-only. |

## Methods

### The picture

| Name | Returns | Description |
|---|---|---|
| `Resize(int columns, int rows)` | `void` | Keeps what still fits. |
| `SetPixel(int x, int y, ElementColor color)` | `void` | One pixel. Out of range is ignored, not thrown at. |
| `GetPixel(int x, int y)` | `ElementColor` | What is there. |
| `SetPixels(int x, int y, ElementColor[,] block)` | `void` | A rectangle, indexed `[row, column]` the way a picture reads. |
| `SetPixels(ElementColor[] colors)` | `void` | The whole canvas from one row major array. |
| `ToArray()` | `ElementColor[]` | And back out, for sending it somewhere. |
| `Fill(ElementColor color)` | `void` | Every pixel. |
| `Clear()` | `void` | Back to empty. |

### Screen coordinates

| Name | Returns | Description |
|---|---|---|
| `TryGetPixelAt(double screenX, double screenY, out int x, out int y)` | `bool` | Which pixel a point on the screen is over. |
| `SetPixelAtScreen(double screenX, double screenY, ElementColor color)` | `void` | Paints the pixel under a screen point. |

### Stamping

| Name | Returns | Description |
|---|---|---|
| `DrawImage(AssetLocation asset, int x, int y, int width, int height)` | `bool` | Stamps an image, scaled down to that many pixels. |
| `DrawIcon(string name, int x, int y, int width, int height, ElementColor color)` | `bool` | The same for one of the game's [icons](GuiIcons). |

Both need a client, so both return `false` while the control is not in a dialog that has been shown -
and both are guarded, because an icon is drawn by whoever registered it and that can be another mod.
The scaling is Cairo's, so a picture reduced to twelve pixels across looks reduced rather than
thrown away, and transparent parts leave what was underneath: a stamp composites, it does not
replace the rectangle it lands in.

### Areas and outlines

| Name | Returns | Description |
|---|---|---|
| `GetArea(int x, int y, bool colorSensitive = true)` | `Vec2i[]` | The area a pixel belongs to. Colour sensitive - the usual case - it takes pixels of the same colour, so pointing at one pixel of a red line gives back the whole line. Colour blind it takes anything painted at all. |
| `SetHighlight(IEnumerable<Vec2i> pixels, bool colorSensitive = true)` | `bool` | Outlines this set. Returns `false`, and nothing changes, when the set does not hang together. |
| `HighlightAreaAt(int x, int y, bool colorSensitive = true)` | `bool` | `GetArea` and `SetHighlight` in one, for a hover. |
| `ClearHighlight()` | `void` | Takes the outline away. |
| `AreConnected(IEnumerable<Vec2i> pixels)` | `bool` | `static`. Whether a set hangs together at all. |

The pixels of an area have to hang together **edge to edge**. Two that meet only at a corner are two
areas - the same rule the outline is drawn by, so a diagonal line is a row of single pixels and
looks like one.

The line runs along the **inside** of the area's border, and there are no lines between two pixels
of it: for every pixel, only the sides facing out of the area are drawn. Twenty pixels come out as
one shape rather than twenty squares, and that falls out of the rule instead of needing a pass to
rub the inner lines out afterwards. Where two drawn sides meet at a corner of the area both stop at
the inset and meet exactly; where a side carries on into the next pixel of the area the line runs
to the pixel boundary so the two join seamlessly.

### Getting the picture out

| Name | Returns | Description |
|---|---|---|
| `ToImageSurface(int scale = 1)` | `ImageSurface` | A **fresh** image - not the one the control draws from - so keeping it, saving it or handing it to `capi.Gui.LoadCairoTexture` cannot pull the canvas out from under itself. Yours to dispose. |
| `SavePng(string path, int scale = 1)` | `void` | Straight to a file. |
| `ToArgb()` | `int[]` | The raw pixels. |

The scale is whole pixels and nearest neighbour, so a canvas exported at 8 is the same picture with
fat square pixels rather than a blurred one.

## Events

| Name | Type | Description |
|---|---|---|
| `PixelPainted` | `EventHandler<PixelPaintedEventArgs>` | Every pixel that changed, with `X`, `Y`, `Color` and **`ByPlayer`** saying who changed it. A canvas shared over the network needs that difference: what the player did here has to be sent, and what arrived from the server must not be sent straight back. |

## Examples

```csharp
var canvas = new PixelCanvasControl(columns: 32, rows: 32, unscaledPixelSize: 8);

canvas.SetPixel(4, 2, new ElementColor(220, 60, 60, 255));

canvas.DrawMode  = true;                                  // let the player paint
canvas.DrawColor = new ElementColor(60, 120, 220, 255);
canvas.PixelPainted += (sender, e) => { if (e.ByPlayer) Send(e.X, e.Y, e.Color); };
```

Outlining what the mouse is over:

```csharp
canvas.HighlightColor = new ElementColor(255, 240, 150, 255);
canvas.HighlightAreaAt(x, y);
```

Getting it out:

```csharp
using ImageSurface image = canvas.ToImageSurface(scale: 4);   // yours to dispose
canvas.SavePng("canvas.png", scale: 8);
int[] raw = canvas.ToArgb();
```

## Painting

With `DrawMode` on, holding `PaintButton` paints and dragging keeps painting. A drag paints **every
pixel between two reports of the mouse**, not only the ones the cursor was seen at: a mouse moving
quickly reports a handful of positions a second, and painting only those leaves a dotted line with
holes in it - which reads as a slow computer rather than as a bug, and so never gets reported. The
stroke also captures the mouse, so leaving the canvas and coming back is one stroke and not two.

## See also

* [UIControl](UIControl) - the base class
* [ElementColor](Supporting-Types#elementcolor-class)
* [GuiIcons](GuiIcons) - what `DrawIcon` takes
