# C# syntax review: making a dialog easier to write

This is the result of going through every place a dialog is built in this repository - the
showcase, the layout probe, the README, the harness scenarios - and asking what a mod author
types most and why it is longer than it has to be. The first half is what changed in this pass;
the second half is what was found and deliberately *not* changed, with the reason.

## What a dialog looked like

The showcase builds a settings-like row this way:

```csharp
var row = new RectangleControl(_Name: "stateToggles")
{
    InsideOrientation = Orientation.Left
};

var enable = new CheckboxControl(_Name: "stateEnabled")
{
    Text = "Enabled",
    IsChecked = true
};
enable.CheckedChanged += (_, isChecked) => target.IsEnabled = isChecked;

var visible = new CheckboxControl(_Name: "stateVisible")
{
    Text = "Visible",
    IsChecked = true
};
visible.CheckedChanged += (_, isChecked) => target.IsVisible = isChecked;

row.Children.Add(enable);
row.Children.Add(visible);
group.Children.Add(row);
```

Four things stand out, and they repeat in every dialog in the repository:

1. **Every label in the dialog font is six named arguments.** `TextLabelControl` defaults to
   Arial in white; a dialog wants `GuiStyle.StandardFontName` in `GuiStyle.DialogDefaultTextColor`,
   so every heading and caption spells that out. The showcase has a private `Heading` helper for
   exactly this reason, and so does every other file that needs one.
2. **A fixed size is two statements** - `Size = ...` and `IsAutoSize = false` - and the second is
   easy to forget, at which point the control silently ignores the first.
3. **Adding a child returns nothing.** `Children.Add(x)` is a statement, so a control that is
   added *and* kept in a variable takes two lines, and a tree of any depth becomes a list of
   `Add` calls read bottom up.
4. **Wiring a handler is a separate statement** with `(sender, e) =>` in front of it, even when
   the handler wants neither argument.

## What changed

Two additions, both in `ModernVintageGUI/UI.cs`, both optional: nothing existing was renamed or
moved, and every dialog written before this still compiles unchanged. One rule runs through both:
**values are assigned through properties, and only adding children chains.** A first draft also
had `.WithSize(w, h)`, `.WithName(n)`, `.Aligned(...)` and friends - method calls that set a
property and hand the control back. They were taken out again: a control's state is its
properties, and a second, fluent spelling of every property is a second API to learn, document
and keep in step with the first. What is left is the part properties cannot express, which is
putting a control into a parent.

### `UI` - factories with the dialog defaults baked in

| Call | Replaces |
| --- | --- |
| `UI.Column(a, b, c)` / `UI.Row(...)` / `UI.Overlay(...)` | a `RectangleControl`, its `InsideOrientation`, and one `Children.Add` per child |
| `UI.Panel(padding, ...)` / `UI.Group(...)` | the same, plus padding / a frame and a tint |
| `UI.Scroll(w, h, ...)` | a fixed size container with `EnableVerticalScrollbar` |
| `UI.Split(Orientation.Left, a, b, c)` | a `SplitPanelControl` with one panel per argument |
| `UI.Tabs(("One", a), ("Two", b))` | a `TabsControl` and its `AddTab` calls |
| `UI.Label(text)` / `UI.Heading(text)` / `UI.Title(text)` / `UI.Paragraph(text, width)` | a `TextLabelControl` in the dialog font and colour |
| `UI.Button(text, onClick, icon)` | a `ButtonControl`, its `Text`, its `IconName` and a `Clicked` handler |
| `UI.Checkbox(text, on, onChanged)` / `UI.TextBox(placeholder, onEnter)` / `UI.Dropdown(...)` / `UI.Progress(value, text)` / `UI.Icon(name)` | the control plus the one handler it usually has |
| `UI.Spacer(w, h)` | an empty fixed size rectangle |

### `UIControlExtensions` - adding children, on every control

| Call | Replaces |
| --- | --- |
| `parent.Add(child)` | `parent.Children.Add(child)` - and returns the child, so `var save = column.Add(UI.Button("Save"))` is one line |
| `parent.AddRange(a, b, c)` | three `Children.Add` calls; returns the parent |

Both are generic on what they are given, so `column.Add(UI.Button(...))` is still a
`ButtonControl`. Everything else - a name, a fixed size, an alignment, a margin - is a property
assignment on the control the helper returned:

```csharp
ButtonControl save = UI.Button("Save", Save);
save.Name = "saveButton";
save.Size = new PointD(160, 40);
save.IsAutoSize = false;
```

A control that needs any of that is therefore made first, in a variable, and put into the tree
by name; the tree expression itself stays the list of children it is.

### The same row, after

```csharp
group.Add(UI.Row(
    UI.Checkbox("Enabled", true, on => target.IsEnabled = on),
    UI.Checkbox("Visible", true, on => target.IsVisible = on)));
```

The five sample windows under `Samples/` are written this way throughout, so they double as the
worked examples. `ConfirmSample` is two named buttons, an aligned row and one tree expression;
`DashboardSample` is a full screen of nested split panels and tabs and stays around two hundred
and fifty lines.

## What was found and left alone

These would each make the API cleaner, and each one would break every dialog already written
against it - including the wiki and the README examples. They are recorded here so the decision
is a decision rather than an oversight.

* **The `_Name`, `_Size`, `_Orientation`, `_Margin`, `_Padding`, `_Index` constructor parameters.**
  Underscore-prefixed PascalCase named arguments read like private fields, and `_Orientation`
  actually sets `InsideOrientation`, which is a trap. The clean fix is lower camel case names
  (`name:`, `size:`, `insideOrientation:`) on every constructor. That is a rename across twenty
  controls and every caller, so it is a separate, deliberate change - the helpers above make it
  unnecessary for new code in the meantime.
* **`Orientation` meaning two things.** As `InsideOrientation` it is a stacking direction, as
  `Orientation` it is a cross-axis alignment, and `Fill`, `Center` and `None` only make sense for
  one of the two. Splitting it into `StackDirection` and `Alignment` would be clearer; the enum's
  own documentation already explains the double reading, and changing it would touch every tree.
* **`Size` not implying `IsAutoSize = false`.** Making the setter switch auto sizing off would fix
  the most common mistake, but `ButtonControl` and `TextLabelControl` rely on assigning a size to
  an auto sizing control today. So a fixed size stays two assignments, `Size` and
  `IsAutoSize = false`, written next to each other; the samples do it that way everywhere.
* **Handler signatures.** Every event is `EventHandler<T>`, so `(sender, e)` is always there even
  when only `e` is wanted. That is the .NET convention and worth keeping for consistency with
  `INotifyPropertyChanged`; the `UI` factories take an `Action` where the arguments are almost
  never used (a click, a tick, an Enter).
* **Two namespaces for one library.** The older controls live in `IS2Mod.ControlTypes`, the newer
  ones in `ModernVintageGUI.ControlTypes`, so every file starts with both `using`s. Merging them
  is a rename of the public surface and belongs with the parameter rename above.
