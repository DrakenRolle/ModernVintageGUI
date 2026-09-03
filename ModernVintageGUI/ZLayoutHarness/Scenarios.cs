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
                Name = "keyboard-focus",
                Description = "Three buttons, the middle one holding the keyboard focus and the "
                            + "last one both focused and hovered - the ring and the hover look "
                            + "are independent states and have to survive each other.",
                Build = BuildKeyboardFocus
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
