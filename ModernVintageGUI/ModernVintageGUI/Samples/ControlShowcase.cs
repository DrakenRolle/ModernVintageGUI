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
            DialogInventory? gridInventory = null)
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

            parent.Children.Add(content);
        }

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

            return column;
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
        private static UIControl BuildRightColumn(ICoreClientAPI? capi, DialogInventory? gridInventory)
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

            return column;
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

        private static UIControl BuildInventoryGrid(ICoreClientAPI? capi, DialogInventory? gridInventory)
        {
            var grid = new InventoryGridControl(GridColumns, _Name: "inventoryGrid");

            IInventory? inventory = gridInventory?.Inventory;

            if (capi != null && inventory != null && gridInventory != null)
            {
                // An inventory of the mod's own, kept by the server and saved with the player -
                // not the hotbar, which only made the grid a second view of the bar the player
                // already has, and not a made up one, which would be a picture rather than an
                // inventory.
                //
                // It starts empty and stays whatever the player leaves in it. Putting stacks in
                // here from code would be conjuring items out of nothing, which is exactly what
                // an inventory must not do.
                //
                // announceOpen is off because DialogInventory opens it, and opens it on the
                // server too - which the grid on its own cannot do for an inventory the server
                // has not heard of yet.
                grid.SetInventory(
                    inventory,
                    capi,
                    sendPacket: gridInventory.SendPacket,
                    announceOpen: false);
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
