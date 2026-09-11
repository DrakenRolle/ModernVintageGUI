# UI designer

A browser based visual designer for ModernVintageGUI dialogs. Blazor Server on the outside, the
framework's own layout and drawing code on the inside: the picture in the middle of the screen is
not a preview drawn to look like the game, it is `PerformLayout()` and `GenerateRenderData()`
rendered to a PNG, the same two calls the game makes and the same ones `ZLayoutHarness` makes.

```bash
dotnet run --project ModernVintageGUI/ZUIDesigner
```

Then open <http://localhost:5199>. `VINTAGE_STORY` has to point at the game directory, exactly as
for the harness - Cairo, the fonts and the vanilla GUI styling all come from there.

---

## Markup first

The document is XML, and the XML is the source of truth. Dragging a control in, moving one,
typing in the property grid and typing in the markup pane are all the same operation: they edit
the document, and the control tree is rebuilt from it afterwards. There is no second model that
could drift out of step with the text, which is why the markup pane can be edited by hand at any
time and why saving is just writing the text out.

```xml
<Dialog Name="root" InsideOrientation="Top" Padding="0" BackgroundColor="#33291fff">
  <TitleBar Name="titleBar" Title="My Dialog" />
  <Rectangle Name="content" InsideOrientation="Top" Padding="10">
    <Label Name="heading" Text="What this dialog is for" FontSize="18" TextColor="#e8e2d6ff" />
    <TextInput Name="search" PlaceholderText="Search" />
    <Rectangle Name="buttonRow" InsideOrientation="Left" Padding="0">
      <Button Name="save" Text="Save" />
      <Button Name="cancel" Text="Cancel" />
    </Rectangle>
  </Rectangle>
</Dialog>
```

### Elements

The tag is the class name without its `Control` suffix: `ButtonControl` is `<Button>`,
`RectangleControl` is `<Rectangle>`. The two exceptions are there because the class names are
longer than the ideas:

| Tag | Class | Notes |
| --- | --- | --- |
| `<Dialog>` | `RectangleControl` | The root. A plain container under a name that says what it is. |
| `<Label>` | `TextLabelControl` | |
| `<Tab>` | - | A page of a `<Tabs>`, not a control. Holds one control and a `Caption`. |

Nothing else is written down anywhere: the toolbox, the property grid and the parser all read the
same catalog, and that catalog is built by reflecting over the control assembly. A control added
to ModernVintageGUI turns up in the designer with its properties on the next build, without this
project being touched.

### Attributes

An attribute is a property, matched by name and case insensitively. The types that can be
written:

| Type | Written as | Example |
| --- | --- | --- |
| `string` | as is | `Text="Save"` |
| `bool` | `true` / `false` | `IsChecked="true"` |
| `int`, `double` | a number, invariant culture | `Padding="10"` |
| enum | the member name | `InsideOrientation="Left"` |
| `PointD` | `width,height` | `Size="200,120"` |
| `ElementColor` | `#rgb`, `#rrggbb` or `#rrggbbaa` | `BackgroundColor="#33291fff"` |

Everything is in unscaled author units, the same as when you write the tree in C#. The GUI scale
slider in the toolbar re-lays the document out at another scale, which is the fastest way to find
a dimension that was hard coded in device pixels by accident.

**`Size` implies `IsAutoSize`.** Writing `Size="200,120"` sets `IsAutoSize="false"` too, and
`Size="0,0"` sets it to true - the same rule the `UIControl` constructor applies to its `_Size`
parameter. Writing `IsAutoSize` yourself always wins. Without this a document that says `Size` and
nothing else would be measured from its content and the size would look ignored, because the
`Size` setter alone does not touch auto sizing.

Which is why **a container dropped from the toolbox has no `Size`**: it grows with whatever is put
in it, so the second control added to it is not squeezed into the leftovers of the first and the
third does not come out with no height at all. A container that is given a fixed size and then
filled past it is doing what the layout says - it clips at its own edge - and since what got cut is
simply not drawn, the designer says so in the diagnostics strip instead of leaving you to wonder:

> `<Rectangle content> needs 118x212 for its children but is 125x80 and cannot grow, so the ones
> that no longer fit are cut off. Clear Size to let it size itself from what is put in it.`

### Containers

Only `<Rectangle>` (and `<Dialog>`, which is one) takes authored children. Everything else either
has no children or builds and owns them itself - a button's label, a list's rows, a dropdown's
popup - and a control dropped in there would sit somewhere its owner is about to overwrite. The
designer says so instead of letting it happen: a full or closed container is never offered as a
drop target, and a document that puts children somewhere they cannot go gets a warning in the
diagnostics strip rather than a silently different dialog.

`<Tabs>` is the exception that is worth the special case:

```xml
<Tabs Name="tabs">
  <Tab Caption="Input">
    <Rectangle Name="inputPage" InsideOrientation="Top" Padding="8" Size="260,120">
      <TextInput Name="field" PlaceholderText="Type here" />
    </Rectangle>
  </Tab>
  <Tab Caption="Output" />
</Tabs>
```

A tab holds exactly one control, so wrap several in a `<Rectangle>`. Only the selected page is on
the tree - that is how `TabsControl` hides the others - so click a `<Tab>` in the outline to turn
to that page and design it.

### The title bar

`<TitleBar>` has one place it belongs and the designer puts it there: **first inside the root**,
wherever you happen to drop it. It is the top edge of the window rather than a control in a stack -
it measures to a minimum width and lets its parent stretch it across the dialog, so anywhere else
it is a bar across the middle of something.

It is also laid out inside its parent's padding like every other control, and a bar that stops
short of the frame is not a title bar. So dropping one sets the root's `Padding` to `0` and hands
that padding to the containers under the bar, which is how the framework's own title bar dialogs
are built. A hand edit can still put it somewhere else; the diagnostics strip says so.

---

## Drag and drop

Drag a control out of the toolbox, or an existing one off the canvas or out of the outline, and
drop it **on the canvas or on the outline** - both take drops, and both give the same two answers.

### On the canvas

* the **container** that would take the control is outlined and named,
* a **caret** shows where in that container it would land - a horizontal line between rows when
  the container stacks downwards, a vertical line between columns when it stacks sideways,
* every **empty container** gets a dashed zone drawn over it, because an empty one measures to
  twice its padding and would otherwise be too small to aim at,
* the ghost following the cursor turns red where nothing can be dropped.

The deepest container under the cursor wins, so dropping onto a button inside a row puts the
control into that row beside the button rather than into the row's parent. The index comes from
comparing the cursor against each child's midpoint along the stacking axis, which is the same rule
the layout used to put them there, so what the caret shows is where the control ends up.

A container that cannot take the drop is skipped and the search carries on outwards: a full tab
page, a control that owns its own children, or - when moving - the dragged control's own subtree,
which would otherwise detach the whole branch.

### On the outline

The outline is the document written out, so dropping onto it is dropping into the tree. Each row
splits into three bands:

```
---- top quarter ......... before this row, beside it in its parent
     middle .............. inside this row, when it is a container with room
---- bottom quarter ...... after this row, beside it in its parent
```

The row lights up for *inside* and grows a line at its edge for *before* and *after*. A row that
is not a container, or one that is full, has no middle - it splits in half - so a list of leaves
can still be reordered by dragging over it. This is the way to reach a container that is hard to
hit on the canvas, and the way to put the first control into an empty one.

### Where the work happens

None of that asks the server. Each render sends the browser one box per control, and the hover
maths runs against that list locally; a round trip per mouse move would put the network between
the cursor and the highlight. The server hears once, on drop, edits the markup, and the tree is
laid out again from it - which is why a container resizes around what you just put in it.

### Keyboard

| Key | |
| --- | --- |
| `Delete` | remove the selected control |
| `Ctrl+D` | duplicate it, with fresh names through the copied subtree |
| `Ctrl+Z` / `Ctrl+Y` | undo, redo |
| `Escape` | abandon the drag in progress |

---

## Leaving the designer

**Save** writes the `.mvgui` file. **Open** reads one back, and a document that is not well formed
lands in the markup pane with the parser's complaint rather than being thrown away.

**C#**, in the pane at the bottom, is the code that builds the same tree - construct, set, add to
the parent, the shape the scenarios and the showcase are written in. **Copy C#** puts it on the
clipboard. The framework is used from code today, so this is how a design gets into a mod.

---

## What the picture cannot show

The same limits the layout harness has, and for the same reason - there is no game running:

* an item slot or an inventory grid draws its frame but no stack; the atlas is the client's,
* a checked `<Checkbox>` fills flat black instead of with vanilla's water texture,
* an `<Image>` needs an asset the client would resolve,
* hover, focus and click states are drawn from whatever the document says, not from a cursor.

Everything about layout, sizing, text measurement, scaling and the vanilla styling is real.

---

## Layout of the project

| | |
| --- | --- |
| `Markup/` | The format. Catalog, parser, document with undo, C# generator. |
| `Rendering/` | Layout and draw to a PNG, plus the hit map the browser drags against. |
| `Components/` | The Blazor page, the outline and the property grid. |
| `wwwroot/designer.js` | Pointer handling, drop resolution, the overlay. |
| `Templates/` | The starting points in the toolbar dropdown. One file each. |

`Markup/` has no dependency on ASP.NET or on the designer - it is the framework plus
`System.Xml.Linq`. If a mod should ever load `.mvgui` files at runtime rather than only generated
code, linking those files into `ModernVintageGUI.csproj` is the whole change:

```xml
<Compile Include="..\ZUIDesigner\Markup\*.cs" LinkBase="Markup" />
```

`MarkupBuilder.Build(XDocument.Load(path))` then hands back the tree.
