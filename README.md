<h1>Hello there :)</h1>

This is an approach to fix the current GUI system for Vintage Story.
The core idea of this framework is a stack-container based way to structure and maintain a user
interface. For now I call it **Modern Vintage Story UI**, or **MVS_UI** for short.

<img src="docs/images/readme-title-bar.png" alt="A dialog with a vanilla style title bar" />

> **Full documentation:** the [wiki](https://github.com/DrakenRolle/ModernVintageGUI/wiki) - every
> control in detail, the layout rules, GUI scale, events, focus and depth, and how to write your
> own control.

<h2>Goals of this Project</h2>

* Make a more modder friendly approach to building user interfaces
  * Achieved by letting the developer only define the data he wants to show, and letting this API
    handle positioning, sizing and order
  * Control focused approach like in other .NET UI frameworks - WinForms, WPF, UWP and so on
* Dialog scaling and control positioning should work independently of GUI scale or window size
* <b>UI DESIGNER :D</b>
  * Not final, but the idea was to use the WPF designer with custom controls, so you can build
    Vintage Story user interfaces in XAML with a real visual designer
* Either JSON or XML exportable format
* Easy to implement custom controls
* Long term sustainability by decoupling this system from the vanilla one as far as possible, so
  game updates do not break all the user interfaces
* Long term thought: fixed positioning reintegrated, but compatible with the designer

<h2>What is included in here</h2>

* **Own parenting and positioning system**, not attached to `ElementBounds`
* **Autosizing** from content - containers from their children, text labels from their text and font
* **Repeatable layout.** Measure and arrange are separated, so laying the same tree out twice gives
  the same answer. That is what makes reopening a dialog and editing it at runtime safe
* **Proper GUI scale support.** Everything you specify is in unscaled author units, exactly like
  `GuiElement.scaled()` in vanilla. Moving the GUI scale slider updates open dialogs live
* **Real mouse capture.** The cursor is released while a dialog is open, clicks are consumed by the
  dialog instead of reaching the world, and block interaction is suppressed
* **Focus driven z-order.** A focused dialog draws above the vanilla GUI and takes clicks in the
  overlap; an unfocused one goes back below it - the same rule the game applies to its own windows
* **Keyboard focus.** Tab and the arrow keys walk the interactive controls in reading order, Enter
  and Space activate the focused one, Escape closes the dialog. Only the keys that actually did
  something are consumed, so the game stays playable with a dialog open
* **Dynamic recomposing** after the UI was opened, so you can change and edit the UI as you like
  even if the dialog is already open
* **Decoupled rendering.** The control tree draws itself onto a single Cairo surface which is
  uploaded once per refresh; the vanilla GUI system is only used where it actually helps
* **A headless layout harness** that renders the UI to PNG and checks the layout invariants without
  starting the game

**Controls so far:** `RectangleControl`, `TextLabelControl`, `ButtonControl`, `ContextMenuControl`,
`TitleBarControl`.

<h2>What is still ongoing</h2>

* Text input - `ClientMain.OnKeyPress`, the one carrying typed characters with the keyboard layout
  applied, does not trigger anything on `IClientEventAPI`, so a text field needs a second Harmony
  patch. Everything else about the keyboard works without one
* `Orientation` (a control's own alignment) is inert, and `Orientation.Fill` is not implemented
* Redraw invalidation - every hover state change currently recomposes the whole surface
* Re-centering on window resize
* XAML editor or custom UI designer
* Cross compatibility with the vanilla UI (e.g. drag an item from a vanilla UI into a modern one)
* More styling options (custom backgrounds, fonts)
* New controls
  * Imagebox, Checkbox, Dropdown, Tabs, Color picker
  * Inventory grid (with auto scrollbar), Itemslot
  * Loading bar / progress bar
* Updates to existing controls
  * Imagebutton (or an updated button with an image source)
  * Panel and window should be auto-scrollbar capable (bool switch)
  * Edge drag resize window

<h1>Getting started</h1>

<h2>Setup</h2>

Declare MVS_UI as a dependency in your `modinfo.json`:

```json
"dependencies": {
    "game": "1.22.0",
    "modernvintagegui": "1.0.0"
}
```

And add it as a Reference to your Mod Project.

That is all - MVS_UI initialises itself. **Do not** apply the Harmony patch or create a `UIManager`
in your own mod, and **do not** bundle a copy of the assembly; see
[why](https://github.com/DrakenRolle/ModernVintageGUI/wiki/Input-Focus-and-Rendering).

<h2>A dialog</h2>

```csharp
var dialog = new CustomDialogElement(capi, "MyTestDialog", "My Title");

var text = new TextLabelControl("Hi im Fancy!");
dialog.Children.Add(text);

dialog.Show();
```

<img src="docs/images/readme-simple-dialog.png" alt="A dialog with a single text label" />

<h2>Buttons</h2>

```csharp
var save = new ButtonControl(_Name: "saveButton");
save.Text = "Save";
save.Clicked += (sender, e) => capi.ShowChatMessage("Save clicked");
dialog.Children.Add(save);
```

<img src="docs/images/readme-buttons-hover.png" alt="Two stacked buttons, the upper one hovered" />

<h2>Stacking</h2>

A container stacks its children along `InsideOrientation` - `Top` (the default) downwards, `Left`
sideways.

```csharp
var row = new RectangleControl();
row.InsideOrientation = Orientation.Left;

foreach (string caption in new[] { "One", "Two", "Three" })
{
    var button = new ButtonControl();
    button.Text = caption;
    row.Children.Add(button);
}

dialog.Children.Add(row);
```

<img src="docs/images/readme-stacking.png" alt="A vertical child above a horizontal row of three buttons" />

Controls of different kinds mix freely in one row:

<img src="docs/images/readme-mixed-row.png" alt="A button, a text label and another button in one row" />

<h2>Keyboard</h2>

Tab and the arrow keys move the focus, Enter and Space activate, Escape closes. Controls the
player operates are in the tab order; decoration is not.

```csharp
var button = new ButtonControl { Text = "Save" };   // focusable already
myPanel.IsFocusable = false;                        // and containers are not

dialog.CloseOnEscape = false;                       // for a dialog that must be dismissed on purpose
```

<img src="docs/images/readme-keyboard-focus.png" alt="Three buttons: plain, focused with a ring, and hovered" />

Hover and focus are separate states, so a control can be in both. Nothing is focused when a dialog
opens, which means Enter and Space stay with the game until the player tabs into the dialog or
clicks a control.

<h2>Context menus</h2>

A menu hangs on any control, positions itself at an anchor and supports cascades. One subscription
sees picks from every level.

```csharp
var menu = new ContextMenuControl(button, items, "positionMode", ContextMenuAnchor.BottomLeft);
button.Clicked += (sender, e) => menu.Toggle();

menu.ItemActivated += (sender, e) => capi.ShowChatMessage(string.Join(" > ", e.Path.Select(i => i.Text)));
```

<img src="docs/images/readme-context-menu-hover.png" alt="A context menu with an entry hovered" />

<h2>Editing the UI after it was opened</h2>

Adding or removing a child relays out and redraws the dialog it belongs to. You do not have to close
and reopen anything. Property changes are not observed yet, so ask for a redraw yourself after those.

```csharp
row.Children.Add(new ButtonControl { Text = "Added at runtime" });
myLabel.Text = "Hey don't touch my fancy Text!";
dialog.Refresh();
```

<img src="docs/images/readme-runtime-before.png" alt="A row with two buttons" />
<img src="docs/images/readme-runtime-after.png" alt="The same row with a third, wider button appended" />

<h2>GUI scale</h2>

One design, any scale - author units in, device pixels out:

<img src="docs/images/readme-scales.png" alt="The same UI rendered at GUI scale 1.0, 1.5 and 2.0" />

<h1>Layout harness</h1>

`ZLayoutHarness` runs the real layout code without the game, renders each scenario to PNG and checks
the invariants:

```
dotnet run --project ModernVintageGUI/ZLayoutHarness
```

Exit code 0 when everything passes, 1 otherwise, so it works in CI. Per scenario it checks
idempotence over five passes, that nothing collapsed to zero, that no siblings overlap in a stacking
container, that laying out at 1.5x and 2x gives the same design that much larger, and that a tree
reused across a scale change matches a freshly built one. Add a scenario in `Scenarios.cs` whenever
you add a control.

Every picture in this README is rendered by the same harness through the real drawing code, so they
can be regenerated instead of re-screenshotted:

```
dotnet run --project ModernVintageGUI/ZLayoutHarness -- --docs docs/images
```

What the harness cannot cover is anything that only exists at runtime in the game: the Harmony
patches, the real mouse grab, focus and depth against vanilla dialogs, and GPU uploads.

<h1>Building</h1>

Set the `VINTAGE_STORY` environment variable to your game installation directory, then:

```
dotnet build ModernVintageGUI.sln
```

The mod project builds into `ModernVintageGUI/ModernVintageGUI/bin/<config>/Mods/mod`. If the game
is running with that folder on its mod path it locks the output DLL - close the client before
rebuilding, or build the other configuration.
