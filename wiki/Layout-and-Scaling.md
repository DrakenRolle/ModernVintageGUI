# Layout and Scaling

Layout runs in two passes over the tree, started by `PerformLayout()` on the root.

**Measure** (`CalculateSize()`) asks every control how big it wants to be, bottom up. **Arrange**
(`NormalizeChildrenByDelta()` then `CalculateAllPositions()`) stretches children across the
available space, gives them positions and clips whatever leaves the parent.

## The three sizes

Three separate values keep the two passes from eating each other:

| | written by | read by |
|---|---|---|
| `ExplicitSize` | you, through the `Size` setter | measure, when `IsAutoSize` is false |
| `CalculatedSize` | `CalculateSize()` | the overflow / clipping check |
| `Size` | measure and arrange, through `SetLayoutSize()` | rendering and hit testing |

**The rule, if you write a control:** measure reads `ExplicitSize`, never `Size`. Arrange writes
through `SetLayoutSize()`, never through the `Size` setter.

Writing a measured value through the `Size` setter turns it into an explicit size, and the control
then stops measuring itself from the next pass on - which shows up as boxes that keep an old size
while their content is drawn at the current one.

The pass is idempotent: running it twice on an unchanged tree gives the same result. That is what
makes reopening a dialog and editing it at runtime safe, and `ZLayoutHarness` checks it on every
run.

## Stacking direction

`InsideOrientation` decides how a control stacks its **children**:

| value | effect |
|---|---|
| `Top` (default) | children stack downwards, all stretched to the content width |
| `Bottom` | same, positioned from the bottom |
| `Left` / `Right` | children stack sideways, all stretched to the content height |
| `None` | children overlay each other, no stretching |

![A vertical child above a horizontal row of three buttons](https://raw.githubusercontent.com/DrakenRolle/ModernVintageGUI/master/docs/images/readme-stacking.png)

Careful with the constructors: the parameter named `_Orientation` sets **`InsideOrientation`**, not
the control's own alignment. Set `InsideOrientation` explicitly when you want to be sure:

```csharp
var row = new RectangleControl();
row.InsideOrientation = Orientation.Left;
```

Two known gaps: the `Orientation` property that would be a control's own alignment inside its parent
is currently never assigned and has no effect, and `Orientation.Fill` exists in the enum but is not
implemented.

Controls of different kinds mix freely in one row:

![A button, a text label and another button in one row](https://raw.githubusercontent.com/DrakenRolle/ModernVintageGUI/master/docs/images/readme-mixed-row.png)

## Margin and padding

`Padding` is the inset between a control and its children, `Margin` the gap a control keeps around
itself. Both are in author units and scaled during layout.

Measure reserves `2 × Margin` per child; arrange places the next sibling at
`previous.End + previous.Margin + own.Margin`, which comes out to the same total.

## Fixed sizes

A `PointD(0, 0)` passed to a constructor makes a control **auto-sizing**, not zero sized. For a
fixed size, assign it and turn auto-sizing off:

```csharp
button.Size = new PointD(150, 150);
button.IsAutoSize = false;
```

## Editing after the dialog is open

Adding or removing a child relays out and redraws the dialog it belongs to - no need to close and
reopen anything.

Property changes are **not** observed. After changing text or a colour, ask for a redraw yourself:

```csharp
row.Children.Add(new ButtonControl { Text = "Added at runtime" });   // relayouts by itself
myLabel.Text = "Hey don't touch my fancy Text!";
dialog.Refresh();                                                    // this one needs a nudge
```

![A row with two buttons](https://raw.githubusercontent.com/DrakenRolle/ModernVintageGUI/master/docs/images/readme-runtime-before.png)
![The same row with a third, wider button appended](https://raw.githubusercontent.com/DrakenRolle/ModernVintageGUI/master/docs/images/readme-runtime-after.png)

## GUI scale

Everything you assign - `Margin`, `Padding`, `Size`, `FontSize`, `LineHeight`, `BorderWidth`,
`RoundedCorners`, `BlurRange` - is in **unscaled author units**. The layout multiplies by
`LayoutScale` on the way to device pixels, exactly like `GuiElement.scaled()` in the vanilla GUI.

After layout, `Position`, `Size` and `CalculatedSize` are **device pixels**. That is what the
renderer and the hit test need, and it is the same space mouse coordinates arrive in, so nothing has
to be transformed back.

![The same UI rendered at GUI scale 1.0, 1.5 and 2.0](https://raw.githubusercontent.com/DrakenRolle/ModernVintageGUI/master/docs/images/readme-scales.png)

`CustomDialogElement` keeps `LayoutScale` in sync with `RuntimeEnv.GUIScale` on every layout, and
`UIManager` watches the `guiScale` setting so open dialogs follow the slider live.

Two things deliberately do **not** scale, because vanilla does not scale them either: the pattern
scale of the dialog background texture, and `GuiStyle.DialogBGRadius`.

## Coordinate spaces

Children are laid out in **dialog local** space, with the root at 0/0 - that is the space the Cairo
surface is drawn in. The dialog itself carries the **screen** position.

Mouse event arguments arrive in screen coordinates. `UIControl.GetScreenPosition()` converts a
control's position the other way:

```csharp
PointD onScreen = anchor.GetScreenPosition();   // dialog position + control position
```
