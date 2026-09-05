# Writing a Custom Control


```csharp
public class MyControl : UIControl
{
    public override PointD CalculateSize()
    {
        // Measure. Device pixels; scale author units with LayoutScale.
        PointD measured = new PointD(
            ScaledPadding * 2 + 100 * LayoutScale,
            ScaledPadding * 2 + 20 * LayoutScale);

        CalculatedSize = measured;
        SetLayoutSize(measured);
        return measured;
    }

    public override void GenerateRenderData(ImageSurface surface, Context ctx)
    {
        // Position and Size are already device pixels.
        ctx.Rectangle(Position.X, Position.Y, Size.X, Size.Y);
        ctx.Fill();

        base.GenerateRenderData(surface, ctx);   // draws the children
    }
}
```

Rules worth repeating:

- Do not upload anything to the GPU in `GenerateRenderData` - the dialog uploads the finished
  surface exactly once per refresh.
- Measure reads `ExplicitSize`, arrange writes `SetLayoutSize`.
- Override `HitTestRecursive` if the control is one piece.
- Scale everything you drew in author units.
- Set `IsFocusable` if the player operates the control, and leave it off on its parts - a composite
  that is atomic for the mouse has to be atomic for the keyboard too. Then draw the focus state
  from `GotFocus` / `LostFocus`, in something that is not also written by `Enter` / `Exit`; a
  control can be hovered and focused at the same time.
- Handle keys in `KeyDown`, and set `Handled` **only** for keys you actually used. We run ahead of
  the vanilla hotkey manager, so anything you swallow is a key the player cannot use in the game.
  A control that wants everything anyway overrides `WantsAllKeyboardInput`.
- Add a scenario to `ZLayoutHarness/Scenarios.cs` - it will then be checked for idempotence,
  collapsed controls, sibling overlap and correct scaling, and rendered to PNG. Drive the real
  handlers to render a state (`InvokeEventEnter`, `InvokeGotFocus`) rather than setting colours by
  hand, so the picture shows what the control does and not what it is meant to do.
- Draw anything the Cairo surface cannot hold - item stacks and icons, above all - in
  `GenerateInteractiveRenderData`, which runs per frame, and place it in front of the surface with
  the offsets on `CustomDialogElement`. See
  [Depth inside one dialog](Input-Focus-and-Rendering#depth-inside-one-dialog).
- Size and place the parts of a composite in **one** method that all three layout overrides call.
  Three copies of that arithmetic are three chances to disagree, and they will. Watch out for
  `base.CalculateAllPositions()`: it ends in `NormalizeChildrenByDelta()`, which overwrites a
  child's size but not its position - so a part that has to sit off-centre keeps its full size and
  moves its `Position`, which survives that pass.
- Anything that opens next to the control and must not be clipped by the dialog - a list, a menu,
  a palette - belongs in a [PopupHost](PopupHost) rather than in your own children. Then
  implement `IDisposable` and dispose it: the popup owns a dialog, and a dialog owns renderers.
- Mouse coordinates in the event arguments are **screen** coordinates, while `Position` and `Size`
  are dialog local. Convert before hit testing, or the control will draw perfectly and ignore every
  click.

What the harness cannot cover is anything that only exists at runtime in the game: the Harmony
patches, the real mouse grab, focus and depth against vanilla dialogs, and GPU uploads. Icons and
item stacks are on that list too - both need a client, so a control that draws them is laid out and
checked by the harness but its icons are missing from the picture. Those you have to look at in the
game.
