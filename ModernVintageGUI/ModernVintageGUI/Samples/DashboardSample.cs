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
        private const double UpperShare = 0.62;

        private static readonly string[] Machines = { "Quern", "Bloomery", "Kiln", "Helve hammer", "Pulverizer", "Windmill" };

        public static void Build(UIControl parent, ICoreClientAPI? capi)
        {
            parent.InsideOrientation = Orientation.Top;

            var random = new Random(12345);

            // --- Log and alerts, first, because the rest writes into them.
            RectangleControl log = ScrollBox("log", Width - 40, 110);
            RectangleControl alerts = ScrollBox("alerts", Width - 40, 110);

            void Log(string line)
            {
                log.Children.Add(Label(line, 14, "logLine" + log.Children.Count));
                log.ScrollTo(0, double.MaxValue);
                capi?.ShowChatMessage("Dashboard: " + line);
            }

            void Alert(string line)
            {
                alerts.Children.Add(Label("! " + line, 14, "alertLine" + alerts.Children.Count));
                alerts.ScrollTo(0, double.MaxValue);
                Log("Alert: " + line);
            }

            // --- Stats: a bar per resource, and a button that pretends a tick happened.
            var bars = new ProgressBarControl[4];
            string[] names = { "Power", "Ore", "Charcoal", "Water" };
            double[] values = { 0.82, 0.35, 0.60, 0.15 };

            var stats = new RectangleControl(_Name: "stats", _Margin: 0, _Padding: 4)
            {
                InsideOrientation = Orientation.Top
            };

            stats.Children.Add(Heading("Production"));

            for (int i = 0; i < bars.Length; i++)
            {
                bars[i] = new ProgressBarControl(_Name: "stat_" + names[i])
                {
                    Value = values[i],
                    Text = names[i] + "  " + (int)(values[i] * 100) + "%",
                    Size = new PointD(180, ProgressBarControl.UnscaledDefaultHeight),
                    IsAutoSize = false,
                    BarColor = BarColorFor(values[i])
                };

                stats.Children.Add(bars[i]);
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

            var tick = new ButtonControl(_Name: "tickButton")
            {
                Text = "Simulate a tick",
                IconName = GuiIcons.Repeat
            };

            tick.Clicked += (sender, e) => Tick();
            stats.Children.Add(tick);

            // --- Map: a canvas the player paints on with the picked colour.
            var picker = new ColorPickerControl(_Name: "mapColor")
            {
                Size = new PointD(120, 80),
                IsAutoSize = false,
                Orientation = Orientation.Center
            };

            var map = new PixelCanvasControl(columns: 16, rows: 16, unscaledPixelSize: 8, _Name: "map")
            {
                DrawMode = true,
                ShowGrid = true,
                DrawColor = picker.SelectedColor,
                Orientation = Orientation.Center
            };

            ControlShowcase.PaintHouse(map);
            picker.ColorChanged += (sender, color) => map.DrawColor = color;

            var mapPanel = new RectangleControl(_Name: "mapPanel", _Margin: 0, _Padding: 4)
            {
                InsideOrientation = Orientation.Top
            };

            mapPanel.Children.Add(Heading("Map - right drag paints"));
            mapPanel.Children.Add(map);
            mapPanel.Children.Add(picker);

            // --- Machines: a tree, with one node per machine and its parts under it.
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

            var machinePanel = new RectangleControl(_Name: "machinePanel", _Margin: 0, _Padding: 4)
            {
                InsideOrientation = Orientation.Top
            };

            machinePanel.Children.Add(Heading("Machines"));
            machinePanel.Children.Add(machines);

            // --- The upper half: three panels. A little under the panel it sits in: its own
            // margin on each side, and the bar.
            var upper = new SplitPanelControl(panelCount: 3, Orientation.Left, _Name: "upper")
            {
            };

            upper.Panels[0].Children.Add(stats);
            upper.Panels[1].Children.Add(mapPanel);
            upper.Panels[2].Children.Add(machinePanel);
            upper.SetFractions(0.30, 0.38, 0.32);

            // --- The lower half: tabs. The switches act on the upper half.
            var switches = new RectangleControl(_Name: "switches", _Margin: 0, _Padding: 4)
            {
                InsideOrientation = Orientation.Top
            };

            var showMap = new CheckboxControl("Show map", isChecked: true, _Name: "showMap");
            showMap.CheckedChanged += (sender, on) =>
            {
                // The panel goes, and its bar with it; the other two share its room.
                upper.Panels[1].IsVisible = on;
                Log(on ? "Map shown" : "Map hidden");
            };
            switches.Children.Add(showMap);

            var freeze = new CheckboxControl("Freeze the upper half", isChecked: false, _Name: "freeze");
            freeze.CheckedChanged += (sender, on) =>
            {
                upper.IsEnabled = !on;
                Log(on ? "Upper half frozen" : "Upper half live");
            };
            switches.Children.Add(freeze);

            var lockBars = new CheckboxControl("Lock the bars", isChecked: false, _Name: "lockBars");
            lockBars.CheckedChanged += (sender, on) =>
            {
                upper.IsResizable = !on;
                Log(on ? "Bars locked" : "Bars unlocked");
            };
            switches.Children.Add(lockBars);

            var switchButtons = new RectangleControl(_Name: "switchButtons", _Margin: 0, _Padding: 0)
            {
                InsideOrientation = Orientation.Left
            };

            var equalise = new ButtonControl(_Name: "equalise")
            {
                Text = "Equal panels"
            };

            equalise.Clicked += (sender, e) => { upper.SetFractions(1, 1, 1); Log("Panels equalised"); };
            switchButtons.Children.Add(equalise);

            var clearLog = new ButtonControl(_Name: "clearLog")
            {
                Text = "Clear log"
            };

            clearLog.Clicked += (sender, e) => { log.Children.Clear(); alerts.Children.Clear(); };
            switchButtons.Children.Add(clearLog);

            switches.Children.Add(switchButtons);

            var tabs = new TabsControl(_Name: "lowerTabs");
            tabs.AddTab("Log", log);
            tabs.AddTab("Alerts", alerts);
            tabs.AddTab("Switches", switches);

            var lower = new RectangleControl(_Name: "lower", _Margin: 0, _Padding: 4)
            {
                InsideOrientation = Orientation.Top
            };

            lower.Children.Add(tabs);

            // --- The whole screen: upper over lower, a bar between them.
            var screen = new SplitPanelControl(panelCount: 2, Orientation.Top, _Name: "screen")
            {
                Size = new PointD(Width, Height)
            };

            screen.Panels[0].Children.Add(upper);
            screen.Panels[1].Children.Add(lower);
            //screen.SetFractions(UpperShare, 1 - UpperShare);
            screen.SplitterMoved += (sender, e) => Log("Halves resized");

            // --- The title line.
            var titleRow = new RectangleControl(_Name: "titleRow", _Margin: 0, _Padding: 0)
            {
                InsideOrientation = Orientation.Left
            };

            titleRow.Children.Add(new TextLabelControl(
                text: "Factory dashboard",
                fontName: GuiStyle.StandardFontName,
                fontSize: 22,
                fontWeight: FontWeight.Bold,
                textColor: new ElementColor(GuiStyle.DialogDefaultTextColor),
                orientation: TextOrientation.MiddleLeft)
            {
                Margin = 4
            });

            titleRow.Children.Add(new RectangleControl(borderWidth: 0, _Name: "titleGap", _Margin: 0, _Padding: 0)
            {
                Size = new PointD(20, 1),
                IsAutoSize = false
            });

            TextLabelControl hint = Label("Drag the bars. Switches are in the lower tabs.", 14, "hint");
            hint.Orientation = Orientation.Center;
            titleRow.Children.Add(hint);

            parent.Children.Add(titleRow);
            parent.Children.Add(screen);

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

        /// <summary>A framed, fixed size column that scrolls when its lines do not fit.</summary>
        private static RectangleControl ScrollBox(string name, double width, double height)
        {
            return new RectangleControl(
                borderWidth: 2,
                borderColor: new ElementColor(0.0, 0.0, 0.0, 0.4),
                _Name: name,
                _Padding: 4)
            {
                InsideOrientation = Orientation.Top,
                Size = new PointD(width, height),
                IsAutoSize = false,
                EnableVerticalScrollbar = true
            };
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
