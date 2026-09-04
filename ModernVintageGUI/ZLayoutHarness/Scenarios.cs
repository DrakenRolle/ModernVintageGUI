using Cairo;
using IS2Mod.ControlTypes;
using IS2Mod.Enums;
using ModernVintageGUI.ControlTypes;
using Vintagestory.API.Client;
using System;
using System.Collections.Generic;

namespace LayoutHarness
{
    internal sealed class Scenario
    {
        public required string Name { get; init; }
        public required string Description { get; init; }
        public required Func<RectangleControl> Build { get; init; }
    }

    internal static class Scenarios
    {
        public static IEnumerable<Scenario> All()
        {
            yield return new Scenario
            {
                Name = "test-dialog",
                Description = "The tree the mod opens on the test hotkey: two stacked buttons " +
                              "plus a horizontally stacked row that mixes buttons and a label.",
                Build = BuildTestDialog
            };

            yield return new Scenario
            {
                Name = "fixed-size-container",
                Description = "A container with IsAutoSize = false that has children. Measuring " +
                              "used to fold its own size plus the children plus the padding back " +
                              "in, so it grew on every layout pass.",
                Build = BuildFixedSizeContainer
            };

            yield return new Scenario
            {
                Name = "title-bar",
                Description = "A dialog with a vanilla style title bar: full bleed bar on top, "
                            + "padded content below it.",
                Build = BuildTitleBar
            };

            yield return new Scenario
            {
                Name = "context-menu-items",
                Description = "The item stack a ContextMenuControl puts into its popup: entries " +
                              "stacked vertically, each entry a button that gets normalized to " +
                              "the width of the widest one.",
                Build = BuildContextMenuItems
            };

            yield return new Scenario
            {
                Name = "context-menu-hover",
                Description = "The same stack with the cursor on the first entry - the highlight is "
                            + "produced by the real Enter handler, not by setting colours by hand.",
                Build = BuildContextMenuHover
            };

            yield return new Scenario
            {
                Name = "dropdown-closed",
                Description = "Three dropdowns: one with a selection, one showing its placeholder, "
                            + "and one given a fixed size. The box and its arrow button are the "
                            + "ones GuiElementDropDown draws.",
                Build = BuildDropdownClosed
            };

            yield return new Scenario
            {
                Name = "dropdown-open",
                Description = "The list a dropdown puts into its popup, with the second entry "
                            + "selected and the cursor on the third - selection and hover are "
                            + "separate states and have to be distinguishable.",
                Build = BuildDropdownOpen
            };

            yield return new Scenario
            {
                Name = "dropdown-item-rows",
                Description = "The same list in the item row style: the measurements of the "
                            + "survival handbook's Blocks and Items list. The icon column is "
                            + "empty here because rendering a stack needs the game's item atlas.",
                Build = BuildDropdownItemRows
            };

            yield return new Scenario
            {
                Name = "keyboard-focus",
                Description = "Three buttons, the middle one holding the keyboard focus and the "
                            + "last one both focused and hovered - the ring and the hover look "
                            + "are independent states and have to survive each other.",
                Build = BuildKeyboardFocus
            };

            yield return new Scenario
            {
                Name = "clipping",
                Description = "Two clipping containers of the same fixed height. The left one "
                            + "holds more rows than fit and is cut, the right one fits and looks "
                            + "untouched.",
                Build = BuildClipping
            };

            yield return new Scenario
            {
                Name = "scroll-vertical",
                Description = "A fixed height container with more rows than fit and a vertical "
                            + "scrollbar, scrolled a third of the way down. Track and handle are "
                            + "drawn with the values from GuiElementScrollbar.",
                Build = () => BuildScrolling(vertical: true, horizontal: false)
            };

            yield return new Scenario
            {
                Name = "scroll-both",
                Description = "The same container with content too wide as well, so both bars "
                            + "show and each viewport axis loses the other bar's strip.",
                Build = () => BuildScrolling(vertical: true, horizontal: true)
            };

            yield return new Scenario
            {
                Name = "showcase",
                Description = "The dialog the test hotkey opens: every control the framework "
                            + "has, built by the same code that builds it in game.",
                Build = BuildShowcase
            };

            yield return new Scenario
            {
                Name = "inventory-grid",
                Description = "A 6 by 3 inventory grid on the vanilla lattice: 48 unit slots "
                            + "with a 3 unit gap, no gap at the edges.",
                Build = BuildInventoryGrid
            };

            yield return new Scenario
            {
                Name = "inventory-grid-scrolling",
                Description = "The same grid with 8 rows in a window only 3 rows tall, scrolled "
                            + "one row down - the case the whole clipping and scrolling work is "
                            + "for.",
                Build = BuildScrollingInventoryGrid
            };

            yield return new Scenario
            {
                Name = "stretched-label",
                Description = "A label inside a vertically stacked panel. Normalization stretches " +
                              "it to the full content width; the next measure pass must still " +
                              "start from the text, not from the width it was stretched to.",
                Build = BuildStretchedLabel
            };
        }

        /// <summary>
        /// Stands in for CustomDialogElement, which cannot be constructed without a running
        /// client. Same role in the tree: auto-sizing root, vertical stacking, padding 10.
        /// </summary>
        private static RectangleControl CreateRoot()
        {
            var root = new RectangleControl(
                backgroundColor: new ElementColor(0.20, 0.16, 0.13, 1.0),
                _Name: "root");

            root.InsideOrientation = Orientation.Top;
            root.Padding = 10;

            return root;
        }

        private static RectangleControl BuildTestDialog()
        {
            RectangleControl root = CreateRoot();

            var button = new ButtonControl(_Name: "save1");
            button.Text = "Save";
            root.Children.Add(button);

            var button2 = new ButtonControl(_Name: "save2");
            button2.Text = "Save";
            button2.Size = new PointD(150, 150);
            button2.IsAutoSize = false;
            root.Children.Add(button2);

            var row = new RectangleControl();
            row.Name = "row";
            row.InsideOrientation = Orientation.Left;

            var rowButton1 = new ButtonControl(_Name: "row1");
            rowButton1.Text = "Test";
            row.Children.Add(rowButton1);

            var rowButton2 = new ButtonControl(_Name: "row2");
            rowButton2.Text = "Test";
            row.Children.Add(rowButton2);

            // The control that collapsed to 0 x 0 from the second layout pass onwards.
            var label = new TextLabelControl("Test", _Name: "label");
            label.Orientation = TextOrientation.Center;
            row.Children.Add(label);

            var rowButton3 = new ButtonControl(_Name: "row3");
            rowButton3.Text = "Test";
            row.Children.Add(rowButton3);

            var rowButton4 = new ButtonControl(_Name: "row4");
            rowButton4.Text = "Test";
            row.Children.Add(rowButton4);

            root.Children.Add(row);

            return root;
        }

        private static RectangleControl BuildFixedSizeContainer()
        {
            RectangleControl root = CreateRoot();

            var fixedBox = new RectangleControl(
                borderWidth: 2,
                borderColor: new ElementColor(1.0, 1.0, 1.0, 0.5),
                _Padding: 8,
                _Name: "fixedBox");

            fixedBox.InsideOrientation = Orientation.Top;
            fixedBox.Size = new PointD(240, 120);
            fixedBox.IsAutoSize = false;

            var inner = new TextLabelControl("Fixed 240x120", _Name: "innerLabel");
            inner.Orientation = TextOrientation.MiddleCenter;
            fixedBox.Children.Add(inner);

            root.Children.Add(fixedBox);

            return root;
        }

        /// <summary>
        /// Mirrors what ContextMenuControl builds inside its popup. The popup itself cannot be
        /// constructed headless (it needs the client API), but the part that has to lay out
        /// correctly is this stack.
        /// </summary>
        private static RectangleControl BuildTitleBar()
        {
            var root = new RectangleControl(
                backgroundColor: new ElementColor(0.20, 0.16, 0.13, 1.0),
                _Name: "root");

            root.InsideOrientation = Orientation.Top;
            root.Padding = 0;

            root.Children.Add(new TitleBarControl("My Title") { Name = "titleBar" });

            var content = new RectangleControl(_Name: "content");
            content.InsideOrientation = Orientation.Top;
            content.Padding = 10;

            var save = new ButtonControl(_Name: "save");
            save.Text = "Save";
            content.Children.Add(save);

            root.Children.Add(content);
            return root;
        }

        private static RectangleControl BuildContextMenuItems()
        {
            RectangleControl root = CreateRoot();

            // The very box ContextMenuControl puts into its popup.
            RectangleControl stack = ContextMenuControl.CreateMenuBackground("itemStack");

            foreach (string caption in new[] { "Fixed", "Moveable", "Reset position" })
            {
                stack.Children.Add(new ContextMenuItem(caption) { Name = caption });
            }

            root.Children.Add(stack);
            return root;
        }

        /// <summary>
        /// The closed boxes. A dropdown without a dialog cannot open its popup - that needs a
        /// client API - but the box draws itself, which is the half this picture is about.
        /// </summary>
        private static RectangleControl BuildDropdownClosed()
        {
            RectangleControl root = CreateRoot();

            var withSelection = new DropdownControl(_Name: "picked");
            withSelection.SetItems(new[]
            {
                new DropdownItem("Granite"),
                new DropdownItem("Andesite"),
                new DropdownItem("Chalk")
            });
            withSelection.Select(1);
            root.Children.Add(withSelection);

            var empty = new DropdownControl(_Name: "placeholder")
            {
                PlaceholderText = "Pick a rock"
            };
            empty.SetItems(new[] { new DropdownItem("Granite") });
            empty.Select(-1);
            root.Children.Add(empty);

            var fixedSize = new DropdownControl(_Name: "fixed")
            {
                Size = new PointD(220, 30),
                IsAutoSize = false
            };
            fixedSize.SetItems(new[] { new DropdownItem("Fixed 220 x 30") });
            fixedSize.Select(0);
            root.Children.Add(fixedSize);

            return root;
        }

        /// <summary>
        /// The open list, built the way DropdownControl builds it - the same background helper
        /// and the same entries, so the picture cannot drift from what the popup shows.
        /// </summary>
        private static RectangleControl BuildDropdownOpen()
        {
            RectangleControl root = CreateRoot();

            var entries = new[]
            {
                new DropdownItem("Granite"),
                new DropdownItem("Andesite"),
                new DropdownItem("Chalk"),
                new DropdownItem("Basalt")
            };

            foreach (DropdownItem entry in entries)
            {
                entry.Name = entry.Text;
            }

            // The very box DropdownControl puts into its popup, with the second entry selected.
            root.Children.Add(DropdownControl.CreateListBox("dropdownList", entries, selectedIndex: 1));

            // The hover goes through the real Enter handler rather than through a colour set by
            // hand, so this shows what the game would draw.
            entries[2].InvokeEventEnter(new MouseEvent(0, 0));

            return root;
        }

        /// <summary>
        /// The handbook row style, forced - without a client there are no stacks, and Auto would
        /// take a list of captions for a menu.
        /// </summary>
        private static RectangleControl BuildDropdownItemRows()
        {
            RectangleControl root = CreateRoot();

            var entries = new[]
            {
                new DropdownItem("Jam"),
                new DropdownItem("Meat Stew"),
                new DropdownItem("Porridge"),
                new DropdownItem("Scrambled Eggs")
            };

            foreach (DropdownItem entry in entries)
            {
                entry.Name = entry.Text;
            }

            root.Children.Add(DropdownControl.CreateListBox(
                "itemRows", entries, selectedIndex: -1, style: DropdownRowStyle.ItemList));

            entries[1].InvokeEventEnter(new MouseEvent(0, 0));

            return root;
        }

        private static RectangleControl BuildContextMenuHover()
        {
            RectangleControl root = BuildContextMenuItems();
            RectangleControl stack = (RectangleControl)root.Children[0];

            ((ContextMenuItem)stack.Children[0]).InvokeEventEnter(new MouseEvent(0, 0));

            return root;
        }

        /// <summary>
        /// The focus states, produced by the real GotFocus and Enter handlers rather than by
        /// setting colours by hand - so this renders whatever a focused button actually looks
        /// like, and the scale checks cover the ring along with everything else.
        /// </summary>
        private static RectangleControl BuildKeyboardFocus()
        {
            RectangleControl root = CreateRoot();

            var plain = new ButtonControl(_Name: "plain");
            plain.Text = "Not focused";
            root.Children.Add(plain);

            var focused = new ButtonControl(_Name: "focused");
            focused.Text = "Focused";
            root.Children.Add(focused);

            var both = new ButtonControl(_Name: "focusedAndHovered");
            both.Text = "Focused and hovered";
            root.Children.Add(both);

            focused.InvokeGotFocus();

            both.InvokeGotFocus();
            both.InvokeEventEnter(new MouseEvent(0, 0));

            return root;
        }

        /// <summary>
        /// Side by side proof of what ClipsChildren does. Both containers are 100 high and hold
        /// five rows that need far more than that.
        /// </summary>
        private static RectangleControl BuildClipping()
        {
            RectangleControl root = CreateRoot();
            root.InsideOrientation = Orientation.Left;

            // Both clip. The left one has more rows than fit and is cut, the right one fits and
            // looks untouched - clipping costs nothing when there is no overflow.
            //
            // The unclipped counterpart is deliberately not rendered here: without clipping the
            // overflow check squashes the last rows to zero height, which CheckNoZeroSizedControls
            // rightly reports as a broken layout. That comparison is made numerically in
            // Program.CheckClipping instead.
            root.Children.Add(BuildRowBox("overflowing", rows: 5));
            root.Children.Add(BuildRowBox("fitting", rows: 1));

            return root;
        }

        private static RectangleControl BuildRowBox(string name, int rows)
        {
            var box = new RectangleControl(
                borderWidth: 2,
                borderColor: new ElementColor(1.0, 1.0, 1.0, 0.4),
                _Padding: 8,
                _Name: name);

            box.InsideOrientation = Orientation.Top;
            box.Size = new PointD(150, 88);
            box.IsAutoSize = false;
            box.ClipsChildren = true;

            for (int i = 0; i < rows; i++)
            {
                var row = new ButtonControl(_Name: name + i);
                row.Text = "Row " + i;
                box.Children.Add(row);
            }

            return box;
        }

        /// <summary>
        /// A scrolling container, driven through the real API rather than by setting the offset
        /// field: laid out once so the content size is known, then scrolled, then handed back
        /// for the harness to lay out again - which is exactly the sequence in the game.
        /// </summary>
        private static RectangleControl BuildScrolling(bool vertical, bool horizontal)
        {
            RectangleControl root = CreateRoot();

            var list = new RectangleControl(
                borderWidth: 2,
                borderColor: new ElementColor(1.0, 1.0, 1.0, 0.25),
                _Padding: 6,
                _Name: "list");

            list.InsideOrientation = Orientation.Top;
            list.Size = new PointD(220, 140);
            list.IsAutoSize = false;
            list.EnableVerticalScrollbar = vertical;
            list.EnableHorizontalScrollbar = horizontal;

            for (int i = 0; i < 8; i++)
            {
                var row = new ButtonControl(_Name: "row" + i);

                // Wide captions on a couple of rows, so the horizontal case has something to
                // actually overflow with.
                row.Text = horizontal && i % 3 == 0
                    ? "Row " + i + " with a much longer caption"
                    : "Row " + i;

                list.Children.Add(row);
            }

            root.Children.Add(list);

            // ScrollTo clamps against the content, which is only known after a layout pass.
            root.PerformLayout();
            list.ScrollTo(
                horizontal ? list.MaxScrollOffset.X / 2 : 0,
                vertical ? list.MaxScrollOffset.Y / 3 : 0);

            return root;
        }

        /// <summary>
        /// The shipped showcase, built by the mod's own code rather than by a copy of it here -
        /// so the picture cannot show a screen that no longer exists.
        /// </summary>
        private static RectangleControl BuildShowcase()
        {
            var root = new RectangleControl(
                backgroundColor: new ElementColor(0.20, 0.16, 0.13, 1.0),
                _Name: "root");

            ModernVintageGUI.Samples.ControlShowcase.Build(root, capi: null, withTitleBar: true);

            return root;
        }

        private static RectangleControl BuildInventoryGrid()
        {
            RectangleControl root = CreateRoot();

            var grid = new InventoryGridControl(columns: 6, _Name: "grid");
            grid.SetSlotCount(18);

            // One slot under the cursor, through the real Enter handler - so this shows the
            // highlight the game would draw and not a colour set by hand here.
            grid.Slots[7].InvokeEventEnter(new MouseEvent(0, 0));

            root.Children.Add(grid);
            return root;
        }

        private static RectangleControl BuildScrollingInventoryGrid()
        {
            RectangleControl root = CreateRoot();

            var grid = new InventoryGridControl(columns: 6, _Name: "grid");
            grid.SetSlotCount(48);

            // Three rows of lattice: 3 slots plus the two gaps between them.
            double visibleHeight = 3 * ItemSlotControl.UnscaledSlotSize
                                 + 2 * ItemSlotControl.UnscaledSlotPadding;
            double visibleWidth = 6 * ItemSlotControl.UnscaledSlotSize
                                + 5 * ItemSlotControl.UnscaledSlotPadding;

            double inset = InventoryGridControl.UnscaledInset * 2;
            grid.Size = new PointD(
                visibleWidth + inset + ScrollbarStyle.UnscaledWidth,
                visibleHeight + inset);
            grid.IsAutoSize = false;
            grid.EnableVerticalScrollbar = true;

            root.Children.Add(grid);

            // ScrollTo needs the content size, which only exists after a layout pass.
            root.PerformLayout();
            grid.ScrollTo(0, (ItemSlotControl.UnscaledSlotSize + ItemSlotControl.UnscaledSlotPadding));

            // The slot in the top left corner of the viewport once the grid has been scrolled by
            // a row - the worst case for the selection ring, which reaches outside its slot on
            // exactly the two sides the clip cuts hardest. If the grid ever stops leaving room
            // for it, this picture shows a highlight with a flat top and a flat left side.
            grid.Slots[6].InvokeEventEnter(new MouseEvent(0, 0));

            return root;
        }

        private static RectangleControl BuildStretchedLabel()
        {
            RectangleControl root = CreateRoot();

            var wide = new ButtonControl(_Name: "wideButton");
            wide.Text = "A deliberately wide button";
            root.Children.Add(wide);

            var label = new TextLabelControl("short", _Name: "stretchedLabel");
            label.Orientation = TextOrientation.Center;
            root.Children.Add(label);

            return root;
        }
    }
}
