using Cairo;
using IS2Mod.ControlTypes;
using IS2Mod.Enums;
using ModernVintageGUI.ControlTypes;
using System;
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
            RectangleControl log = UI.Scroll(220, 190);
            log.Name = "log";

            void Log(string line)
            {
                TextLabelControl entry = UI.Label(line, 14);
                entry.Name = "logLine" + log.Children.Count;
                log.Children.Add(entry);
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

            ProgressBarControl progress = UI.Progress(0, "Idle");
            progress.Name = "progress";
            progress.Size = new PointD(200, ProgressBarControl.UnscaledDefaultHeight);
            progress.IsAutoSize = false;
            progress.BarColor = new ElementColor(0.75, 0.45, 0.15, 1.0);

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

            var recipeNames = new string[Recipes.Length];

            for (int i = 0; i < Recipes.Length; i++)
                recipeNames[i] = Recipes[i].Name;

            DropdownControl picker = UI.Dropdown(pick =>
            {
                foreach (Recipe candidate in Recipes)
                {
                    if (candidate.Name == pick)
                        recipe = candidate;
                }

                stepsDone = 0;
                UpdateProgress();
                Log("Recipe: " + recipe.Name + " (" + recipe.Needs + ")");
            }, recipeNames);
            picker.Name = "recipePicker";

            TextLabelControl hint = UI.Label("Right drag turns the preview", 13);
            hint.Orientation = Orientation.Center;

            ButtonControl work = UI.Button("Work", Work, GuiIcons.Handheld);
            work.Name = "workButton";

            ButtonControl reset = UI.Button("Reset", Reset, GuiIcons.Undo);
            reset.Name = "resetButton";

            CheckboxControl repeat = UI.Checkbox("Repeat automatically", autoRepeat, on => { autoRepeat = on; Log(on ? "Repeat on" : "Repeat off"); });
            repeat.Name = "repeat";

            RectangleControl middle = UI.Column(
                UI.Heading("Recipe"),
                picker,
                viewer,
                hint,
                UI.Heading("Progress"),
                progress,
                UI.Row(work, reset),
                repeat);
            middle.Name = "middle";
            middle.Padding = 4;

            // --- The left: slots.
            var input = new InventoryGridControl(columns: 3, _Name: "inputSlots");
            input.SetSlotCount(9);

            var fuel = new InventoryGridControl(columns: 1, _Name: "fuelSlot");
            fuel.SetSlotCount(1);

            var output = new InventoryGridControl(columns: 2, _Name: "outputSlots");
            output.SetSlotCount(2);

            RectangleControl left = UI.Column(
                UI.Heading("Input"),
                input,
                UI.Heading("Fuel"),
                fuel,
                UI.Heading("Output"),
                output);
            left.Name = "left";
            left.Padding = 4;

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

            RectangleControl right = UI.Column(
                UI.Tabs(
                    ("Recipes", book),
                    ("Log", log)));
            right.Name = "right";
            right.Padding = 4;

            RectangleControl body = UI.Row(left, middle, right);
            body.Name = "body";

            parent.Add(UI.Title("Workshop"));
            parent.Add(body);

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
    }
}
