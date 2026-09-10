using Cairo;
using IS2Mod.ControlTypes;
using IS2Mod.Enums;
using ModernVintageGUI.ControlTypes;
using System.Collections.Generic;
using Vintagestory.API.Client;

namespace ModernVintageGUI.Samples
{
    /// <summary>
    /// Complexity 3: a master-detail browser in a split panel.
    ///
    /// Three panels side by side - categories as a tree, the entries of the picked category as
    /// a list, and the picked entry's details - and under all of that a log, with a horizontal
    /// bar between the two halves. Every bar can be dragged; a panel dragged too small for its
    /// content grows a scrollbar rather than reflowing it.
    ///
    /// The wiring is the everyday kind: the tree fills the list, the list fills the detail view
    /// (in <see cref="ListViewDetailMode.Attached"/>, so the panel stands still while the list is
    /// browsed), and everything that happens is written into the log.
    /// </summary>
    public static class ExplorerSample
    {
        public const string Id = "explorer";

        /// <summary>The whole screen, in author units.</summary>
        private const double Width = 640.0;
        private const double Height = 420.0;

        /// <summary>How much of the height the browser takes; the log gets the rest.</summary>
        private const double BrowserShare = 0.72;

        private sealed class Entry
        {
            public string Name;
            public string Hardness;
            public string Text;
            public string Layer;

            public Entry(string name, string hardness, string layer, string text)
            {
                Name = name;
                Hardness = hardness;
                Layer = layer;
                Text = text;
            }
        }

        private static readonly Dictionary<string, Entry[]> Catalogue = new Dictionary<string, Entry[]>
        {
            ["rock"] = new[]
            {
                new Entry("Granite", "hard", "Deep", "A coarse grained rock. Most millstones are cut from it."),
                new Entry("Andesite", "hard", "Middle", "Volcanic, grey and fine grained."),
                new Entry("Chalk", "soft", "Upper", "Pale and easily worked. Burns to quicklime."),
                new Entry("Basalt", "hard", "Deep", "Dark and dense, from cooled lava."),
                new Entry("Limestone", "soft", "Upper", "The rock most caves were dissolved out of.")
            },
            ["wood"] = new[]
            {
                new Entry("Oak", "hard", "Surface", "Slow growing, heavy, and the usual choice for anything that has to last."),
                new Entry("Birch", "soft", "Surface", "Light and pale. Burns quickly."),
                new Entry("Pine", "soft", "Surface", "Straight and resinous. Good for planks, poor for a fire indoors.")
            },
            ["metal"] = new[]
            {
                new Entry("Copper", "soft", "Middle", "The first metal anyone works. Soft, and easy to cast."),
                new Entry("Tin", "soft", "Middle", "On its own nearly useless; with copper it is bronze."),
                new Entry("Iron", "hard", "Deep", "Needs a bloomery and a lot of charcoal.")
            }
        };

        public static void Build(UIControl parent, ICoreClientAPI? capi)
        {
            parent.InsideOrientation = Orientation.Top;

            // The log first, because everything else writes into it.
            RectangleControl log = UI.Scroll(Width - 20, 70);
            log.Name = "log";
            log.Padding = 4;

            void Log(string line)
            {
                TextLabelControl entry = UI.Label(line, 14);
                entry.Name = "logLine" + log.Children.Count;
                log.Children.Add(entry);

                // The newest line at the bottom, where the eye is.
                log.ScrollTo(0, double.MaxValue);
                capi?.ShowChatMessage("Explorer: " + line);
            }

            // Details on the right. Attached mode: the list fills it, this code places it.
            var list = new ListViewControl(_Name: "entries")
            {
                DetailMode = ListViewDetailMode.Attached,
                Size = new PointD(200, 280),
                IsAutoSize = false
            };

            DetailViewControl details = list.DetailView;
            details.Name = "details";
            details.Size = new PointD(200, 280);
            details.IsAutoSize = false;

            // Categories on the left.
            var tree = new TreeViewControl(_Name: "categories")
            {
                Size = new PointD(150, 280),
                IsAutoSize = false
            };

            TreeNode materials = tree.AddNode("Materials", iconName: GuiIcons.Basket);
            materials.Add("Rock", "rock", GuiIcons.Erode);
            materials.Add("Wood", "wood", GuiIcons.Tree);
            materials.Add("Metal", "metal", GuiIcons.Ring);
            materials.Expand();

            tree.AddNode("Tools", iconName: GuiIcons.Handheld);
            tree.AddNode("Clothing", iconName: GuiIcons.Shirt);

            tree.SelectionChanged += (sender, e) =>
            {
                string? key = e.Value as string;

                if (key != null && Catalogue.TryGetValue(key, out Entry[]? entries))
                {
                    list.SetItems(ToItems(entries));
                    list.Select(0);
                    Log("Category: " + e.Node?.Text + " (" + entries.Length + " entries)");
                }
                else
                {
                    list.SetItems(null);
                    details.Clear();
                    Log("Category: " + (e.Node?.Text ?? "none") + " (empty)");
                }
            };

            list.SelectionChanged += (sender, e) =>
            {
                if (e.Item != null)
                    Log("Picked " + e.Item.Text);
            };

            // The browser: three panels, the middle one widest.
            SplitPanelControl browser = UI.Split(Orientation.Left, tree, list, details);
            browser.Name = "browser";
            // A little under the panel it sits in: its own margin on each side, and the bar.
            browser.Size = new PointD(Width - 10, Height * BrowserShare - 20);
            browser.IsAutoSize = false;

            browser.SetFractions(0.25, 0.40, 0.35);
            browser.SplitterMoved += (sender, e) => Log("Browser bar " + e.SplitterIndex + " moved");

            // And the whole screen: the browser over the log, with a horizontal bar between.
            SplitPanelControl screen = UI.Split(
                Orientation.Top,
                browser,
                UI.Column(UI.Heading("Log"), log));
            screen.Name = "screen";
            screen.Size = new PointD(Width, Height);
            screen.IsAutoSize = false;

            screen.SetFractions(BrowserShare, 1 - BrowserShare);

            parent.Add(UI.Title("Material explorer"));
            parent.Add(screen);

            // Something showing from the start, so the picture - and the first look in game -
            // is a filled screen rather than three empty boxes.
            list.SetItems(ToItems(Catalogue["rock"]));
            list.Select(0);
            list.ShowDetails(list.Items[0]);

            Log("Ready. Drag the bars between the panels.");
        }

        private static IEnumerable<ListViewItem> ToItems(Entry[] entries)
        {
            foreach (Entry entry in entries)
            {
                yield return new ListViewItem(entry.Name, value: entry.Name.ToLowerInvariant())
                {
                    Secondary = entry.Hardness,
                    Description = entry.Text,
                    Details =
                    {
                        new DetailEntry("Layer", entry.Layer),
                        new DetailEntry("Hardness", entry.Hardness)
                    }
                };
            }
        }
    }
}
