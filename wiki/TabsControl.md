# TabsControl Class

**Namespace:** `ModernVintageGUI.ControlTypes`
**Assembly:** `ModernVintageGUI.dll`

A row of tabs with one page showing at a time.

```csharp
public class TabsControl : UIControl
```

**Inheritance:** `Object` → [UIControl](UIControl) → **TabsControl**

**Implements:** `INotifyPropertyChanged` (inherited)

## Remarks

The pages belong to the control, which is the part that makes it worth having: adding a tab and
adding what is on it is one call, and showing and hiding them is then nobody's job. A caller who
wants the pages arranged somewhere else can still have that - pass `null` content and listen on
`SelectionChanged`.

A page that is not showing is taken **off the tree** rather than hidden by a flag, so it costs no
layout and cannot be tabbed into with the keyboard.

The tab captions are not [ButtonControl](ButtonControl)s. A tab is a flat strip that is either lit
or not, and a button would bring its embossed frame and look nothing like one - the same reason a
[ContextMenuItem](ContextMenuItem) is not a button either. The measurements come from
`GuiElementHorizontalTabs`: 30 high, 12 of padding left and right, 2 between two tabs.

## Constructors

| | Description |
|---|---|
| `TabsControl(string _Name = "", double _Margin = 5)` | Builds the tab strip and the page host. |

## Properties

| Name | Type | Description |
|---|---|---|
| `Tabs` | `IReadOnlyList<TabPage>` | The tabs, in order. |
| `SelectedIndex` | `int` | Which tab is showing, or `-1` when there are none. Setting it selects, raising `SelectionChanged`. |
| `SelectedTab` | `TabPage?` | The showing tab, or `null`. Read-only. |

## Methods

| Name | Returns | Description |
|---|---|---|
| `AddTab(string caption, UIControl? content = null)` | `TabPage` | Adds a tab and returns it. The first tab added is selected without raising `SelectionChanged`. |
| `Select(int index)` | `void` | What a click does, from code. |

## Events

| Name | Type | Description |
|---|---|---|
| `SelectionChanged` | `EventHandler<TabSelectedEventArgs>` | Raised when the showing tab changes, by click, by keyboard or from code. |

## TabPage Class

**Namespace:** `ModernVintageGUI.ControlTypes`

One tab: its caption, and what is shown while it is picked. Created by `AddTab`, never directly.

```csharp
public class TabPage
```

**Inheritance:** `Object` → **TabPage**

| Member | Type | Description |
|---|---|---|
| `Caption` | `string` | The tab's caption. Read-only. |
| `Content` | `UIControl?` | What the tab shows. `null` for a tab the caller arranges itself. |

## TabSelectedEventArgs Class

```csharp
public class TabSelectedEventArgs : EventArgs
```

| Member | Type | Description |
|---|---|---|
| `Page` | `TabPage` | The tab that is now showing. |
| `Index` | `int` | Its position. |

## Examples

```csharp
var tabs = new TabsControl();
tabs.AddTab("Input",  inputPanel);
tabs.AddTab("Output", outputPanel);

tabs.SelectionChanged += (sender, e) => capi.ShowChatMessage("Now on " + e.Page.Caption);
```

A tab whose content the caller arranges itself:

```csharp
tabs.AddTab("Icons");                       // no content
tabs.SelectionChanged += (s, e) => ShowMyOwnPage(e.Index);
```

## See also

* [UIControl](UIControl) - the base class
* [ContextMenuControl](ContextMenuControl) - the other flat-row control
