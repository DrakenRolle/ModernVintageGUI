using Cairo;
using IS2Mod.ControlTypes;
using IS2Mod.Enums;
using ModernVintageGUI.ControlTypes;
using ModernVintageGUI.Enums;
using ModernVintageGUI.Inventory;
using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace ModernVintageGUI.Samples
{
    /// <summary>
    /// One screen showing every control the framework has, built the way a mod would build it.
    ///
    /// It lives in the mod rather than in the harness on purpose: the dialog the test hotkey
    /// opens and the picture in the documentation are then the same tree, so a control that
    /// changes cannot leave the screenshot showing something that no longer exists.
    ///
    /// Everything here takes a plain <see cref="UIControl"/> as its parent, so it works both
    /// under a real CustomDialogElement and under the bare RectangleControl the layout harness
    /// uses in place of one.
    /// </summary>
    public static class ControlShowcase
    {
        /// <summary>Rows in the scrolling list.</summary>
        private const int ListRowCount = 8;

        /// <summary>Slots in the inventory grid, and how many of them are shown at once.</summary>
        private const int GridColumns = 6;
        private const int GridSlotCount = 48;
        private const int GridVisibleRows = 3;

        /// <summary>
        /// Fills <paramref name="parent"/> with the showcase. Pass the title bar in only when
        /// there is a dialog to drag - the harness has none, and a bar with nothing behind it
        /// would just be decoration.
        /// </summary>
        /// <param name="capi">
        /// Used only to report context menu picks into the chat. Null in the harness, where the
        /// menu is still built and laid out but nothing is clicked.
        /// </param>
        public static void Build(
            UIControl parent,
            ICoreClientAPI? capi,
            bool withTitleBar,
            ModInventoryAccess? gridInventory = null)
        {
            // The bar has to reach the edges the way vanilla does, so the root carries no
            // padding and the content below it sits in a padded container instead.
            parent.Padding = 0;
            parent.InsideOrientation = Orientation.Top;

            if (withTitleBar)
            {
                parent.Children.Add(new TitleBarControl("Control showcase") { Name = "titleBar" });
            }

            var content = new RectangleControl(_Name: "content")
            {
                InsideOrientation = Orientation.Left,
                Padding = 10
            };

            content.Children.Add(BuildLeftColumn(capi));
            content.Children.Add(BuildRightColumn(capi, gridInventory));
            content.Children.Add(BuildThirdColumn(capi));
            content.Children.Add(BuildFourthColumn(capi));

            parent.Children.Add(content);
        }

        #region Fourth column - lists and trees
        /// <summary>Sizes of the list controls in the showcase, in author units.</summary>
        private const double ListViewWidth = 210.0;
        private const double ListViewHeight = 260.0;
        private const double TreeViewHeight = 170.0;

        private static UIControl BuildFourthColumn(ICoreClientAPI? capi)
        {
            var column = new RectangleControl(_Name: "fourthColumn")
            {
                InsideOrientation = Orientation.Top,
                Padding = 4
            };

            column.Children.Add(Heading("List view with row details"));
            column.Children.Add(BuildListView(capi));

            column.Children.Add(Heading("Tree"));
            column.Children.Add(BuildTreeView(capi));

            return column;
        }

        /// <summary>
        /// A list whose rows fold their details out under themselves, which is what a list does
        /// unless it is told otherwise. Clicking another row moves the panel there, clicking the
        /// open row again folds it back in.
        /// </summary>
        private static UIControl BuildListView(ICoreClientAPI? capi)
        {
            var list = new ListViewControl(_Name: "listView")
            {
                Size = new PointD(ListViewWidth, ListViewHeight),
                IsAutoSize = false
            };

            list.SetItems(new[]
            {
                new ListViewItem("Granite", value: "granite")
                {
                    Secondary = "hard",
                    Description = "A coarse grained rock. Common in the deeper layers, and the "
                                + "one most millstones are cut from.",
                    Details =
                    {
                        new DetailEntry("Layer", "Deep"),
                        new DetailEntry("Tool", "Pickaxe")
                    }
                },
                new ListViewItem("Andesite", value: "andesite")
                {
                    Secondary = "hard",
                    Description = "A volcanic rock, grey and fine grained.",
                    Details = { new DetailEntry("Layer", "Middle") }
                },
                new ListViewItem("Chalk", value: "chalk")
                {
                    Secondary = "soft",
                    Description = "Soft, pale and easily worked. Burns to quicklime.",
                    Details = { new DetailEntry("Layer", "Upper") }
                },
                new ListViewItem("Basalt", value: "basalt")
                {
                    Secondary = "hard",
                    Description = "Dark and dense, from cooled lava."
                },
                new ListViewItem("Limestone", value: "limestone")
                {
                    Secondary = "soft",
                    Description = "The rock most of the caves in this world were dissolved out of."
                }
            });

            list.SelectionChanged += (sender, e) =>
                capi?.ShowChatMessage("List view: " + (e.Value ?? "none"));

            // One row already folded out, so the picture - and the dialog the test hotkey opens -
            // shows what a click does rather than a plain list of captions.
            list.ShowDetails(list.Items[0]);

            return list;
        }

        /// <summary>
        /// A tree with three branches, one of them open. Values on the leaves, so a pick reports
        /// something a mod would actually store.
        /// </summary>
        private static UIControl BuildTreeView(ICoreClientAPI? capi)
        {
            var tree = new TreeViewControl(_Name: "treeView")
            {
                Size = new PointD(ListViewWidth, TreeViewHeight),
                IsAutoSize = false
            };

            TreeNode rocks = tree.AddNode("Rocks", iconName: GuiIcons.Erode);
            rocks.Add("Granite", "rock-granite");
            rocks.Add("Andesite", "rock-andesite");

            TreeNode soft = rocks.Add("Soft", "rock-soft");
            soft.Add("Chalk", "rock-chalk");
            soft.Add("Limestone", "rock-limestone");

            TreeNode wood = tree.AddNode("Wood", iconName: GuiIcons.Line);
            wood.Add("Oak", "log-oak");
            wood.Add("Birch", "log-birch");
            wood.Add("Pine", "log-pine");

            tree.AddNode("Loose ends", iconName: GuiIcons.Dice);

            // Open one branch and not the others: a tree that opens closed says nothing about
            // what it holds, and one that opens fully unfolded is a list.
            rocks.Expand();

            tree.SelectionChanged += (sender, e) =>
                capi?.ShowChatMessage("Tree: " + (e.Value ?? e.Node?.Text ?? "none"));

            return tree;
        }
        #endregion

        #region Third column - the rest of the controls
        private static UIControl BuildThirdColumn(ICoreClientAPI? capi)
        {
            var column = new RectangleControl(_Name: "thirdColumn")
            {
                InsideOrientation = Orientation.Top,
                Padding = 4
            };

            column.Children.Add(Heading("Checkbox"));

            var checkbox = new CheckboxControl("Show the details", isChecked: true, _Name: "checkbox");
            checkbox.CheckedChanged += (sender, on) => capi?.ShowChatMessage("Checkbox: " + on);
            column.Children.Add(checkbox);

            column.Children.Add(Heading("Text input"));

            var input = new TextInputControl(_Name: "search")
            {
                PlaceholderText = "Search..."
            };

            input.EnterPressed += (sender, text) => capi?.ShowChatMessage("Search: " + text);
            column.Children.Add(input);

            column.Children.Add(Heading("Progress"));

            var progress = new ProgressBarControl(_Name: "progress")
            {
                Value = 0.35,
                Text = "35%"
            };

            column.Children.Add(progress);

            column.Children.Add(Heading("Tabs"));
            column.Children.Add(BuildTabs(capi));

            column.Children.Add(Heading("Colour"));

            var picker = new ColorPickerControl(_Name: "colorPicker")
            {
                Size = new PointD(140, 100),
                IsAutoSize = false
            };

            column.Children.Add(picker);

            column.Children.Add(Heading("Pixel canvas"));

            var canvas = new PixelCanvasControl(columns: 16, rows: 16, unscaledPixelSize: 8, _Name: "canvas")
            {
                DrawMode = true,
                ShowGrid = true,
                DrawColor = picker.SelectedColor,
                HighlightColor = new ElementColor(255, 240, 150, 255)
            };

            PaintHouse(canvas);

            // The picker and the canvas together are the point: pick a colour on the left, hold
            // the right button on the canvas and draw with it.
            picker.ColorChanged += (sender, color) => canvas.DrawColor = color;

            // And the area helpers, live: whatever the cursor is over gets outlined - the roof as
            // one shape, the wall as another, the sky as the rest. GetArea decides what belongs
            // together, the outline follows the same rule, and nothing is drawn between two
            // pixels of the same area.
            //
            // SetHighlight does nothing when the area has not changed, which is what keeps a
            // mouse move from redrawing the dialog several dozen times a second.
            canvas.MouseMove += (sender, e) =>
            {
                if (canvas.TryGetPixelAt(e.X, e.Y, out int x, out int y))
                {
                    canvas.HighlightAreaAt(x, y);
                }
                else
                {
                    canvas.ClearHighlight();
                }
            };

            canvas.Exit += (sender, e) => canvas.ClearHighlight();

            column.Children.Add(canvas);

            return column;
        }

        /// <summary>
        /// A sky, a hill and a house, in sixteen by sixteen.
        ///
        /// Small enough to read as a check as well as a picture: if it comes out mirrored,
        /// shifted or scaled wrong that is obvious at a glance, which a field of random colours
        /// would not be. It is also four areas that are each one colour and hang together, which
        /// is what makes it worth hovering.
        /// </summary>
        public static void PaintHouse(PixelCanvasControl canvas)
        {
            var sky = new ElementColor(78, 132, 190, 255);
            var grass = new ElementColor(84, 140, 60, 255);
            var wall = new ElementColor(196, 120, 66, 255);
            var roof = new ElementColor(120, 52, 44, 255);
            var window = new ElementColor(238, 220, 120, 255);

            canvas.Fill(sky);

            for (int y = 12; y < canvas.Rows; y++)
            {
                for (int x = 0; x < canvas.Columns; x++)
                {
                    canvas.SetPixel(x, y, grass);
                }
            }

            // The house, as a block of pixels: the array overload, indexed the way the picture
            // reads - row first.
            var house = new ElementColor[5, 6];

            for (int row = 0; row < 5; row++)
            {
                for (int column = 0; column < 6; column++)
                {
                    house[row, column] = wall;
                }
            }

            house[2, 2] = window;
            house[2, 3] = window;

            canvas.SetPixels(5, 7, house);

            // A roof, one pixel narrower on each side going up.
            for (int row = 0; row < 3; row++)
            {
                for (int column = row; column < 8 - row; column++)
                {
                    canvas.SetPixel(4 + column, 4 + row, roof);
                }
            }
        }

        /// <summary>
        /// Two tabs with a page each. The pages belong to the tabs control, so switching them is
        /// nobody's job here.
        /// </summary>
        private static UIControl BuildTabs(ICoreClientAPI? capi)
        {
            var tabs = new TabsControl(_Name: "tabs");

            var first = new RectangleControl(_Name: "tabPageOne")
            {
                InsideOrientation = Orientation.Top,
                Padding = 6
            };

            first.Children.Add(new TextLabelControl("The first page.", _Name: "tabOneLabel"));

            var iconButton = new ButtonControl(_Name: "iconButton")
            {
                Text = "With an icon",
                IconName = GuiIcons.Medal
            };

            first.Children.Add(iconButton);

            tabs.AddTab("One", first);
            tabs.AddTab("Icons", BuildIconGallery(capi));

            tabs.SelectionChanged += (sender, e) => capi?.ShowChatMessage("Tab: " + e.Page.Caption);

            return tabs;
        }

        /// <summary>Icon size and row height in the gallery, in author units.</summary>
        private const double IconGallerySize = 24.0;
        private const double IconGalleryHeight = 150.0;

        /// <summary>
        /// Every icon the game knows, with its name.
        ///
        /// It is in the showcase because an icon cannot be judged from a name: the only way to
        /// find out what "medal" or "handheld" looks like at the size a button draws it is to
        /// look at it. The layout harness cannot help here - drawing an icon needs a client - so
        /// without this the answer is only available by trying one name at a time in game.
        /// </summary>
        private static UIControl BuildIconGallery(ICoreClientAPI? capi)
        {
            var list = new RectangleControl(
                borderWidth: 2,
                borderColor: new ElementColor(0.0, 0.0, 0.0, 0.4),
                _Padding: 4,
                _Name: "iconGallery")
            {
                InsideOrientation = Orientation.Top
            };

            list.Size = new PointD(200, IconGalleryHeight);
            list.IsAutoSize = false;
            list.EnableVerticalScrollbar = true;

            // The game's own only. Icons other mods registered are theirs to draw and not always
            // safe to draw out of context - the map's waypoint icons throw when the map has not
            // loaded their SVG - so a gallery is no place to walk through them.
            foreach (string name in GuiIcons.BuiltIn)
            {
                var row = new RectangleControl(_Name: "iconRow_" + name)
                {
                    InsideOrientation = Orientation.Left
                };

                row.Children.Add(new ImageControl(
                    _Name: "icon_" + name,
                    _Size: new PointD(IconGallerySize, IconGallerySize))
                {
                    IconName = name
                });

                row.Children.Add(new TextLabelControl(
                    text: name,
                    fontName: GuiStyle.StandardFontName,
                    fontSize: (int)GuiStyle.SmallFontSize,
                    textColor: new ElementColor(GuiStyle.DialogDefaultTextColor),
                    orientation: TextOrientation.MiddleLeft,
                    padding: 6,
                    _Name: "iconName_" + name));

                list.Children.Add(row);
            }

            return list;
        }
        #endregion

        #region Left column - text, buttons, menus
        private static UIControl BuildLeftColumn(ICoreClientAPI? capi)
        {
            var column = new RectangleControl(_Name: "leftColumn")
            {
                InsideOrientation = Orientation.Top,
                Padding = 4
            };

            column.Children.Add(Heading("Text"));
            column.Children.Add(new TextLabelControl("A label sizes itself from its text.", _Name: "label"));

            column.Children.Add(Heading("Buttons"));

            var save = new ButtonControl(_Name: "saveButton");
            save.Text = "Save";
            column.Children.Add(save);

            var fixedSize = new ButtonControl(_Name: "fixedButton");
            fixedSize.Text = "Fixed 160 x 40";
            fixedSize.Size = new PointD(160, 40);
            fixedSize.IsAutoSize = false;
            column.Children.Add(fixedSize);

            column.Children.Add(Heading("A row of mixed controls"));

            var row = new RectangleControl(_Name: "mixedRow")
            {
                InsideOrientation = Orientation.Left
            };

            var left = new ButtonControl(_Name: "rowLeft");
            left.Text = "One";
            row.Children.Add(left);

            var middle = new TextLabelControl("between", _Name: "rowLabel")
            {
                Orientation = TextOrientation.Center
            };
            row.Children.Add(middle);

            var right = new ButtonControl(_Name: "rowRight");
            right.Text = "Two";
            row.Children.Add(right);

            column.Children.Add(row);

            column.Children.Add(Heading("Context menu"));

            var opener = new ButtonControl(_Name: "menuButton");
            opener.Text = "Open a menu";
            column.Children.Add(opener);

            AttachMenu(opener, capi);

            column.Children.Add(Heading("Dropdown"));
            column.Children.Add(BuildTextDropdown(capi));
            column.Children.Add(BuildItemDropdown(capi));

            return column;
        }

        /// <summary>A plain list of captions, the everyday case.</summary>
        private static UIControl BuildTextDropdown(ICoreClientAPI? capi)
        {
            var dropdown = new DropdownControl(_Name: "textDropdown")
            {
                PlaceholderText = "Pick a rock"
            };

            dropdown.SetItems(new[]
            {
                new DropdownItem("Granite", value: "granite"),
                new DropdownItem("Andesite", value: "andesite"),
                new DropdownItem("Chalk", value: "chalk"),
                new DropdownItem("Basalt", value: "basalt")
            });

            dropdown.Select(0);

            dropdown.SelectionChanged += (sender, e) =>
                capi?.ShowChatMessage("Dropdown: " + e.Item?.Text + " (" + e.Value + ")");

            return dropdown;
        }

        /// <summary>
        /// The same control filled with item stacks: each entry draws its stack as its icon and
        /// carries the game's item tooltip. This is the shape the item type selector will use.
        /// </summary>
        /// <summary>How many item types the showcase puts in its picker.</summary>
        private const int ItemDropdownCount = 40;

        /// <summary>And how many of them are on screen before it scrolls.</summary>
        private const int ItemDropdownVisibleRows = 8;

        /// <summary>
        /// The same control filled with item stacks. Each entry draws its stack as its icon and
        /// carries the game's item tooltip, and the rows take the measurements of the survival
        /// handbook's Blocks and Items list - which the control picks by itself, because a list
        /// built from stacks is an item list.
        ///
        /// This is the shape the item type selector will use; what it will not do is choose the
        /// types itself, those come from the caller.
        /// </summary>
        private static UIControl BuildItemDropdown(ICoreClientAPI? capi)
        {
            var dropdown = new DropdownControl(_Name: "itemDropdown")
            {
                PlaceholderText = "Pick an item type",
                MaxVisibleItems = ItemDropdownVisibleRows
            };

            var items = new List<DropdownItem>();

            if (capi != null)
            {
                foreach (Item item in capi.World.Items)
                {
                    if (items.Count >= ItemDropdownCount)
                        break;

                    // Items without a code are the placeholders the registry keeps for unknown
                    // assets - they have no name and no model to draw.
                    if (item?.Code == null)
                        continue;

                    items.Add(new DropdownItem(new ItemStack(item), value: item.Code.ToString()));
                }
            }

            if (items.Count == 0)
            {
                // No client - the layout harness. A caption, so the picture still shows a box.
                items.Add(new DropdownItem("No items without a client"));
            }

            dropdown.SetItems(items);
            dropdown.Select(0);

            dropdown.SelectionChanged += (sender, e) =>
                capi?.ShowChatMessage("Item dropdown: " + e.Value);

            return dropdown;
        }

        /// <summary>
        /// A menu with two commands and one entry that opens a sub menu, so the cascade and the
        /// bubbling ItemActivated are both exercised.
        /// </summary>
        private static void AttachMenu(ButtonControl opener, ICoreClientAPI? capi)
        {
            var nested = new ContextMenuItem("More", new List<ContextMenuItem>
            {
                new ContextMenuItem("Nested one"),
                new ContextMenuItem("Nested two"),
                new ContextMenuItem("Nested three")
            });

            var menu = new ContextMenuControl(
                opener,
                new List<ContextMenuItem>
                {
                    new ContextMenuItem("First"),
                    new ContextMenuItem("Second"),
                    nested
                },
                "showcaseMenu",
                ContextMenuAnchor.BottomLeft);

            // One subscription sees picks from every level of the cascade.
            menu.ItemActivated += (sender, e) =>
                capi?.ShowChatMessage("Picked: " + string.Join(" > ", e.Path.Select(i => i.Text)));

            // Clicking the button again closes the menu instead of reaching this handler - the
            // UIManager consumes that click for the dismissal, which is what makes it a toggle.
            opener.Clicked += (sender, e) => menu.Toggle();
        }
        #endregion

        #region Right column - scrolling
        private static UIControl BuildRightColumn(ICoreClientAPI? capi, ModInventoryAccess? gridInventory)
        {
            var column = new RectangleControl(_Name: "rightColumn")
            {
                InsideOrientation = Orientation.Top,
                Padding = 4
            };

            column.Children.Add(Heading("Scrolling list"));
            column.Children.Add(BuildScrollingList());

            column.Children.Add(Heading("Inventory grid"));
            column.Children.Add(BuildInventoryGrid(capi, gridInventory));

            column.Children.Add(Heading("Single slot and item type"));
            column.Children.Add(BuildSlotRow(capi));

            column.Children.Add(Heading("Item list"));
            column.Children.Add(BuildItemListView(capi));

            return column;
        }

        /// <summary>How tall the item list in the showcase is, in author units.</summary>
        private const double ItemListViewHeight = 280.0;

        /// <summary>
        /// A list of blocks that is browsed rather than picked from: every row carries the
        /// game's item tooltip, and clicking one folds out the game's own description of it -
        /// the same text the handbook shows - plus a list of every variant of that block, under
        /// that row.
        ///
        /// Blocks rather than items, because blocks are the ones that come in variants: one row
        /// for rock, and inside it the granite, the andesite and the chalk.
        /// </summary>
        private static UIControl BuildItemListView(ICoreClientAPI? capi)
        {
            var list = new ItemListViewControl(_Name: "itemListView")
            {
                Size = new PointD(230, ItemListViewHeight),
                IsAutoSize = false
            };

            if (capi != null)
            {
                // One row per *kind* rather than one per block: the whole point of the nested
                // list is that the variants live inside the row, and a flat list of all of them
                // would have nothing left to open.
                var stacks = new List<ItemStack>();
                var seen = new HashSet<string>();

                foreach (Block block in capi.World.Blocks)
                {
                    if (stacks.Count >= ItemDropdownCount)
                        break;

                    if (block?.Code == null || block.Code.Path.Length == 0)
                        continue;

                    int dash = block.Code.Path.IndexOf('-');
                    string kind = dash < 0 ? block.Code.Path : block.Code.Path.Substring(0, dash);

                    if (!seen.Add(block.Code.Domain + ":" + kind))
                        continue;

                    stacks.Add(new ItemStack(block));
                }

                list.SetStacks(stacks);

                list.VariantSelected += (sender, e) =>
                    capi.ShowChatMessage("Variant: " + (e.Value ?? "none"));
            }
            else
            {
                // No client - the layout harness. Rows without stacks, so the picture still
                // shows the list; the icon column stays empty because drawing a stack needs the
                // game's item atlas.
                list.SetItems(new[]
                {
                    new ListViewItem("Jam") { Description = "A jar of it." },
                    new ListViewItem("Meat Stew") { Description = "Warm, and rather good." },
                    new ListViewItem("Porridge"),
                    new ListViewItem("Scrambled Eggs")
                });
            }

            list.ItemActivated += (sender, e) =>
                capi?.ShowChatMessage("Item list: " + (e.Value ?? e.Item?.Text ?? "none"));

            return list;
        }

        /// <summary>
        /// The two one-square controls next to each other, because they look alike and are not
        /// the same thing at all.
        ///
        /// The left one is a real slot: an inventory of one, of the grid's own, that the player
        /// can put something into and take it back out of next time. The right one holds no
        /// item - it picks an item *type*, and nothing can be dropped into it.
        /// </summary>
        private static UIControl BuildSlotRow(ICoreClientAPI? capi)
        {
            var row = new RectangleControl(_Name: "slotRow")
            {
                InsideOrientation = Orientation.Left
            };

            // Weg B: one bool, and the grid brings its own inventory. The server still has to
            // declare it - see ModernVintageGUIModSystem, where the same name is registered.
            var single = new InventoryGridControl(
                columns: 1,
                _Name: SingleSlotName,
                internalInventory: capi != null,
                slotCount: 1);

            if (capi == null)
            {
                // No client - the layout harness. An inventory needs one, so this is the empty
                // square on its own.
                single.SetSlotCount(1);
            }

            row.Children.Add(single);

            row.Children.Add(BuildItemTypeSelector(capi));

            return row;
        }

        /// <summary>The name the single slot's own inventory is stored under.</summary>
        public const string SingleSlotName = "singleSlot";

        /// <summary>
        /// The type picker. It looks like a slot and behaves like a dropdown: clicking it opens
        /// the list of types the caller put in, in the handbook's own row style.
        /// </summary>
        private static UIControl BuildItemTypeSelector(ICoreClientAPI? capi)
        {
            var selector = new ItemTypeSelectorControl(_Name: "typeSelector")
            {
                MaxVisibleItems = ItemDropdownVisibleRows,
                AllowEmpty = true
            };

            if (capi != null)
            {
                // The types come from outside, always. A picker that went looking for them
                // itself would decide for the mod what may be picked.
                var types = new List<ItemStack>();

                foreach (Item item in capi.World.Items)
                {
                    if (types.Count >= ItemDropdownCount)
                        break;

                    if (item?.Code != null)
                    {
                        types.Add(new ItemStack(item));
                    }
                }

                selector.SetTypes(types);

                selector.SelectionChanged += (sender, e) =>
                    capi.ShowChatMessage("Item type: " + (e.Code?.ToString() ?? "none"));
            }

            return selector;
        }

        private static UIControl BuildScrollingList()
        {
            var list = new RectangleControl(
                borderWidth: 2,
                borderColor: new ElementColor(0.0, 0.0, 0.0, 0.4),
                _Padding: 6,
                _Name: "scrollingList")
            {
                InsideOrientation = Orientation.Top
            };

            // Fixed height is what makes it a window onto the content. Auto sizing would grow to
            // fit every row and there would be nothing left to scroll.
            list.Size = new PointD(230, 120);
            list.IsAutoSize = false;
            list.EnableVerticalScrollbar = true;

            for (int i = 0; i < ListRowCount; i++)
            {
                var row = new ButtonControl(_Name: "listRow" + i);
                row.Text = "Row " + (i + 1);
                list.Children.Add(row);
            }

            return list;
        }

        private static UIControl BuildInventoryGrid(ICoreClientAPI? capi, ModInventoryAccess? gridInventory)
        {
            var grid = new InventoryGridControl(GridColumns, _Name: "inventoryGrid");

            if (capi != null && gridInventory?.Inventory != null)
            {
                // An inventory of the mod's own, kept by the server and saved with the player -
                // not the hotbar, which only made the grid a second view of the bar the player
                // already has, and not a made up one, which would be a picture rather than an
                // inventory.
                //
                // One argument: the access carries the packets a move produces and opens and
                // closes the inventory along with the dialog.
                //
                // It starts empty and stays whatever the player leaves in it. Putting stacks in
                // here from code would be conjuring items out of nothing, which is exactly what
                // an inventory must not do.
                grid.SetInventory(gridInventory);
            }
            else
            {
                // No client - the layout harness. Empty slots, so the picture is still the grid.
                grid.SetSlotCount(GridSlotCount);
            }

            // Exactly the lattice of the visible rows, plus the strip the bar needs. Written out
            // rather than guessed: n slots and n-1 gaps, because the lattice has no gap at the
            // edges.
            double latticeWidth = GridColumns * ItemSlotControl.UnscaledSlotSize
                                + (GridColumns - 1) * ItemSlotControl.UnscaledSlotPadding;

            double latticeHeight = GridVisibleRows * ItemSlotControl.UnscaledSlotSize
                                 + (GridVisibleRows - 1) * ItemSlotControl.UnscaledSlotPadding;

            // Plus the inset the grid keeps on each side for the selection ring, and the strip
            // the scrollbar needs.
            double inset = InventoryGridControl.UnscaledInset * 2;

            grid.Size = new PointD(
                latticeWidth + inset + ScrollbarStyle.UnscaledWidth,
                latticeHeight + inset);
            grid.IsAutoSize = false;
            grid.EnableVerticalScrollbar = true;

            return grid;
        }
        #endregion

        private static TextLabelControl Heading(string text)
        {
            return new TextLabelControl(
                text: text,
                fontSize: (int)GuiStyle.SmallFontSize,
                textColor: new ElementColor(GuiStyle.DialogDefaultTextColor),
                _Name: "heading_" + text)
            {
                Margin = 4
            };
        }
    }
}
