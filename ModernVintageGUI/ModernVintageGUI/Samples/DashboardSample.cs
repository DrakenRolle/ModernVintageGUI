using Cairo;
using IS2Mod.ControlTypes;
using IS2Mod.Enums;
using ModernVintageGUI.ControlTypes;
using System;
using Vintagestory.API.Client;

namespace ModernVintageGUI.Samples
{
    /// <summary>
    /// Complexity 5: a dashboard.
    ///
    /// Split panels inside split panels, both ways round: the upper half is three panels side
    /// by side - production stats, a map the player can paint on, and a tree of machines - and
    /// the lower half is a tabbed area with a log, alerts and switches. The two halves are
    /// themselves a stacked split, so the player can pull the log up over the stats.
    ///
    /// The switches are the point of the lower half. "Show map" hides the middle panel and its
    /// bar, so the other two panels take its room; "Freeze" disables the whole upper half; and
    /// both are undone by the same switch. That is the framework's runtime editing at work on a
    /// screen big enough for it to matter.
    /// </summary>
    public static class DashboardSample
    {
        public const string Id = "dashboard";

        private const double Width = 720.0;
        private const double Height = 460.0;

        private static readonly string[] Machines = { "Quern", "Bloomery", "Kiln", "Helve hammer", "Pulverizer", "Windmill" };

        public static void Build(UIControl parent, ICoreClientAPI? capi)
        {
            parent.InsideOrientation = Orientation.Top;

            var random = new Random(12345);

            // --- Log and alerts, first, because the rest writes into them.
            RectangleControl log = UI.Scroll(Width - 40, 110);
            log.Name = "log";

            RectangleControl alerts = UI.Scroll(Width - 40, 110);
            alerts.Name = "alerts";

            void Log(string line)
            {
                // Wrapped to the box, so a long line is read rather than cut at the bar.
                TextLabelControl entry = UI.Paragraph(line, UI.ScrollContentWidth(Width - 40), 14);
                entry.Name = "logLine" + log.Children.Count;
                log.Children.Add(entry);
                log.ScrollTo(0, double.MaxValue);
                capi?.ShowChatMessage("Dashboard: " + line);
            }

            void Alert(string line)
            {
                TextLabelControl entry = UI.Paragraph("! " + line, UI.ScrollContentWidth(Width - 40), 14);
                entry.Name = "alertLine" + alerts.Children.Count;
                alerts.Children.Add(entry);
                alerts.ScrollTo(0, double.MaxValue);
                Log("Alert: " + line);
            }

            // --- Stats: a bar per resource, and a button that pretends a tick happened.
            var bars = new ProgressBarControl[4];
            string[] names = { "Power", "Ore", "Charcoal", "Water" };
            double[] values = { 0.82, 0.35, 0.60, 0.15 };

            RectangleControl stats = UI.Column(UI.Heading("Production"));
            stats.Name = "stats";
            stats.Padding = 4;

            for (int i = 0; i < bars.Length; i++)
            {
                bars[i] = UI.Progress(values[i], names[i] + "  " + (int)(values[i] * 100) + "%");
                bars[i].Name = "stat_" + names[i];
                bars[i].Size = new PointD(180, ProgressBarControl.UnscaledDefaultHeight);
                bars[i].IsAutoSize = false;
                bars[i].BarColor = BarColorFor(values[i]);

                stats.Add(bars[i]);
            }

            void Tick()
            {
                for (int i = 0; i < bars.Length; i++)
                {
                    values[i] = Math.Clamp(values[i] + (random.NextDouble() - 0.5) * 0.3, 0, 1);
                    bars[i].Value = values[i];
                    bars[i].Text = names[i] + "  " + (int)(values[i] * 100) + "%";
                    bars[i].BarColor = BarColorFor(values[i]);

                    if (values[i] < 0.2)
                        Alert(names[i] + " is low");
                }

                Log("Tick");
            }

            ButtonControl tickButton = UI.Button("Simulate a tick", Tick, GuiIcons.Repeat);
            tickButton.Name = "tickButton";
            stats.Add(tickButton);

            // --- Map: a canvas the player paints on with the picked colour.
            var picker = new ColorPickerControl(_Name: "mapColor")
            {
                Size = new PointD(120, 80),
                IsAutoSize = false
            };

            var map = new PixelCanvasControl(columns: 16, rows: 16, unscaledPixelSize: 8, _Name: "map")
            {
                DrawMode = true,
                ShowGrid = true,
                DrawColor = picker.SelectedColor
            };

            ControlShowcase.PaintHouse(map);
            picker.ColorChanged += (sender, color) => map.DrawColor = color;

            RectangleControl mapPanel = UI.Column(
                UI.Heading("Map - right drag paints"),
                map.Aligned(Orientation.Center),
                picker.Aligned(Orientation.Center));
            mapPanel.Name = "mapPanel";
            mapPanel.Padding = 4;

            // --- Machines: a tree, with one node per machine and its parts under it.
            // Sized by its nodes rather than a box: what does not fit its panel is scrolled.
            var machines = new TreeViewControl(_Name: "machines")
            {
                IsAutoSize = true
            };

            foreach (string machine in Machines)
            {
                TreeNode node = machines.AddNode(machine, machine, GuiIcons.GrowShrink);
                node.Add("Input", machine + ".in");
                node.Add("Output", machine + ".out");
                node.Add("Power", machine + ".power");
            }

            machines.Nodes[0].Expand();
            machines.SelectionChanged += (sender, e) => Log("Machine: " + (e.Value ?? "none"));

            RectangleControl machinePanel = UI.Column(UI.Heading("Machines"), machines);
            machinePanel.Name = "machinePanel";
            machinePanel.Padding = 4;

            // --- The upper half: three panels, at the split panel's default size. When the
            // screen's bar is dragged up over it, the panel it sits in scrolls it.
            SplitPanelControl upper = UI.Split(Orientation.Left, stats, mapPanel, machinePanel);
            upper.Name = "upper";

            upper.SetFractions(0.30, 0.38, 0.32);

            // --- The lower half: tabs. The switches act on the upper half.
            CheckboxControl showMap = UI.Checkbox("Show map", true, on =>
            {
                // The panel goes, and its bar with it; the other two share its room.
                upper.Panels[1].IsVisible = on;
                Log(on ? "Map shown" : "Map hidden");
            });
            showMap.Name = "showMap";

            CheckboxControl freeze = UI.Checkbox("Freeze the upper half", false, on =>
            {
                upper.IsEnabled = !on;
                Log(on ? "Upper half frozen" : "Upper half live");
            });
            freeze.Name = "freeze";

            CheckboxControl lockBars = UI.Checkbox("Lock the bars", false, on =>
            {
                upper.IsResizable = !on;
                Log(on ? "Bars locked" : "Bars unlocked");
            });
            lockBars.Name = "lockBars";

            ButtonControl equalise = UI.Button("Equal panels", () => { upper.SetFractions(1, 1, 1); Log("Panels equalised"); });
            equalise.Name = "equalise";

            ButtonControl clearLog = UI.Button("Clear log", () => { log.Children.Clear(); alerts.Children.Clear(); });
            clearLog.Name = "clearLog";

            RectangleControl switches = UI.Column(
                showMap,
                freeze,
                lockBars,
                UI.Row(equalise, clearLog));
            switches.Name = "switches";
            switches.Padding = 4;

            RectangleControl lower = UI.Column(
                UI.Tabs(
                    ("Log", log),
                    ("Alerts", alerts),
                    ("Switches", switches)));
            lower.Name = "lower";
            lower.Padding = 4;

            // --- The whole screen: upper over lower, a bar between them.
            SplitPanelControl screen = UI.Split(Orientation.Top, upper, lower);
            screen.Name = "screen";
            screen.Size = new PointD(Width, Height);
            screen.IsAutoSize = false;

            screen.SplitterMoved += (sender, e) => Log("Halves resized");

            parent.Add(UI.Row(
                UI.Title("Factory dashboard"),
                UI.Spacer(20, 1),
                UI.Label("Drag the bars. Switches are in the lower tabs.", 14).Aligned(Orientation.Center)));

            parent.Add(screen);

            Alert("Water is low");
            Log("Dashboard ready");
        }

        /// <summary>Green when there is plenty, amber in the middle, red when it runs out.</summary>
        private static ElementColor BarColorFor(double value)
        {
            if (value < 0.2)
                return new ElementColor(0.65, 0.12, 0.12, 1.0);

            if (value < 0.5)
                return new ElementColor(0.75, 0.55, 0.15, 1.0);

            return new ElementColor(0.25, 0.55, 0.20, 1.0);
        }
    }
}
