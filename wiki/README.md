# wiki

The content of the [GitHub wiki](https://github.com/DrakenRolle/ModernVintageGUI/wiki), staged here
so it can be reviewed in a diff. The wiki lives in its own repository - this folder is not what
GitHub serves.

`_Sidebar.md` and `README.md` aside, every file is one wiki page, and the file name is the page
name: `ButtonControl.md` is reachable at `/wiki/ButtonControl`, so a link written `[x](ButtonControl)`
resolves once the file is in the wiki repository.

## Publishing

```
git clone https://github.com/DrakenRolle/ModernVintageGUI.wiki.git /tmp/mvsui-wiki
cp wiki/*.md /tmp/mvsui-wiki/
rm /tmp/mvsui-wiki/README.md          # this file is not a wiki page

git -C /tmp/mvsui-wiki add -A
git -C /tmp/mvsui-wiki commit -m "One reference page per control"
git -C /tmp/mvsui-wiki push
```

## Layout

| page | what is in it |
|---|---|
| `Home.md` | the landing page |
| `Layout-and-Scaling.md` | the two layout passes, the three sizes, stacking, GUI scale |
| `Input-Focus-and-Rendering.md` | events, mouse capture, keyboard focus, z-order, depth |
| `Inventories.md` | real inventories, the three ways to own one, change events |
| `Writing-a-Custom-Control.md` | the rules a control of your own has to follow |
| `Controls.md` | the control reference index: every class, what it inherits from, the hierarchy |
| `Supporting-Types.md` | the enums, structs, interfaces and event arguments |
| one file per control | the API reference for that control |

The per-control pages follow the shape of a .NET API reference: namespace and assembly, the class
declaration, the inheritance chain and interfaces, then Constructors, Properties, Methods, Events,
Remarks, Examples and See also.

The pictures are hosted out of `docs/images` in this repository and linked by raw URL, because a
wiki cannot reference files from the code repository any other way. They are regenerated with

```
dotnet run --project ModernVintageGUI/ZLayoutHarness -- --docs docs/images
```
