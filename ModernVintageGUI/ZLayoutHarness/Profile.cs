using Cairo;
using IS2Mod.ControlTypes;
using IS2Mod.Diagnostics;
using IS2Mod.Enums;
using System;
using System.Diagnostics;
using Vintagestory.API.Client;

namespace LayoutHarness
{
    /// <summary>
    /// Times what a dialog does, on the tree the mod actually opens.
    ///
    /// The two things a dialog does between frames are laid out here separately, because they
    /// are triggered by different events and only one of them is supposed to be cheap:
    ///
    ///  * **The layout** runs on every <c>RecomposeToMain</c> - which is every hover highlight,
    ///    every selection, every added control. Nothing about a hover changes any size, so this
    ///    is pure overhead on the most common interaction there is.
    ///  * **The drawing** runs once per changed frame: a surface the size of the dialog is
    ///    allocated, every control paints itself onto it, and the result is uploaded to the GPU.
    ///
    /// What cannot be measured here is the third one, the per frame interactive pass that draws
    /// item stacks - it needs the game's item atlas and its render API. Its cost is a walk of
    /// the tree plus one RenderItemstackToGui per visible stack, and it happens on *every*
    /// frame whether anything changed or not.
    /// </summary>
    internal static class Profile
    {
        public static int Run(int passes)
        {
            Console.WriteLine("ModernVintageGUI profile");
            Console.WriteLine(new string('=', 78));
            Console.WriteLine();
            Console.WriteLine("The showcase tree - the dialog the test hotkey opens - at GUI scale 1.");
            Console.WriteLine(passes + " passes each.");
            Console.WriteLine();

            RectangleControl root = BuildShowcase();

            // Warm up: first call JITs the layout and fills the font cache, which would
            // otherwise land entirely in the first measured pass.
            root.PerformLayout();
            Render(root);

            Console.WriteLine("tree: " + Count(root) + " controls, " + Depth(root) + " levels deep");
            Console.WriteLine();

            MeasureLayout(root, passes);
            MeasureDraw(root, passes);
            MeasureWithoutEmboss(root, passes);
            MeasureAtScale(root, passes, 2.0);
            MeasureBlurAgainstSurfaceSize(passes);

            return 0;
        }

        /// <summary>
        /// One button, blurred on a small surface and then on a large one.
        ///
        /// The question this answers is why a button costs what it does: the blur is asked to
        /// cover the button's rectangle, so its cost should follow the *button*. If it follows
        /// the surface the button happens to sit on instead, then every button in a dialog pays
        /// for the size of the dialog, and the fix is to blur somewhere small rather than to
        /// blur less.
        /// </summary>
        private static void MeasureBlurAgainstSurfaceSize(int passes)
        {
            Console.WriteLine("### One button, same size, on surfaces of different sizes");
            Console.WriteLine();

            foreach (int side in new[] { 200, 600, 1200, 2400 })
            {
                var host = new RectangleControl(_Name: "host")
                {
                    InsideOrientation = Orientation.Top,
                    Size = new PointD(side, side),
                    IsAutoSize = false
                };

                // A fixed size, so the only thing that changes between the runs is the surface
                // the button sits on. Stretching it to the container would measure the button.
                var button = new ButtonControl(_Name: "button")
                {
                    Text = "Save",
                    Size = new PointD(160, 40),
                    IsAutoSize = false,
                    MaxSize = new PointD(160, 40)
                };

                host.Children.Add(button);
                host.PerformLayout();

                Render(host);

                var watch = Stopwatch.StartNew();

                for (int i = 0; i < passes; i++)
                {
                    Render(host);
                }

                watch.Stop();

                Console.WriteLine("  surface " + side + "x" + side
                                + " with one " + (int)button.Size.X + "x" + (int)button.Size.Y
                                + " button: " + Per(watch, passes) + " ms per redraw");
            }

            Console.WriteLine();
        }

        /// <summary>
        /// The same dialog at another GUI scale.
        ///
        /// Worth a line of its own because the drawing does not scale with the number of
        /// controls, it scales with the *area* of the dialog: the surface, the blurs and every
        /// filled rectangle are per pixel, so twice the scale is four times the pixels. A player
        /// on a large GUI scale is on a different curve than the one this was developed at.
        /// </summary>
        private static void MeasureAtScale(RectangleControl root, int passes, double scale)
        {
            root.LayoutScale = scale;
            root.PerformLayout();
            Render(root);

            var layout = Stopwatch.StartNew();

            for (int i = 0; i < passes; i++)
            {
                root.PerformLayout();
            }

            layout.Stop();

            var draw = Stopwatch.StartNew();

            for (int i = 0; i < passes; i++)
            {
                Render(root);
            }

            draw.Stop();

            Console.WriteLine("### At GUI scale " + scale + " ("
                            + (int)root.Size.X + "x" + (int)root.Size.Y + " px): layout "
                            + Per(layout, passes) + " ms, redraw " + Per(draw, passes) + " ms per pass");
            Console.WriteLine();

            root.LayoutScale = 1.0;
            root.PerformLayout();
        }

        /// <summary>
        /// The same redraw with the button emboss switched off, because that one switch decides
        /// most of it: the emboss is a blurred border, the blur is a CPU pass over every pixel of
        /// the button, and it is redone on every redraw of the dialog.
        ///
        /// Printed as a matter of course rather than left to be discovered, so that a panel
        /// packed with buttons has a number to weigh its looks against.
        /// </summary>
        private static void MeasureWithoutEmboss(RectangleControl root, int passes)
        {
            var buttons = new System.Collections.Generic.List<ButtonControl>();

            CollectButtons(root, buttons);

            if (buttons.Count == 0)
                return;

            foreach (ButtonControl button in buttons)
            {
                button.ShowEmboss = false;
            }

            root.PerformLayout();
            Render(root);

            var watch = Stopwatch.StartNew();

            for (int i = 0; i < passes; i++)
            {
                Render(root);
            }

            watch.Stop();

            foreach (ButtonControl button in buttons)
            {
                button.ShowEmboss = true;
            }

            root.PerformLayout();

            Console.WriteLine("### Redraw with ShowEmboss = false on all " + buttons.Count + " buttons: "
                            + Per(watch, passes) + " ms per pass");
            Console.WriteLine();
        }

        private static void CollectButtons(UIControl control, System.Collections.Generic.List<ButtonControl> into)
        {
            if (control is ButtonControl button)
            {
                into.Add(button);
            }

            foreach (UIControl child in control.Children)
            {
                CollectButtons(child, into);
            }
        }

        /// <summary>
        /// A full layout pass - what one hover highlight costs today, because every redraw
        /// request goes through <c>RecomposeToMain</c> and that lays the dialog out again.
        /// </summary>
        private static void MeasureLayout(RectangleControl root, int passes)
        {
            UIProfiler.Reset();
            UIProfiler.Enabled = true;

            var watch = Stopwatch.StartNew();

            for (int i = 0; i < passes; i++)
            {
                UIProfiler.CountPass();
                root.PerformLayout();
            }

            watch.Stop();
            UIProfiler.Enabled = false;

            Console.WriteLine("### PerformLayout: " + Per(watch, passes) + " ms per pass");
            Console.WriteLine();
            Console.WriteLine(UIProfiler.Report("layout"));
        }

        /// <summary>
        /// One redraw: the surface allocation, the whole tree drawing itself, and the flush.
        /// The GPU upload that follows it in the game is not here.
        /// </summary>
        private static void MeasureDraw(RectangleControl root, int passes)
        {
            UIProfiler.Reset();
            UIProfiler.Enabled = true;

            var watch = Stopwatch.StartNew();

            for (int i = 0; i < passes; i++)
            {
                UIProfiler.CountPass();
                Render(root);
            }

            watch.Stop();
            UIProfiler.Enabled = false;

            Console.WriteLine("### Redraw (surface + draw + flush): " + Per(watch, passes) + " ms per pass");
            Console.WriteLine();
            Console.WriteLine(UIProfiler.Report("draw"));
        }

        private static string Per(Stopwatch watch, int passes)
        {
            return (watch.Elapsed.TotalMilliseconds / Math.Max(1, passes)).ToString("0.###");
        }

        /// <summary>
        /// The same three steps <c>CustomDialogElement.RenderDialog</c> takes, minus the upload:
        /// a surface the size of the dialog, the tree drawn onto it, and a flush.
        /// </summary>
        private static void Render(UIControl root)
        {
            int width = Math.Max(1, (int)root.Size.X);
            int height = Math.Max(1, (int)root.Size.Y);

            using (var surface = new ImageSurface(Format.Argb32, width, height))
            using (var ctx = new Context(surface))
            {
                ctx.Antialias = Antialias.Best;

                root.GenerateRenderData(surface, ctx);

                surface.Flush();
            }
        }

        private static RectangleControl BuildShowcase()
        {
            var root = new RectangleControl(
                backgroundColor: new ElementColor(0.20, 0.16, 0.13, 1.0),
                _Name: "root");

            ModernVintageGUI.Samples.ControlShowcase.Build(root, capi: null, withTitleBar: true);

            root.LayoutScale = 1.0;

            return root;
        }

        private static int Count(UIControl control)
        {
            int total = 1;

            foreach (UIControl child in control.Children)
            {
                total += Count(child);
            }

            return total;
        }

        private static int Depth(UIControl control)
        {
            int deepest = 0;

            foreach (UIControl child in control.Children)
            {
                deepest = Math.Max(deepest, Depth(child));
            }

            return deepest + 1;
        }
    }
}
