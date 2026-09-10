# In-game screenshots

Starts the game, opens the world, opens the UI, takes pictures, closes the game. No hands.

```powershell
ModernVintageGUI\ZIngameShots\ingame-screenshot.ps1
```

That builds the mod, starts Vintage Story with `--openWorld` on the one world in the Saves
folder, runs [steps/showcase.txt](steps/showcase.txt) inside it and puts the pictures into `out\`
next to this file. The run ends with the world being left the way the escape menu does it, which
saves it, and the game closing itself.

The [layout harness](../ZLayoutHarness) renders the same controls without the game. What it cannot
show is anything that only exists at runtime - the world behind the dialog, item stacks in slots,
the vanilla HUD around it, and whether a popup really lands where it should on a real window. That
is what this is for.

## The pictures are the whole window

A dropdown list, a context menu and a tooltip are dialogs of their own in the overlay band, not
children of the dialog they belong to. A picture of the dialog's own surface would not have them
in it. So the capture reads the default framebuffer at the end of the frame, exactly the way the
game's F12 does: world, HUD, dialog, popups, everything - except the player's own body: the
immersive first person mode is switched off for the run and put back afterwards, or an arm
would swing into the frame after every teleport.

## Two halves

* [ingame-screenshot.ps1](ingame-screenshot.ps1) - builds, starts, waits, reports. `-Steps`,
  `-Out`, `-World`, `-Configuration`, `-TimeoutSec`, `-NoBuild`, `-KeepOpen`, `-Skip`; `Get-Help`
  on the script has the details.
* [AutoScreenshot.cs](../ModernVintageGUI/Automation/AutoScreenshot.cs) in the mod - reads the
  step script named by `MVGUI_AUTOSHOT`, waits for the world to be there, runs the steps at the
  end of each rendered frame and writes `autoshot.done` into `MVGUI_AUTOSHOT_OUT` when it is
  through. Without the variable it does nothing at all.

A script can also be run in a game that is already open, to try it without a restart:

```
.mvsui autoshot C:\path\to\steps.txt [C:\output\dir]
```

## Steps

One per line, `#` starts a comment.

| Step | Does |
|---|---|
| `wait 90` | waits 90 rendered frames; `wait 2s` and `wait 500ms` wait by the clock |
| `open` | opens the showcase dialog - the same one the J hotkey opens |
| `close` | hides it |
| `shot name.png` | saves a picture of the whole window into the output directory |
| `shot name.png crop` | the same, cut down to the dialogs on screen - popups included; `crop 24` leaves 24 pixels around them |
| `dropdown NAME` | opens the dropdown with that `Name`; `dropdown NAME close` closes it |
| `menu OWNER` | shows the context menu attached to the control with that `Name`; `menu OWNER close` hides it |
| `hover NAME` | moves the cursor onto the middle of a control |
| `click NAME` | presses and releases the left button there |
| `drag NAME DX DY` | presses there, moves the cursor by DX, DY in eight moves and releases - a split panel bar, or anything else that captures the mouse; a fourth number is the move count |
| `key KEY [NAME]` | presses a key (a `GlKeys` name) - after focusing the named control, or in the topmost dialog of ours: `key Escape` |
| `type TEXT` | types the characters into the topmost dialog, for a focused text field |
| `wheel NAME [TICKS]` | moves the cursor onto a control and turns the wheel there - negative ticks scroll down, one down when no count is given |
| `tab NAME 1` | switches the tabs control with that `Name` to its second page |
| `hotkey CODE` | runs the handler of a registered hotkey the way the key would - `hotkey mvgui_samples` is K |
| `hide NAME` | hides the dialog with that name, or the dialog the named control is in |
| `chat off` | hides the vanilla chat window, which sits where a popup hangs out of a centred dialog; `chat on` brings it back, and a run that leaves the game open does that itself |
| `cmd LINE` | sends a chat line as the player would type it: `/...` goes to the server, `.…` runs a client command |
| `section NAME` | not a step: names the steps after it, up to the next `section` line, so that `-Skip NAME` can leave them out; a name may recur |
| `onquit STEP` | runs the step only when the script ends with a `quit`: the teardown of a scene, which a run that keeps the game open for a look at it skips |
| `quit` | leaves the world and closes the game |

`cmd` is what lets a script change the world before it takes a picture - a mod that has commands
for placing its blocks can be photographed from here without a second pipeline. The Copper Casing
mod's [`docs/screenshots/coppercasing.txt`](../CopperCasing/docs/screenshots/coppercasing.txt)
builds a scene with its `/casing` command, walks the camera around it with `.casing look`, and
takes it down again - with `onquit` in front of the teardown, so a run with `-KeepOpen` leaves the
scene standing for a look around.

`NAME` is the `Name` a control was built with - `textDropdown`, `menuButton`, `saveButton` and so
on in [ControlShowcase.cs](../ModernVintageGUI/Samples/ControlShowcase.cs). A menu entry can be
named by its text. Every dialog of ours that is showing is searched, the showcase first - so the
sample windows opened with `cmd .mvsui sample explorer` or `hotkey mvgui_samples` can be driven
too. `NAME:2` is the second control of that name in tree order (nested split panels name their
bars alike, `_splitter0`), `@TabsControl` the first control of that type.
[steps/samples.txt](steps/samples.txt) drives all five sample windows that way.

A step that changes the UI runs after the frame that asked for it, and the next step waits two
more frames, so what it changed is on screen before a `shot` that follows.

## What comes out

```
out\
  showcase.png             the pictures the script asked for
  showcase-dropdown.png
  ...
  autoshot.done            "ok", or "error: line 7 'dropdown foo': no control named 'foo'"
  autoshot.log             every step with a time, and what went wrong if something did
  autoshot.steps           the script as it was run (with the appended quit)
```

The same lines are in the game's `client-main.log`, prefixed `[ModernVintageGUI autoshot]`.

## From Visual Studio

The CopperCasing project has three launch profiles that run this pipeline on its scene, for
`Ctrl+F5` from the profile dropdown:

* **Test world (keep open)** builds the scene, takes the pictures and leaves the game standing
  in it, the player in front of the whole scene, for walking around in it.
* **Test world (quit)** is the same run, after which the scene is taken down, the world saved and
  the game closed.
* **Test world (walk around)** builds the scene and leaves the game open on the lookout without
  taking a single picture: `-Skip pictures` leaves the `section pictures` parts of the script out.

All three hand the pipeline `-NoBuild`, because Visual Studio has just built both mods, put the
pictures into `CopperCasing/docs/images/ingame`, and keep their console open with the list of them.
