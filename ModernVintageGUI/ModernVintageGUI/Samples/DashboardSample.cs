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
        private const double UpperShare = 0.62;

        private static readonly string[] Machines = { "Quern", "Bloomery", "Kiln", "Helve hammer", "Pulverizer", "Windmill" };

        public static void Build(UIControl parent, ICoreClientAPI? capi)
        {
            parent.InsideOrientation = Orientation.Top;

            var random = new Random(12345);

            // --- Log and alerts, first, because the rest writes into them.
            RectangleControl log = UI.Scroll(Width - 40, 110).WithName("log");
            RectangleControl alerts = UI.Scroll(Width - 40, 110).WithName("alerts");

            void Log(string line)
            {
                log.Children.Add(UI.Label(line, 14).WithName("logLine" + log.Children.Count));
                log.ScrollTo(0, double.MaxValue);
                capi?.ShowChatMessage("Dashboard: " + line);
            }

            void Alert(string line)
            {
                alerts.Children.Add(UI.Label("! " + line, 14).WithName("alertLine" + alerts.Children.Count));
                alerts.ScrollTo(0, double.MaxValue);
                Log("Alert: " + line);
            }

            // --- Stats: a bar per resource, and a button that pretends a tick happened.
            var bars = new ProgressBarControl[4];
            string[] names = { "Power", "Ore", "Charcoal", "Water" };
            double[] values = { 0.82, 0.35, 0.60, 0.15 };

            RectangleControl stats = UI.Column(UI.Heading("Production")).WithName("stats").WithPadding(4);

            for (int i = 0; i < bars.Length; i++)
            {
                bars[i] = UI.Progress(values[i], names[i] + "  " + (int)(values[i] * 100) + "%")
                    .WithName("stat_" + names[i])
                    .WithSize(180, ProgressBarControl.UnscaledDefaultHeight);

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

            stats.Add(UI.Button("Simulate a tick", Tick, GuiIcons.Repeat).WithName("tickButton"));

            // --- Map: a canvas the player paints on with the picked colour.
            var picker = new ColorPickerControl(_Name: "mapColor").WithSize(120, 80);

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
                picker.Aligned(Orientation.Center))
                .WithName("mapPanel")
                .WithPadding(4);

            // --- Machines: a tree, with one node per machine and its parts under it.
            var machines = new TreeViewControl(_Name: "machines").WithSize(180, 240);

            foreach (string machine in Machines)
            {
                TreeNode node = machines.AddNode(machine, machine, GuiIcons.GrowShrink);
                node.Add("Input", machine + ".in");
                node.Add("Output", machine + ".out");
                node.Add("Power", machine + ".power");
            }

            machines.Nodes[0].Expand();
            machines.SelectionChanged += (sender, e) => Log("Machine: " + (e.Value ?? "none"));

            RectangleControl machinePanel = UI.Column(UI.Heading("Machines"), machines)
                .WithName("machinePanel")
                .WithPadding(4);

            // --- The upper half: three panels.
            SplitPanelControl upper = UI.Split(Orientation.Left, stats, mapPanel, machinePanel)
                .WithName("upper")
                // A little under the panel it sits in: its own margin on each side, and the bar.
                .WithSize(Width - 10, Height * UpperShare - 20);

            upper.SetFractions(0.30, 0.38, 0.32);

            // --- The lower half: tabs. The switches act on the upper half.
            RectangleControl switches = UI.Column(
                UI.Checkbox("Show map", true, on =>
                {
                    // The panel goes, and its bar with it; the other two share its room.
                    upper.Panels[1].IsVisible = on;
                    Log(on ? "Map shown" : "Map hidden");
                }).WithName("showMap"),

                UI.Checkbox("Freeze the upper half", false, on =>
                {
                    upper.IsEnabled = !on;
                    Log(on ? "Upper half frozen" : "Upper half live");
                }).WithName("freeze"),

                UI.Checkbox("Lock the bars", false, on =>
                {
                    upper.IsResizable = !on;
                    Log(on ? "Bars locked" : "Bars unlocked");
                }).WithName("lockBars"),

                UI.Row(
                    UI.Button("Equal panels", () => { upper.SetFractions(1, 1, 1); Log("Panels equalised"); })
                        .WithName("equalise"),
                    UI.Button("Clear log", () => { log.Children.Clear(); alerts.Children.Clear(); })
                        .WithName("clearLog")))
                .WithName("switches")
                .WithPadding(4);

            RectangleControl lower = UI.Column(
                UI.Tabs(
                    ("Log", log),
                    ("Alerts", alerts),
                    ("Switches", switches)))
                .WithName("lower")
                .WithPadding(4);

            // --- The whole screen: upper over lower, a bar between them.
            SplitPanelControl screen = UI.Split(Orientation.Top, upper, lower)
                .WithName("screen")
                .WithSize(Width, Height);

            screen.SetFractions(UpperShare, 1 - UpperShare);
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
