# TextInputControl Class

**Namespace:** `ModernVintageGUI.ControlTypes`
**Assembly:** `ModernVintageGUI.dll`

A single line text field: a search box, a name, a number.

```csharp
public class TextInputControl : UIControl
```

**Inheritance:** `Object` → [UIControl](UIControl) → **TextInputControl**

**Implements:** `INotifyPropertyChanged` (inherited)

## Remarks

Characters arrive through [`UIControl.KeyPress`](UIControl#events), which exists only because of
`ClientMainKeyPressPatch` - the game does not offer typed characters to anything that is not one of
its own dialogs. `KeyDown` is not a substitute: it carries a raw key code, so it cannot tell an "a"
from an "A" and cannot produce an umlaut at all. See
[Input, Focus and Rendering](Input-Focus-and-Rendering#typed-characters) for the patch.

The field asks for every key while it is focused (`WantsAllKeyboardInput`), so typing an E does not
open the inventory in the middle of a word. Escape still leaves, because a dialog you cannot escape
from is a trap.

Not there yet: selecting a range, cut and paste, and a blinking caret. The caret is solid on
purpose - blinking means redrawing the dialog surface twice a second for a two pixel bar.

## Constructors

| | Description |
|---|---|
| `TextInputControl(string _Name = "", PointD? _Size = null, double _Margin = 5)` | Defaults to 200 x 30 author units with auto-sizing off. |

## Fields

| Name | Type | Value | Description |
|---|---|---|---|
| `UnscaledDefaultHeight` | `const double` | `30.0` | The field height in author units. |

## Properties

| Name | Type | Description |
|---|---|---|
| `Text` | `string` | What is in the field. Setting it puts the caret at the end and raises `TextChanged`. |
| `PlaceholderText` | `string` | Shown greyed while the field is empty. |
| `MaxLength` | `int` | How many characters fit. `0` - the default - means no limit. |
| `IsPassword` | `bool` | Draw dots instead of the characters. |
| `CharacterFilter` | `Func<char, bool>?` | Called for every character before it is taken. Return `false` to refuse it - a number field lets digits through and nothing else. |
| `WantsAllKeyboardInput` | `bool` | `override`. `true` while the field holds the keyboard focus. |

## Methods

| Name | Returns | Description |
|---|---|---|
| `CalculateSize()` | `PointD` | `override`. |
| `NormalizeChildrenByDelta()` | `void` | `override`. |
| `CalculateAllPositions()` | `void` | `override`. |
| `HitTestRecursive(UIControl, double, double)` | `UIControl?` | `protected override`. One hit target - the label must not take the click meant for the field. |
| `GenerateRenderData(ImageSurface surface, Context ctx)` | `void` | `override`. The sunken box, the text or the placeholder, and the caret. |

## Events

| Name | Type | Description |
|---|---|---|
| `TextChanged` | `EventHandler<string>` | Raised whenever the text changes, by typing or from code. |
| `EnterPressed` | `EventHandler<string>` | Raised when Enter is pressed in the field. The text is the argument. |

## Examples

```csharp
var search = new TextInputControl(_Name: "search");
search.PlaceholderText = "Search items...";
search.TextChanged  += (sender, text) => Filter(text);
search.EnterPressed += (sender, text) => Submit(text);
```

A number field:

```csharp
var amount = new TextInputControl { MaxLength = 4, CharacterFilter = char.IsDigit };
```

## See also

* [UIControl](UIControl) - the base class
* [Input, Focus and Rendering](Input-Focus-and-Rendering#typed-characters) - why this needs a Harmony patch
