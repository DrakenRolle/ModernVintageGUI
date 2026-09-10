using Cairo;
using IS2Mod.ControlTypes;
using IS2Mod.Enums;
using ModernVintageGUI.ControlTypes;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace ModernVintageGUI.Samples
{
    /// <summary>
    /// Complexity 4: a crafting station.
    ///
    /// The screen a block entity with a process would open: slots for what goes in and what
    /// comes out, a recipe to pick with a turning 3D preview of its result, a progress bar and
    /// the buttons that drive it, and on the right a pair of tabs - the recipe book and a log.
    ///
    /// The slots are pictures here. A real station gives the grid an inventory the server knows
    /// about (see the inventory section of the README); this sample has no block entity behind
    /// it, so it draws the squares and leaves them empty rather than conjuring items.
    /// </summary>
    public static class WorkshopSample
    {
        public const string Id = "workshop";

        private sealed class Recipe
        {
            public string Name;
            public string Needs;
            public string Makes;
            public int Steps;

            public Recipe(string name, string needs, string makes, int steps)
            {
                Name = name;
                Needs = needs;
                Makes = makes;
                Steps = steps;
            }
        }

        private static readonly Recipe[] Recipes =
        {
            new Recipe("Copper ingot", "5 copper nuggets", "1 copper ingot", 4),
            new Recipe("Bronze ingot", "4 copper nuggets, 1 tin nugget", "1 bronze ingot", 6),
            new Recipe("Quern stone", "2 granite", "1 quern", 8),
            new Recipe("Charcoal", "8 firewood", "6 charcoal", 10)
        };

        public static void Build(UIControl parent, ICoreClientAPI? capi)
        {
            parent.InsideOrientation = Orientation.Top;

            // State of the station.
            Recipe recipe = Recipes[0];
            int stepsDone = 0;
            bool autoRepeat = false;

            // --- Log, first, because the rest writes into it.
            var log = new RectangleControl(
                borderWidth: 2,
                borderColor: new ElementColor(0.0, 0.0, 0.0, 0.4),
                _Name: "log",
                _Padding: 4)
            {
                InsideOrientation = Orientation.Top,
                Size = new PointD(220, 190),
                IsAutoSize = false,
                EnableVerticalScrollbar = true
            };

            void Log(string line)
            {
                log.Children.Add(Label(line, 14, "logLine" + log.Children.Count));
                log.ScrollTo(0, double.MaxValue);
                capi?.ShowChatMessage("Workshop: " + line);
            }

            // --- The middle: preview, picker, progress, buttons.
            var viewer = new ShapeViewerControl(_Name: "preview")
            {
                Size = new PointD(140, 140),
                IsAutoSize = false,
                Orientation = Orientation.Center
            };

            ShowSomething(capi, viewer);

            var progress = new ProgressBarControl(_Name: "progress")
            {
                Value = 0,
                Text = "Idle",
                Size = new PointD(200, ProgressBarControl.UnscaledDefaultHeight),
                IsAutoSize = false,
                BarColor = new ElementColor(0.75, 0.45, 0.15, 1.0)
            };

            void UpdateProgress()
            {
                progress.Max = recipe.Steps;
                progress.Value = stepsDone;
                progress.Text = stepsDone == 0
                    ? "Idle"
                    : stepsDone >= recipe.Steps ? "Done" : stepsDone + " / " + recipe.Steps;
            }

            void Work()
            {
                if (stepsDone >= recipe.Steps)
                {
                    stepsDone = 0;

                    if (!autoRepeat)
                    {
                        UpdateProgress();
                        Log("Output taken; press Work to start again");
                        return;
                    }
                }

                stepsDone++;
                UpdateProgress();

                if (stepsDone >= recipe.Steps)
                    Log("Finished: " + recipe.Makes);
            }

            void Reset()
            {
                stepsDone = 0;
                UpdateProgress();
                Log("Reset");
            }

            var picker = new DropdownControl(_Name: "recipePicker");
            var recipeItems = new List<DropdownItem>(Recipes.Length);

            foreach (Recipe entry in Recipes)
                recipeItems.Add(new DropdownItem(entry.Name, value: entry.Name));

            picker.SetItems(recipeItems);
            picker.Select(0);
            picker.SelectionChanged += (sender, e) =>
            {
                string pick = e.Item?.Text ?? "";

                foreach (Recipe candidate in Recipes)
                {
                    if (candidate.Name == pick)
                        recipe = candidate;
                }

                stepsDone = 0;
                UpdateProgress();
                Log("Recipe: " + recipe.Name + " (" + recipe.Needs + ")");
            };

            var middle = new RectangleControl(_Name: "middle", _Margin: 0, _Padding: 4)
            {
                InsideOrientation = Orientation.Top
            };

            middle.Children.Add(Heading("Recipe"));
            middle.Children.Add(picker);
            middle.Children.Add(viewer);

            TextLabelControl hint = Label("Right drag turns the preview", 13, "previewHint");
            hint.Orientation = Orientation.Center;
            middle.Children.Add(hint);

            middle.Children.Add(Heading("Progress"));
            middle.Children.Add(progress);

            var actions = new RectangleControl(_Name: "actions", _Margin: 0, _Padding: 0)
            {
                InsideOrientation = Orientation.Left
            };

            var work = new ButtonControl(_Name: "workButton")
            {
                Text = "Work",
                IconName = GuiIcons.Handheld
            };

            work.Clicked += (sender, e) => Work();
            actions.Children.Add(work);

            var reset = new ButtonControl(_Name: "resetButton")
            {
                Text = "Reset",
                IconName = GuiIcons.Undo
            };

            reset.Clicked += (sender, e) => Reset();
            actions.Children.Add(reset);

            middle.Children.Add(actions);

            var repeat = new CheckboxControl("Repeat automatically", isChecked: autoRepeat, _Name: "repeat");
            repeat.CheckedChanged += (sender, on) => { autoRepeat = on; Log(on ? "Repeat on" : "Repeat off"); };
            middle.Children.Add(repeat);

            // --- The left: slots.
            var input = new InventoryGridControl(columns: 3, _Name: "inputSlots");
            input.SetSlotCount(9);

            var fuel = new InventoryGridControl(columns: 1, _Name: "fuelSlot");
            fuel.SetSlotCount(1);

            var output = new InventoryGridControl(columns: 2, _Name: "outputSlots");
            output.SetSlotCount(2);

            var left = new RectangleControl(_Name: "left", _Margin: 0, _Padding: 4)
            {
                InsideOrientation = Orientation.Top
            };

            left.Children.Add(Heading("Input"));
            left.Children.Add(input);
            left.Children.Add(Heading("Fuel"));
            left.Children.Add(fuel);
            left.Children.Add(Heading("Output"));
            left.Children.Add(output);

            // --- The right: the recipe book and the log, as tabs.
            var book = new ListViewControl(_Name: "recipeBook")
            {
                Size = new PointD(220, 190),
                IsAutoSize = false
            };

            var rows = new List<ListViewItem>();

            foreach (Recipe entry in Recipes)
            {
                rows.Add(new ListViewItem(entry.Name, value: entry.Name)
                {
                    Secondary = entry.Steps + " steps",
                    Description = "Needs " + entry.Needs + ". Makes " + entry.Makes + ".",
                    Details =
                    {
                        new DetailEntry("Needs", entry.Needs),
                        new DetailEntry("Makes", entry.Makes),
                        new DetailEntry("Steps", entry.Steps.ToString())
                    }
                });
            }

            book.SetItems(rows);
            book.ItemActivated += (sender, e) =>
            {
                if (e.Value is string name)
                    picker.SelectByValue(name);
            };

            var tabs = new TabsControl(_Name: "tabs");
            tabs.AddTab("Recipes", book);
            tabs.AddTab("Log", log);

            var right = new RectangleControl(_Name: "right", _Margin: 0, _Padding: 4)
            {
                InsideOrientation = Orientation.Top
            };

            right.Children.Add(tabs);

            // --- The screen: a title over the three columns.
            parent.Children.Add(new TextLabelControl(
                text: "Workshop",
                fontName: GuiStyle.StandardFontName,
                fontSize: 22,
                fontWeight: FontWeight.Bold,
                textColor: new ElementColor(GuiStyle.DialogDefaultTextColor),
                orientation: TextOrientation.MiddleLeft)
            {
                Margin = 4
            });

            var body = new RectangleControl(_Name: "body", _Margin: 0, _Padding: 0)
            {
                InsideOrientation = Orientation.Left
            };

            body.Children.Add(left);
            body.Children.Add(middle);
            body.Children.Add(right);
            parent.Children.Add(body);

            UpdateProgress();
            Log("Station ready. Pick a recipe and press Work.");
        }

        /// <summary>
        /// Puts the first block with a shape into the preview - the sample has no real result
        /// item, and an empty frame says nothing about whether the viewer works. Nothing without
        /// a client: the harness lays the frame out and leaves it empty.
        /// </summary>
        private static void ShowSomething(ICoreClientAPI? capi, ShapeViewerControl viewer)
        {
            if (capi == null)
                return;

            foreach (Block block in capi.World.Blocks)
            {
                if (block?.Code == null || block.BlockId == 0 || block.Shape?.Base == null)
                    continue;

                viewer.ShowBlock(block);
                return;
            }
        }

        private static TextLabelControl Heading(string text)
        {
            return new TextLabelControl(
                text: text,
                fontName: GuiStyle.StandardFontName,
                fontSize: (int)GuiStyle.SmallFontSize,
                textColor: new ElementColor(GuiStyle.DialogDefaultTextColor),
                orientation: TextOrientation.MiddleLeft,
                _Name: "heading_" + text)
            {
                Margin = 4
            };
        }

        private static TextLabelControl Label(string text, int fontSize, string name)
        {
            return new TextLabelControl(
                text: text,
                fontName: GuiStyle.StandardFontName,
                fontSize: fontSize,
                textColor: new ElementColor(GuiStyle.DialogDefaultTextColor),
                orientation: TextOrientation.MiddleLeft,
                _Name: name)
            {
                Margin = 2
            };
        }
    }
}
