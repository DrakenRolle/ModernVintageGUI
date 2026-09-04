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
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

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
            CheckFocusOrder(failures);
            CheckRuntimeEditRedraws(failures);
            CheckClipping(failures);
            CheckScrolling(failures);
            CheckMaxSize(failures);
            CheckPixelCanvasPainting(failures);
            CheckPixelCanvasAreas(failures);

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

        /// <summary>
        /// The keyboard focus contract: which controls are in the tab order, in what order, and
        /// that moving through it is a proper cycle in both directions.
        ///
        /// Worth checking headlessly because the failure modes are silent in the game - a
        /// decoration that became focusable just makes Tab stop on an invisible rectangle, and a
        /// composite that ended up in the wrong place in the tree makes Tab jump around the
        /// dialog without anything looking broken.
        /// </summary>
        /// <summary>
        /// A stroke on a pixel canvas: the button that paints, the pixel under the cursor, and
        /// the line between two reports of the mouse.
        ///
        /// The last one is why this check exists. A mouse moving across a canvas reports a
        /// handful of positions a second, so a stroke that painted only where the mouse was seen
        /// would be a dotted line - and that is the kind of thing that looks like a slow computer
        /// rather than a bug, and never gets reported.
        /// </summary>
        private static void CheckPixelCanvasPainting(List<string> failures)
        {
            Console.WriteLine("### pixel canvas painting");
            Console.WriteLine("A drag paints every pixel between where the mouse was seen, and only in draw mode.");
            Console.WriteLine();

            var root = new RectangleControl(_Name: "root");
            root.InsideOrientation = Orientation.Top;
            root.Padding = 0;

            var canvas = new PixelCanvasControl(columns: 16, rows: 16, unscaledPixelSize: 10, _Name: "canvas")
            {
                DrawColor = new ElementColor(220, 60, 60, 255)
            };

            root.Children.Add(canvas);
            root.PerformLayout();

            var painted = new List<string>();
            canvas.PixelPainted += (sender, e) => painted.Add($"{e.X},{e.Y}" + (e.ByPlayer ? "" : "!"));

            // Draw mode is off, so this stroke has to do nothing at all.
            Stroke(canvas, 1, 1, 5, 1);

            if (painted.Count > 0)
            {
                failures.Add("[pixel canvas] painted with draw mode off: " + string.Join(" ", painted));
                Console.WriteLine("  PAINTED WITH DRAW MODE OFF");
                return;
            }

            Console.WriteLine("  draw mode off: nothing painted");

            canvas.DrawMode = true;

            // The left button when the right one paints: also nothing.
            Stroke(canvas, 1, 1, 5, 1, EnumMouseButton.Left);

            if (painted.Count > 0)
            {
                failures.Add("[pixel canvas] painted with the wrong button: " + string.Join(" ", painted));
                Console.WriteLine("  PAINTED WITH THE WRONG BUTTON");
                return;
            }

            Console.WriteLine("  the other button: nothing painted");

            // One press and one move, from a corner to a point that is neither on a row, a
            // column nor the diagonal - so nothing about the line is symmetric.
            Stroke(canvas, 2, 2, 13, 7);

            if (painted.Count == 0)
            {
                failures.Add("[pixel canvas] a stroke in draw mode painted nothing");
                Console.WriteLine("  NOTHING PAINTED");
                return;
            }

            Console.WriteLine($"  stroke from 2,2 to 13,7 painted {painted.Count} pixels");

            if (canvas.GetPixel(2, 2).R != 220 || canvas.GetPixel(13, 7).R != 220)
            {
                failures.Add("[pixel canvas] the stroke did not reach both of its ends");
                Console.WriteLine("  ENDS MISSING");
                return;
            }

            // Every step of the line has to touch the one before it. A gap means the line was
            // sampled rather than drawn.
            var cells = new List<(int X, int Y)>();

            for (int y = 0; y < canvas.Rows; y++)
            {
                for (int x = 0; x < canvas.Columns; x++)
                {
                    if (canvas.GetPixel(x, y).A > 0)
                        cells.Add((x, y));
                }
            }

            // 13 - 2 = 11 steps across, so a line that skips nothing has twelve pixels in it.
            if (cells.Count != 12)
            {
                failures.Add($"[pixel canvas] a line from 2,2 to 13,7 should be 12 pixels, got {cells.Count}");
                Console.WriteLine($"  WRONG LENGTH: {cells.Count}");
                return;
            }

            foreach ((int X, int Y) cell in cells)
            {
                bool touches = cells.Any(other =>
                    (other.X != cell.X || other.Y != cell.Y)
                    && Math.Abs(other.X - cell.X) <= 1
                    && Math.Abs(other.Y - cell.Y) <= 1);

                if (touches)
                    continue;

                failures.Add($"[pixel canvas] the pixel at {cell.X},{cell.Y} stands alone - the line has a gap");
                Console.WriteLine($"  GAP AT {cell.X},{cell.Y}");
                return;
            }

            Console.WriteLine("  every pixel of the line touches the next, no gaps");

            // And what a player painted is reported as theirs, so a mod can tell it from what it
            // set itself and not send the whole canvas back to the server.
            if (painted.Any(entry => entry.EndsWith("!")))
            {
                failures.Add("[pixel canvas] a pixel the player painted was reported as set from code");
                Console.WriteLine("  WRONG SOURCE REPORTED");
                return;
            }

            Console.WriteLine("  all of them reported as painted by the player");
        }

        /// <summary>
        /// Areas on a pixel canvas: what hangs together, what an area lookup gives back, and
        /// what the outline refuses to draw.
        /// </summary>
        private static void CheckPixelCanvasAreas(List<string> failures)
        {
            Console.WriteLine("### pixel canvas areas");
            Console.WriteLine("Areas hang together edge to edge, and an outline is only drawn around one that does.");
            Console.WriteLine();

            var red = new ElementColor(220, 60, 60, 255);
            var blue = new ElementColor(60, 120, 220, 255);

            var canvas = new PixelCanvasControl(columns: 12, rows: 12, _Name: "canvas");

            // A red line of five, a red pixel on its own further along, and a blue block that
            // touches the line - so colour blind and colour sensitive have to disagree.
            for (int x = 2; x <= 6; x++)
            {
                canvas.SetPixel(x, 4, red);
            }

            canvas.SetPixel(9, 4, red);

            canvas.SetPixel(6, 5, blue);
            canvas.SetPixel(6, 6, blue);

            // Connectivity, on sets rather than on the canvas.
            var straight = new[] { new Vec2i(1, 1), new Vec2i(2, 1), new Vec2i(3, 1) };
            var diagonal = new[] { new Vec2i(1, 1), new Vec2i(2, 2) };
            var apart = new[] { new Vec2i(1, 1), new Vec2i(1, 2), new Vec2i(5, 5) };

            Check(failures, PixelCanvasControl.AreConnected(straight), "three in a row hang together");
            Check(failures, !PixelCanvasControl.AreConnected(diagonal), "two on a diagonal do not");
            Check(failures, !PixelCanvasControl.AreConnected(apart), "and neither does one off on its own");
            Check(failures, PixelCanvasControl.AreConnected(new[] { new Vec2i(3, 3) }), "a single pixel is an area");
            Check(failures, !PixelCanvasControl.AreConnected(Array.Empty<Vec2i>()), "nothing is not an area");

            // Pointing at one pixel of the line gives back the line - and only the line.
            Vec2i[] line = canvas.GetArea(4, 4);

            Check(failures, line.Length == 5, $"pointing at the red line gives back its five pixels, got {line.Length}");
            Check(failures, line.All(p => p.Y == 4 && p.X >= 2 && p.X <= 6), "all of them on the line");
            Check(failures, !line.Any(p => p.X == 9), "and not the loose red pixel that does not touch it");

            // Colour blind, the same point picks up the blue block hanging off the line.
            Vec2i[] blob = canvas.GetArea(4, 4, colorSensitive: false);

            Check(failures, blob.Length == 7, $"colour blind, the blue block comes along, got {blob.Length}");

            // An outline is only drawn around something that hangs together.
            Check(failures, canvas.SetHighlight(line), "an area can be outlined");
            Check(failures, canvas.HasHighlight, "and the canvas says so");

            Check(failures, !canvas.SetHighlight(apart.ToList()), "a set with a gap in it is refused");
            Check(failures, canvas.HighlightedArea.Length == 5, "and the refused one did not replace the old outline");

            // Colour sensitive outlining refuses an area of two colours.
            Check(failures, !canvas.SetHighlight(blob, colorSensitive: true), "two colours are refused when colour matters");
            Check(failures, canvas.SetHighlight(blob), "and taken when it does not");

            canvas.ClearHighlight();
            Check(failures, !canvas.HasHighlight, "and it can be cleared again");
        }

        private static void Check(List<string> failures, bool condition, string what)
        {
            Console.WriteLine((condition ? "  ok   " : "  FAIL ") + what);

            if (!condition)
            {
                failures.Add("[pixel canvas] " + what);
            }
        }

        /// <summary>
        /// Presses on one pixel, moves to another in one jump, and lets go - the way the mouse
        /// arrives when it is moved quickly.
        /// </summary>
        private static void Stroke(
            PixelCanvasControl canvas,
            int fromX,
            int fromY,
            int toX,
            int toY,
            EnumMouseButton button = EnumMouseButton.Right)
        {
            canvas.InvokeEventMouseDown(At(canvas, fromX, fromY, button));
            canvas.InvokeEventMouseMove(At(canvas, toX, toY, button));
            canvas.InvokeEventMouseUp(At(canvas, toX, toY, button));
        }

        /// <summary>The middle of a canvas pixel, as a mouse event.</summary>
        private static MouseEvent At(PixelCanvasControl canvas, int x, int y, EnumMouseButton button)
        {
            LayoutRect box = canvas.ContentBox();
            double pixel = canvas.PixelSize;

            return new MouseEvent(
                (int)(box.X + (x + 0.5) * pixel),
                (int)(box.Y + (y + 0.5) * pixel),
                button,
                0);
        }

        private static void CheckFocusOrder(List<string> failures)
        {
            Console.WriteLine("### focus order");
            Console.WriteLine("Tab has to walk the interactive controls in reading order, and only those.");
            Console.WriteLine();

            var root = new RectangleControl(_Name: "root");
            root.InsideOrientation = Orientation.Top;
            root.Padding = 10;

            var first = new ButtonControl(_Name: "first");
            first.Text = "First";
            root.Children.Add(first);

            // Not interactive, so it must not appear in the tab order.
            root.Children.Add(new TextLabelControl("Just a caption", _Name: "caption"));

            var row = new RectangleControl(_Name: "row");
            row.InsideOrientation = Orientation.Left;
            foreach (string caption in new[] { "left", "middle", "right" })
            {
                var button = new ButtonControl(_Name: caption);
                button.Text = caption;
                row.Children.Add(button);
            }
            root.Children.Add(row);

            var last = new ButtonControl(_Name: "last");
            last.Text = "Last";
            root.Children.Add(last);

            root.PerformLayout();

            List<UIControl> order = UIControl.FocusableControls(root).ToList();

            string[] expected = { "first", "left", "middle", "right", "last" };
            string[] actual = order.Select(c => c.Name).ToArray();

            if (!actual.SequenceEqual(expected))
            {
                failures.Add(
                    "[focus order] expected " + string.Join(", ", expected) +
                    " but got " + (actual.Length == 0 ? "nothing" : string.Join(", ", actual)));

                Console.WriteLine("  WRONG TAB ORDER: " + string.Join(", ", actual));
                return;
            }

            Console.WriteLine("  tab order: " + string.Join(" -> ", actual));

            // Reading order: each control starts at or below its predecessor, and on the same
            // line it starts at or to the right of it. This is what ties the tab order to what
            // the player actually sees, rather than only to the shape of the tree.
            for (int i = 0; i < order.Count - 1; i++)
            {
                UIControl a = order[i];
                UIControl b = order[i + 1];

                bool sameLine = Math.Abs(a.Position.Y - b.Position.Y) < 1.0;
                bool inOrder = sameLine ? b.Position.X >= a.Position.X - 0.001 : b.Position.Y > a.Position.Y;

                if (inOrder)
                    continue;

                failures.Add(string.Format(
                    CultureInfo.InvariantCulture,
                    "[focus order] '{0}' at {1:0.##}/{2:0.##} is tabbed to before '{3}' at {4:0.##}/{5:0.##}, " +
                    "which is not reading order",
                    a.Name, a.Position.X, a.Position.Y, b.Name, b.Position.X, b.Position.Y));
            }

            // Tab from every control has to land on the next one and wrap at the end, and
            // Shift+Tab has to undo exactly that. A broken wrap traps the focus at one end.
            for (int i = 0; i < order.Count; i++)
            {
                UIControl current = order[i];
                UIControl expectedNext = order[(i + 1) % order.Count];
                UIControl expectedPrevious = order[(i - 1 + order.Count) % order.Count];

                UIControl? next = UIControl.NextFocusable(root, current, backwards: false);
                UIControl? previous = UIControl.NextFocusable(root, current, backwards: true);

                if (!ReferenceEquals(next, expectedNext))
                {
                    failures.Add(
                        $"[focus order] Tab from '{current.Name}' should reach '{expectedNext.Name}' " +
                        $"but reached '{next?.Name ?? "nothing"}'");
                }

                if (!ReferenceEquals(previous, expectedPrevious))
                {
                    failures.Add(
                        $"[focus order] Shift+Tab from '{current.Name}' should reach '{expectedPrevious.Name}' " +
                        $"but reached '{previous?.Name ?? "nothing"}'");
                }
            }

            // Nothing focused yet: the first Tab has to enter the dialog at one end or the other
            // rather than doing nothing, otherwise a dialog is unreachable by keyboard.
            UIControl? entry = UIControl.NextFocusable(root, null, backwards: false);
            UIControl? backwardsEntry = UIControl.NextFocusable(root, null, backwards: true);

            if (!ReferenceEquals(entry, order[0]))
                failures.Add($"[focus order] the first Tab should enter at '{order[0].Name}', got '{entry?.Name ?? "nothing"}'");

            if (!ReferenceEquals(backwardsEntry, order[order.Count - 1]))
                failures.Add($"[focus order] the first Shift+Tab should enter at '{order[order.Count - 1].Name}', got '{backwardsEntry?.Name ?? "nothing"}'");

            Console.WriteLine($"  {order.Count} focusable controls, cycle closes in both directions");
        }

        /// <summary>
        /// Editing a tree that is already on screen has to give the same picture as having built
        /// it that way in the first place.
        ///
        /// This is the drawing half of what `CheckSurvivesScaleChange` does for the layout, and
        /// it is the half that matters for the deferred redraw: `Refresh()` no longer draws, it
        /// marks the dialog dirty and the renderer rebuilds the surface once at the start of the
        /// next frame. Comparing pixels rather than the layout snapshot catches a control that
        /// lays out correctly but keeps drawing from something it cached earlier.
        ///
        /// What this cannot cover is the renderer itself - the dirty flag being flushed per
        /// frame and the GL upload need the game.
        /// </summary>
        private static void CheckRuntimeEditRedraws(List<string> failures)
        {
            Console.WriteLine("### runtime edit");
            Console.WriteLine("Editing a live tree must draw the same as building it that way.");
            Console.WriteLine();

            RectangleControl BuildRow(int buttonCount)
            {
                var root = new RectangleControl(
                    backgroundColor: new ElementColor(0.20, 0.16, 0.13, 1.0),
                    _Name: "root");
                root.InsideOrientation = Orientation.Top;
                root.Padding = 10;

                for (int i = 0; i < buttonCount; i++)
                {
                    var button = new ButtonControl(_Name: "b" + i);
                    button.Text = "Button " + i;
                    root.Children.Add(button);
                }

                return root;
            }

            // The tree that is already open: built with two, then edited to three.
            RectangleControl edited = BuildRow(2);
            edited.PerformLayout();
            byte[] beforeEdit = RenderToBytes(edited);

            var added = new ButtonControl(_Name: "b2");
            added.Text = "Button 2";
            edited.Children.Add(added);
            edited.PerformLayout();
            byte[] afterEdit = RenderToBytes(edited);

            // Built with three from the start.
            RectangleControl fresh = BuildRow(3);
            fresh.PerformLayout();
            byte[] freshBytes = RenderToBytes(fresh);

            if (beforeEdit.Length == afterEdit.Length && beforeEdit.AsSpan().SequenceEqual(afterEdit))
            {
                failures.Add("[runtime edit] adding a child did not change the drawing at all");
                Console.WriteLine("  ADDING A CHILD CHANGED NOTHING");
                return;
            }

            if (afterEdit.Length != freshBytes.Length)
            {
                failures.Add(string.Format(
                    CultureInfo.InvariantCulture,
                    "[runtime edit] the edited tree renders {0:0.##}x{1:0.##}, a freshly built one {2:0.##}x{3:0.##}",
                    edited.Size.X, edited.Size.Y, fresh.Size.X, fresh.Size.Y));

                Console.WriteLine("  EDITED AND FRESH TREE HAVE DIFFERENT SIZES");
                return;
            }

            int differing = 0;
            for (int i = 0; i < afterEdit.Length; i++)
            {
                if (afterEdit[i] != freshBytes[i])
                    differing++;
            }

            if (differing > 0)
            {
                failures.Add(string.Format(
                    CultureInfo.InvariantCulture,
                    "[runtime edit] the edited tree draws differently from a freshly built one: " +
                    "{0} of {1} bytes differ", differing, afterEdit.Length));

                Console.WriteLine($"  EDITED AND FRESH TREE DRAW DIFFERENTLY ({differing} bytes)");
                return;
            }

            Console.WriteLine(
                $"  2 -> 3 buttons: {edited.Size.X:0.##}x{edited.Size.Y:0.##}, " +
                "pixel identical to a freshly built tree");
        }

        /// <summary>
        /// ClipsChildren has to do three things, and this checks all three against the same tree
        /// with the flag off:
        ///
        /// 1. Nothing is painted outside the container's content box.
        /// 2. The child keeps its natural size instead of being shrunk by the overflow check -
        ///    that is what makes content larger than its container possible at all.
        /// 3. A point outside the content box does not hit the child any more, so what was cut
        ///    away is not clickable either.
        /// </summary>
        private static void CheckClipping(List<string> failures)
        {
            Console.WriteLine("### clipping");
            Console.WriteLine("ClipsChildren must cut the drawing, keep child sizes, and cut hit testing.");
            Console.WriteLine();

            // Overflow happens on the stacking axis. Across it the children are normalized to the
            // container width anyway, which is what a list wants, so there is nothing to cut.
            const int RowCount = 6;

            RectangleControl Build(bool clips, int rowCount, out RectangleControl box)
            {
                var root = new RectangleControl(_Name: "root");
                root.InsideOrientation = Orientation.None;
                root.Padding = 0;
                root.Size = new PointD(260, 260);
                root.IsAutoSize = false;

                box = new RectangleControl(_Padding: 8, _Name: "box");
                box.InsideOrientation = Orientation.Top;
                box.Size = new PointD(220, 100);
                box.IsAutoSize = false;
                box.ClipsChildren = clips;

                for (int i = 0; i < rowCount; i++)
                {
                    var row = new ButtonControl(_Name: "row" + i);
                    row.Text = "Row " + i;
                    box.Children.Add(row);
                }

                root.Children.Add(box);
                return root;
            }

            RectangleControl openRoot = Build(false, RowCount, out RectangleControl openBox);
            RectangleControl clipRoot = Build(true, RowCount, out RectangleControl clipBox);
            RectangleControl emptyRoot = Build(true, 0, out RectangleControl emptyBox);

            openRoot.PerformLayout();
            clipRoot.PerformLayout();
            emptyRoot.PerformLayout();

            double cut = clipBox.ContentBox().Bottom;

            // 1. Nothing of the children is painted below the content box. Compared against the
            //    same container with no children at all rather than against zero, because the
            //    container draws its own box down there and clipping does not touch that.
            int inkOpen = CountInkBelow(openRoot, cut);
            int inkClipped = CountInkBelow(clipRoot, cut);
            int inkEmpty = CountInkBelow(emptyRoot, emptyBox.ContentBox().Bottom);

            if (inkOpen <= inkEmpty)
            {
                failures.Add("[clipping] the unclipped tree did not overflow, so the check proves nothing");
                Console.WriteLine("  THE TEST TREE DOES NOT OVERFLOW");
                return;
            }

            if (inkClipped != inkEmpty)
            {
                failures.Add(
                    $"[clipping] children painted below the content box despite ClipsChildren: " +
                    $"{inkClipped} pixels where an empty container has {inkEmpty} (unclipped: {inkOpen})");
                Console.WriteLine($"  CLIP LEAKED: {inkClipped - inkEmpty} pixels of children below the cut");
            }
            else
            {
                Console.WriteLine(
                    $"  ink below the cut: {inkOpen} unclipped, {inkClipped} clipped, {inkEmpty} with no children");
            }

            // 2. The rows keep their natural height instead of being squashed to fit. This is
            //    what makes content taller than its container possible, and therefore scrolling.
            var openLast = (ButtonControl)openBox.Children[RowCount - 1];
            var clipLast = (ButtonControl)clipBox.Children[RowCount - 1];

            if (clipLast.Size.Y <= openLast.Size.Y)
            {
                failures.Add(string.Format(
                    CultureInfo.InvariantCulture,
                    "[clipping] the last row was still shrunk: {0:0.##} high inside a clipping " +
                    "container, {1:0.##} without one - it should keep its full height",
                    clipLast.Size.Y, openLast.Size.Y));
            }
            else
            {
                Console.WriteLine(
                    $"  last row height: {openLast.Size.Y:0.##} squashed to fit, {clipLast.Size.Y:0.##} kept natural");
            }

            // 3. What was cut away is not clickable either.
            double probeX = clipBox.Position.X + clipBox.Size.X / 2;
            double probeY = cut + 4;

            UIControl? hitClipped = HitAt(clipRoot, probeX, probeY);

            if (hitClipped is ButtonControl)
            {
                failures.Add(
                    $"[clipping] a point below the content box still hit '{hitClipped.Name}'");
            }
            else
            {
                Console.WriteLine(
                    $"  hit below the cut: '{hitClipped?.Name ?? "nothing"}' - no row");
            }
        }

        /// <summary>
        /// The scrolling contract: a bar appears only when it is needed, it costs the viewport
        /// exactly its own width, the reachable range is content minus viewport, scrolling moves
        /// the content by exactly what was asked, and the position survives a GUI scale change.
        ///
        /// That last one is the reason the offset is stored in author units. The content grows
        /// with the scale, so an offset kept in device pixels would point at a different row
        /// after the player moves the slider and the list would jump.
        /// </summary>
        private static void CheckScrolling(List<string> failures)
        {
            Console.WriteLine("### scrolling");
            Console.WriteLine("Bars appear on demand, cost their own width, and survive a scale change.");
            Console.WriteLine();

            RectangleControl BuildList(int rowCount, out RectangleControl list)
            {
                var root = new RectangleControl(_Name: "root");
                root.InsideOrientation = Orientation.Top;
                root.Padding = 0;

                list = new RectangleControl(_Padding: 6, _Name: "list");
                list.InsideOrientation = Orientation.Top;
                list.Size = new PointD(220, 140);
                list.IsAutoSize = false;
                list.EnableVerticalScrollbar = true;

                for (int i = 0; i < rowCount; i++)
                {
                    var row = new ButtonControl(_Name: "row" + i);
                    row.Text = "Row " + i;
                    list.Children.Add(row);
                }

                root.Children.Add(list);
                return root;
            }

            // 1. No overflow, no bar - and the viewport is then the full padding box.
            RectangleControl fitsRoot = BuildList(2, out RectangleControl fits);
            fitsRoot.PerformLayout();

            if (fits.MaxScrollOffset.Y > 0.001)
            {
                failures.Add(string.Format(CultureInfo.InvariantCulture,
                    "[scrolling] content that fits still reports {0:0.##} of scroll range",
                    fits.MaxScrollOffset.Y));
            }

            RectangleControl overflowRoot = BuildList(8, out RectangleControl list8);
            overflowRoot.PerformLayout();

            double barThickness = ScrollbarStyle.UnscaledWidth * list8.LayoutScale;
            double expectedViewportWidth = fits.ViewportSize.X - barThickness;

            if (Math.Abs(list8.ViewportSize.X - expectedViewportWidth) > 0.001)
            {
                failures.Add(string.Format(CultureInfo.InvariantCulture,
                    "[scrolling] the bar should cost the viewport {0:0.##} of width: {1:0.##} without " +
                    "a bar, {2:0.##} with, expected {3:0.##}",
                    barThickness, fits.ViewportSize.X, list8.ViewportSize.X, expectedViewportWidth));
            }
            else
            {
                Console.WriteLine(
                    $"  viewport width {fits.ViewportSize.X:0.##} without a bar, " +
                    $"{list8.ViewportSize.X:0.##} with (bar is {barThickness:0.##})");
            }

            // 2. Reachable range is content minus viewport.
            double expectedMax = list8.ContentSize.Y - list8.ViewportSize.Y;
            if (Math.Abs(list8.MaxScrollOffset.Y - expectedMax) > 0.001)
            {
                failures.Add(string.Format(CultureInfo.InvariantCulture,
                    "[scrolling] scroll range is {0:0.##} but content {1:0.##} minus viewport {2:0.##} is {3:0.##}",
                    list8.MaxScrollOffset.Y, list8.ContentSize.Y, list8.ViewportSize.Y, expectedMax));
            }

            // 3. Scrolling moves the content by exactly the offset.
            UIControl firstRow = list8.Children[0];
            double beforeY = firstRow.Position.Y;

            const double Step = 40;
            list8.ScrollTo(0, Step);
            overflowRoot.PerformLayout();

            double movedBy = beforeY - firstRow.Position.Y;
            if (Math.Abs(movedBy - Step) > 0.001)
            {
                failures.Add(string.Format(CultureInfo.InvariantCulture,
                    "[scrolling] scrolling by {0:0.##} moved the content by {1:0.##}", Step, movedBy));
            }
            else
            {
                Console.WriteLine($"  scrolling by {Step:0.##} moved the first row up by {movedBy:0.##}");
            }

            // 4. Both ends clamp.
            list8.ScrollTo(0, -500);
            overflowRoot.PerformLayout();
            if (list8.ScrollOffset.Y != 0)
            {
                failures.Add(string.Format(CultureInfo.InvariantCulture,
                    "[scrolling] scrolling past the top left the offset at {0:0.##}", list8.ScrollOffset.Y));
            }

            list8.ScrollTo(0, 99999);
            overflowRoot.PerformLayout();
            if (Math.Abs(list8.ScrollOffset.Y - list8.MaxScrollOffset.Y) > 0.001)
            {
                failures.Add(string.Format(CultureInfo.InvariantCulture,
                    "[scrolling] scrolling past the bottom left the offset at {0:0.##}, range ends at {1:0.##}",
                    list8.ScrollOffset.Y, list8.MaxScrollOffset.Y));
            }
            else
            {
                Console.WriteLine($"  clamped at both ends, range 0 to {list8.MaxScrollOffset.Y:0.##}");
            }

            // 5. The same place in the content after a scale change. Scroll to the middle at 1x,
            //    then lay out at 2x: the offset has to double along with everything else.
            RectangleControl scaleRoot = BuildList(8, out RectangleControl scaleList);
            scaleRoot.PerformLayout();

            double halfway = scaleList.MaxScrollOffset.Y / 2;
            scaleList.ScrollTo(0, halfway);
            scaleRoot.PerformLayout();

            double fractionAt1x = scaleList.ScrollOffset.Y / scaleList.MaxScrollOffset.Y;

            scaleRoot.LayoutScale = 2.0;
            scaleRoot.PerformLayout();

            double fractionAt2x = scaleList.ScrollOffset.Y / scaleList.MaxScrollOffset.Y;

            if (Math.Abs(fractionAt1x - fractionAt2x) > 0.02)
            {
                failures.Add(string.Format(CultureInfo.InvariantCulture,
                    "[scrolling] the scroll position moved when the GUI scale changed: {0:0.###} of the " +
                    "way down at 1x, {1:0.###} at 2x", fractionAt1x, fractionAt2x));
            }
            else
            {
                Console.WriteLine(
                    $"  scale change keeps the position: {fractionAt1x:0.###} of the way down at 1x, " +
                    $"{fractionAt2x:0.###} at 2x");
            }
        }

        /// <summary>
        /// MaxSize caps an auto sizing control - on the stacking axis, where the control decides
        /// its own size, and across it, where the parent stretches it and could walk over the
        /// cap without noticing. The second one is the interesting half: it is the failure that
        /// would show up later as "MaxSize works sometimes".
        ///
        /// And it scales, like every other authored dimension.
        /// </summary>
        private static void CheckMaxSize(List<string> failures)
        {
            Console.WriteLine("### max size");
            Console.WriteLine("A cap has to hold on both axes, and to scale with the GUI.");
            Console.WriteLine();

            const double CapHeight = 90;
            const double CapWidth = 120;

            RectangleControl Build(bool capped, out RectangleControl box, double scale)
            {
                var root = new RectangleControl(_Name: "root");
                root.InsideOrientation = Orientation.Top;
                root.Padding = 10;
                root.LayoutScale = scale;

                // Wide enough that the parent would stretch an uncapped child well past the cap.
                root.Size = new PointD(400, 400);
                root.IsAutoSize = false;

                box = new RectangleControl(_Padding: 4, _Name: "box");
                box.InsideOrientation = Orientation.Top;
                box.ClipsChildren = true;

                if (capped)
                    box.MaxSize = new PointD(CapWidth, CapHeight);

                for (int i = 0; i < 6; i++)
                {
                    var row = new ButtonControl(_Name: "row" + i);
                    row.Text = "Row " + i;
                    box.Children.Add(row);
                }

                root.Children.Add(box);
                root.PerformLayout();
                return root;
            }

            Build(false, out RectangleControl open, 1.0);
            Build(true, out RectangleControl capped, 1.0);

            // 1. The stacking axis: the content is far taller than the cap.
            if (open.Size.Y <= CapHeight)
            {
                failures.Add("[max size] the uncapped control was already inside the cap, so the check proves nothing");
                Console.WriteLine("  THE TEST TREE DOES NOT EXCEED THE CAP");
                return;
            }

            if (capped.Size.Y > CapHeight + 0.001)
            {
                failures.Add(string.Format(CultureInfo.InvariantCulture,
                    "[max size] height cap of {0:0.##} not honoured: {1:0.##} high (uncapped {2:0.##})",
                    CapHeight, capped.Size.Y, open.Size.Y));
            }
            else
            {
                Console.WriteLine(
                    $"  height: {open.Size.Y:0.##} uncapped, {capped.Size.Y:0.##} capped at {CapHeight:0.##}");
            }

            // 2. Across the stacking axis, where the parent does the sizing. This is the half
            //    the normalization pass used to overwrite.
            if (capped.Size.X > CapWidth + 0.001)
            {
                failures.Add(string.Format(CultureInfo.InvariantCulture,
                    "[max size] width cap of {0:0.##} was overwritten by the parent stretching the " +
                    "child: {1:0.##} wide (uncapped {2:0.##})",
                    CapWidth, capped.Size.X, open.Size.X));
            }
            else
            {
                Console.WriteLine(
                    $"  width: {open.Size.X:0.##} stretched by the parent, {capped.Size.X:0.##} capped at {CapWidth:0.##}");
            }

            // 3. It is an authored dimension, so it doubles at 2x like everything else.
            Build(true, out RectangleControl capped2x, 2.0);

            if (Math.Abs(capped2x.Size.Y - CapHeight * 2) > 1.0)
            {
                failures.Add(string.Format(CultureInfo.InvariantCulture,
                    "[max size] the cap did not scale: {0:0.##} high at 2x, expected about {1:0.##}",
                    capped2x.Size.Y, CapHeight * 2));
            }
            else
            {
                Console.WriteLine($"  cap scales: {capped.Size.Y:0.##} at 1x, {capped2x.Size.Y:0.##} at 2x");
            }
        }

        /// <summary>
        /// Walks the tree the way the dialog's hit test does. HitTest itself is protected and
        /// converts from screen coordinates, which a harness tree has no notion of.
        /// </summary>
        private static UIControl? HitAt(UIControl root, double x, double y)
        {
            if (!root.ContainsLocalPoint(x, y))
                return null;

            if (root.ClipsChildren && !root.ContentBox().Contains(x, y))
                return root;

            for (int i = root.Children.Count - 1; i >= 0; i--)
            {
                UIControl? hit = HitAt(root.Children[i], x, y);
                if (hit != null)
                    return hit;
            }

            return root;
        }

        /// <summary>Counts pixels with any opacity below a given y.</summary>
        private static int CountInkBelow(UIControl root, double y)
        {
            int width = Math.Max(1, (int)Math.Ceiling(root.Size.X));
            int height = Math.Max(1, (int)Math.Ceiling(root.Size.Y));

            using (var surface = new ImageSurface(Format.Argb32, width, height))
            using (var ctx = new Context(surface))
            {
                ctx.Antialias = Antialias.Best;
                root.GenerateRenderData(surface, ctx);
                surface.Flush();

                byte[] data = surface.Data;
                int stride = surface.Stride;
                int firstRow = (int)Math.Ceiling(y) + 1;
                int count = 0;

                for (int row = firstRow; row < height; row++)
                {
                    for (int column = 0; column < width; column++)
                    {
                        // Argb32 is BGRA in memory on a little endian machine, so alpha is last.
                        if (data[row * stride + column * 4 + 3] != 0)
                            count++;
                    }
                }

                return count;
            }
        }

        /// <summary>Lays the tree out onto its own surface and returns the raw pixels.</summary>
        private static byte[] RenderToBytes(UIControl root)
        {
            int width = Math.Max(1, (int)Math.Ceiling(root.Size.X));
            int height = Math.Max(1, (int)Math.Ceiling(root.Size.Y));

            using (var surface = new ImageSurface(Format.Argb32, width, height))
            using (var ctx = new Context(surface))
            {
                ctx.Antialias = Antialias.Best;
                root.GenerateRenderData(surface, ctx);
                surface.Flush();

                return (byte[])surface.Data.Clone();
            }
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

                // A context menu anchor is supposed to be zero sized: it hangs in the host tree
                // only to be given a position, and its menu lives in a popup of its own. That it
                // costs no space is checked properly by CheckContextMenuAnchorIsFree.
                if (control is ContextMenuControl)
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
