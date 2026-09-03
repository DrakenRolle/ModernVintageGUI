using Cairo;
using IS2Mod.ControlTypes;
using IS2Mod.Enums;
using ModernVintageGUI.ControlTypes;
using ModernVintageGUI.Enums;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using IOPath = System.IO.Path;
using System.Linq;

namespace LayoutHarness
{
    /// <summary>
    /// Runs the real layout code headlessly, renders the result to PNG and checks the
    /// invariants that the layout engine is supposed to hold.
    ///
    /// The point is to stop needing the game to find a layout bug: every scenario is laid out
    /// several times in a row, and if the tree moves between passes the harness says exactly
    /// which control changed.
    ///
    /// Usage:  dotnet run --project ZLayoutHarness [-- &lt;output directory&gt;]
    /// Exit code 0 = all checks passed, 1 = at least one failed.
    /// </summary>
    internal static class Program
    {
        private const int LayoutPasses = 5;

        /// <summary>
        /// The GUI scales to exercise. 1.0 is the reference every other scale is compared
        /// against; the game itself lets the player pick roughly this range.
        /// </summary>
        private static readonly double[] Scales = { 1.0, 1.5, 2.0 };

        /// <summary>
        /// How far the size at a given scale may deviate from "size at 1.0 times the scale".
        /// It cannot be exact: glyph advances are hinted and rounded per font size, so text does
        /// not measure to precisely twice its width at twice the size. Loose enough for that,
        /// tight enough that a dimension which does not scale at all still fails.
        /// </summary>
        private const double ScaleTolerance = 0.08;

        private static int Main(string[] args)
        {
            NativeCairo.Register();

            // "--docs [dir]" renders the README illustrations instead of running the checks.
            if (args.Length > 0 && args[0] == "--docs")
            {
                string docsDir = args.Length > 1
                    ? args[1]
                    : IOPath.Combine(AppContext.BaseDirectory, "doc-images");

                return DocImages.Generate(docsDir);
            }

            string outputDir = args.Length > 0
                ? args[0]
                : IOPath.Combine(AppContext.BaseDirectory, "layout-output");

            Directory.CreateDirectory(outputDir);

            Console.WriteLine("ModernVintageGUI layout harness");
            Console.WriteLine("output: " + outputDir);
            Console.WriteLine(new string('=', 78));
            Console.WriteLine();
            Console.WriteLine("Fonts resolve through the OS here. The game installs Lora into the");
            Console.WriteLine("system fonts, so text measures the same as in-game.");
            Console.WriteLine();

            var failures = new List<string>();

            foreach (Scenario scenario in Scenarios.All())
            {
                RunScenario(scenario, outputDir, failures);
            }

            CheckContextMenuAnchorIsFree(failures);

            Console.WriteLine();

            Console.WriteLine(new string('=', 78));

            if (failures.Count == 0)
            {
                Console.WriteLine("ALL CHECKS PASSED");
                return 0;
            }

            Console.WriteLine($"{failures.Count} CHECK(S) FAILED:");
            foreach (string failure in failures)
            {
                Console.WriteLine("  - " + failure);
            }

            return 1;
        }

        private static void RunScenario(Scenario scenario, string outputDir, List<string> failures)
        {
            Console.WriteLine($"### {scenario.Name}");
            Console.WriteLine(scenario.Description);
            Console.WriteLine();

            PointD? referenceSize = null;
            var freshSnapshots = new Dictionary<double, string>();

            foreach (double scale in Scales)
            {
                (PointD size, string snapshot) = RunAtScale(scenario, scale, outputDir, failures);
                freshSnapshots[scale] = snapshot;

                if (scale == 1.0)
                {
                    referenceSize = size;
                }
                else if (referenceSize.HasValue)
                {
                    CheckProportional(scenario.Name, scale, referenceSize.Value, size, failures);
                }
            }

            CheckSurvivesScaleChange(scenario, freshSnapshots, failures);

            Console.WriteLine();
        }

        /// <summary>
        /// In the game a dialog is built once and reopened many times, and the GUI scale can
        /// change in between - every Show() lays the same tree out again at whatever scale is
        /// current. Laying a reused tree out at scale S therefore has to give the same result as
        /// laying out a freshly built one at S. Anything a previous pass left behind that is
        /// still in author units (a measurement that got recorded as an explicit size, say)
        /// shows up here and nowhere else.
        /// </summary>
        private static void CheckSurvivesScaleChange(
            Scenario scenario, Dictionary<double, string> freshSnapshots, List<string> failures)
        {
            // There and back again, so a scale going down is covered as well as going up.
            double[] sequence = Scales.Concat(Scales.Reverse().Skip(1)).ToArray();

            RectangleControl root = scenario.Build();

            foreach (double scale in sequence)
            {
                root.LayoutScale = scale;
                root.PerformLayout();

                string snapshot = LayoutSnapshot.Capture(root);

                if (snapshot == freshSnapshots[scale])
                    continue;

                string diff = LayoutSnapshot.FirstDifference(freshSnapshots[scale], snapshot)!;
                string path = string.Join(" -> ", sequence.TakeWhile(s => s != scale).Append(scale)
                    .Select(s => s.ToString("0.##", CultureInfo.InvariantCulture) + "x"));

                failures.Add(
                    $"[{scenario.Name}] reusing the tree across a scale change ({path}) does not " +
                    $"match a freshly built tree at that scale: {diff}");

                Console.WriteLine($"  SCALE CHANGE BROKE THE TREE ({path}): {diff}");
                return;
            }

            Console.WriteLine($"  survives scale changes ({string.Join(" -> ", sequence.Select(s => s.ToString("0.##", CultureInfo.InvariantCulture) + "x"))})");
        }

        private static (PointD Size, string Snapshot) RunAtScale(Scenario scenario, double scale, string outputDir, List<string> failures)
        {
            string label = $"{scenario.Name} @ {scale.ToString("0.##", CultureInfo.InvariantCulture)}x";

            RectangleControl root = scenario.Build();
            root.LayoutScale = scale;

            string? firstSnapshot = null;

            for (int pass = 1; pass <= LayoutPasses; pass++)
            {
                root.PerformLayout();

                string snapshot = LayoutSnapshot.Capture(root);

                if (pass == 1)
                {
                    firstSnapshot = snapshot;
                }
                else if (snapshot != firstSnapshot)
                {
                    string diff = LayoutSnapshot.FirstDifference(firstSnapshot!, snapshot)!;
                    failures.Add($"[{label}] layout is not idempotent, pass 1 vs pass {pass}: {diff}");

                    Console.WriteLine($"  NOT IDEMPOTENT at pass {pass}: {diff}");
                    break;
                }
            }

            // Only the reference scale gets its tree dumped - three full dumps per scenario
            // would bury the interesting lines.
            if (scale == 1.0)
            {
                Console.WriteLine(firstSnapshot!.TrimEnd());
                Console.WriteLine();
            }

            Render(root, IOPath.Combine(
                outputDir,
                $"{scenario.Name}-x{scale.ToString("0.##", CultureInfo.InvariantCulture)}.png"));

            CheckNoZeroSizedControls(label, root, failures);
            CheckNoSiblingOverlap(label, root, failures);

            return (root.Size, firstSnapshot!);
        }

        #region Checks
        /// <summary>
        /// A ContextMenuControl hangs in the host tree purely as an anchor and must not cost any
        /// layout space. Two identical trees are laid out, one of them with a menu attached to a
        /// button - the result has to be the same down to the pixel.
        /// </summary>
        private static void CheckContextMenuAnchorIsFree(List<string> failures)
        {
            Console.WriteLine("### context menu anchor");
            Console.WriteLine("Attaching a ContextMenuControl must not change the host layout.");
            Console.WriteLine();

            RectangleControl BuildHost(bool withMenu)
            {
                var root = new RectangleControl(_Name: "root");
                root.InsideOrientation = Orientation.Top;
                root.Padding = 10;

                var opener = new ButtonControl(_Name: "opener");
                opener.Text = "Title bar mode";
                root.Children.Add(opener);

                var below = new ButtonControl(_Name: "below");
                below.Text = "Something below";
                root.Children.Add(below);

                if (withMenu)
                {
                    // Attaches itself to the opener in its constructor.
                    _ = new ContextMenuControl(
                        opener,
                        new List<ContextMenuItem>
                        {
                            new ContextMenuItem("Fixed"),
                            new ContextMenuItem("Moveable")
                        },
                        "modeMenu",
                        ContextMenuAnchor.BottomLeft);
                }

                return root;
            }

            RectangleControl plain = BuildHost(withMenu: false);
            RectangleControl withMenu = BuildHost(withMenu: true);

            plain.PerformLayout();
            withMenu.PerformLayout();

            void Compare(string what, PointD a, PointD b)
            {
                if (Math.Abs(a.X - b.X) < 0.001 && Math.Abs(a.Y - b.Y) < 0.001)
                    return;

                failures.Add(string.Format(
                    CultureInfo.InvariantCulture,
                    "[context menu anchor] attaching a menu changed {0}: {1:0.##}/{2:0.##} without, {3:0.##}/{4:0.##} with",
                    what, a.X, a.Y, b.X, b.Y));
            }

            Compare("the host size", plain.Size, withMenu.Size);

            UIControl plainBelow = FindByName(plain, "below")!;
            UIControl menuBelow = FindByName(withMenu, "below")!;
            Compare("the position of the control below the opener", plainBelow.Position, menuBelow.Position);

            Console.WriteLine(
                $"  host {plain.Size.X:0.##}x{plain.Size.Y:0.##} without menu, " +
                $"{withMenu.Size.X:0.##}x{withMenu.Size.Y:0.##} with");
        }

        private static UIControl? FindByName(UIControl root, string name)
        {
            foreach (UIControl control in LayoutSnapshot.Walk(root))
            {
                if (control.Name == name)
                    return control;
            }

            return null;
        }

        /// <summary>
        /// The whole point of the GUI scale work: laying out at scale S has to produce the same
        /// design S times larger. A dimension somebody forgot to scale shows up here as a size
        /// that grew by less than the scale factor.
        /// </summary>
        private static void CheckProportional(
            string scenarioName, double scale, PointD reference, PointD actual, List<string> failures)
        {
            CheckAxis("width", reference.X, actual.X);
            CheckAxis("height", reference.Y, actual.Y);

            void CheckAxis(string axis, double referenceValue, double actualValue)
            {
                double expected = referenceValue * scale;
                if (expected <= 0)
                    return;

                double deviation = Math.Abs(actualValue - expected) / expected;
                if (deviation <= ScaleTolerance)
                    return;

                failures.Add(string.Format(
                    CultureInfo.InvariantCulture,
                    "[{0} @ {1:0.##}x] {2} does not scale: expected about {3:0.#} ({4:0.#} x {1:0.##}), got {5:0.#} - off by {6:0.#}%",
                    scenarioName, scale, axis, expected, referenceValue, actualValue, deviation * 100));
            }
        }

        /// <summary>
        /// A control that ended up with no width or height is almost always a layout bug - it is
        /// what the collapsing text label looked like.
        /// </summary>
        private static void CheckNoZeroSizedControls(string scenarioName, UIControl root, List<string> failures)
        {
            foreach (UIControl control in LayoutSnapshot.Walk(root))
            {
                // The button decoration overlays legitimately measure to nothing before the
                // button stretches them over itself.
                if (control.Size.X > 0 && control.Size.Y > 0)
                    continue;

                failures.Add(
                    $"[{scenarioName}] control collapsed to zero: {LayoutSnapshot.Describe(control)}");
            }
        }

        /// <summary>
        /// In a stacking panel two siblings must not sit on top of each other. This is what the
        /// collapsed label looked like on screen: its text was drawn over the button next to it.
        /// </summary>
        private static void CheckNoSiblingOverlap(string scenarioName, UIControl root, List<string> failures)
        {
            foreach (UIControl control in LayoutSnapshot.Walk(root))
            {
                bool horizontal = control.InsideOrientation is Orientation.Left or Orientation.Right;
                bool vertical = control.InsideOrientation is Orientation.Top or Orientation.Bottom;

                if ((!horizontal && !vertical) || control.Children.Count < 2)
                    continue;

                // Composite controls deliberately stack their parts on top of each other.
                if (control.Parent is ButtonControl || control is ButtonControl)
                    continue;

                List<UIControl> ordered = horizontal
                    ? control.Children.OrderBy(c => c.Position.X).ToList()
                    : control.Children.OrderBy(c => c.Position.Y).ToList();

                for (int i = 0; i < ordered.Count - 1; i++)
                {
                    UIControl a = ordered[i];
                    UIControl b = ordered[i + 1];

                    double aEnd = horizontal ? a.Position.X + a.Size.X : a.Position.Y + a.Size.Y;
                    double bStart = horizontal ? b.Position.X : b.Position.Y;

                    if (aEnd > bStart + 0.001)
                    {
                        failures.Add(string.Format(
                            CultureInfo.InvariantCulture,
                            "[{0}] siblings overlap inside '{1}': '{2}' ends at {3:0.##} but '{4}' starts at {5:0.##}",
                            scenarioName,
                            string.IsNullOrEmpty(control.Name) ? control.GetType().Name : control.Name,
                            string.IsNullOrEmpty(a.Name) ? a.GetType().Name : a.Name,
                            aEnd,
                            string.IsNullOrEmpty(b.Name) ? b.GetType().Name : b.Name,
                            bStart));
                    }
                }
            }
        }
        #endregion

        #region Rendering
        private static void Render(UIControl root, string path)
        {
            int width = Math.Max(1, (int)Math.Ceiling(root.Size.X));
            int height = Math.Max(1, (int)Math.Ceiling(root.Size.Y));

            using (var surface = new ImageSurface(Format.Argb32, width, height))
            using (var ctx = new Context(surface))
            {
                ctx.Antialias = Antialias.Best;

                // Checkerboard, so that a control which draws nothing is visibly transparent
                // instead of silently blending into a flat background.
                DrawCheckerboard(ctx, width, height);

                root.GenerateRenderData(surface, ctx);

                surface.Flush();
                surface.WriteToPng(path);
            }

            Console.WriteLine($"  wrote {IOPath.GetFileName(path)} ({width}x{height})");
        }

        private static void DrawCheckerboard(Context ctx, int width, int height)
        {
            const int cell = 16;

            ctx.SetSourceRGB(0.85, 0.85, 0.85);
            ctx.Rectangle(0, 0, width, height);
            ctx.Fill();

            ctx.SetSourceRGB(0.75, 0.75, 0.75);
            for (int y = 0; y < height; y += cell)
            {
                for (int x = 0; x < width; x += cell)
                {
                    if (((x / cell) + (y / cell)) % 2 == 0)
                        continue;

                    ctx.Rectangle(x, y, cell, cell);
                    ctx.Fill();
                }
            }
        }
        #endregion
    }
}
