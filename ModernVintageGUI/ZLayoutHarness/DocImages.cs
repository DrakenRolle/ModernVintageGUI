using Cairo;
using IS2Mod.ControlTypes;
using IS2Mod.Enums;
using System;
using System.Collections.Generic;
using System.IO;
using Vintagestory.API.Client;
using ModernVintageGUI.ControlTypes;
using IOPath = System.IO.Path;

namespace LayoutHarness
{
    /// <summary>
    /// Renders the illustrations used in README.md. Same layout code as the game, and the same
    /// dialog background as CustomDialogElement draws - but headless, so the pictures can be
    /// regenerated from a build instead of being re-taken by hand every time a control changes.
    ///
    /// What this cannot show is anything the game owns: the world behind the dialog, the real
    /// mouse cursor, and vanilla GUIs next to it.
    /// </summary>
    internal static class DocImages
    {
        /// <summary>Vanilla passes this to GuiElement.getPattern for the dialog background.</summary>
        private const double DirtPatternScale = 0.125;

        /// <summary>...and this as mulAlpha, out of 255.</summary>
        private const double DirtPatternAlpha = 64.0 / 255.0;

        /// <summary>
        /// Rendered at 2x so the pictures stay readable on a README. It is the same design, just
        /// at a higher GUI scale - not an upscaled image.
        /// </summary>
        private const double ImageScale = 2.0;

        public static int Generate(string outputDir)
        {
            Directory.CreateDirectory(outputDir);

            string? soilPath = FindSoilTexture();
            if (soilPath == null)
            {
                Console.WriteLine("Could not find gui/backgrounds/soil.png in the game assets - " +
                                  "the dialog background will be flat.");
            }

            Console.WriteLine("Generating README images into " + outputDir);
            Console.WriteLine();

            foreach ((string name, UIControl root) in BuildAll())
            {
                root.LayoutScale = ImageScale;
                root.PerformLayout();
                string path = IOPath.Combine(outputDir, name + ".png");
                Render(root, soilPath, path);
                Console.WriteLine($"  {name}.png  ({(int)Math.Ceiling(root.Size.X)}x{(int)Math.Ceiling(root.Size.Y)})");
            }

            RenderScaleComparison(soilPath, IOPath.Combine(outputDir, "readme-scales.png"));

            Console.WriteLine();
            return 0;
        }

        /// <summary>
        /// The same tree at several GUI scales, side by side on one canvas. This is the picture
        /// behind the proportional-scaling check in the harness.
        /// </summary>
        private static void RenderScaleComparison(string? soilPath, string path)
        {
            double[] scales = { 1.0, 1.5, 2.0 };
            const int gap = 16;

            var roots = new List<UIControl>();
            foreach (double scale in scales)
            {
                UIControl root = BuildStacking();
                root.LayoutScale = scale;
                root.PerformLayout();
                roots.Add(root);
            }

            int width = 0;
            int height = 0;
            foreach (UIControl root in roots)
            {
                width += (int)Math.Ceiling(root.Size.X) + gap;
                height = Math.Max(height, (int)Math.Ceiling(root.Size.Y));
            }
            width = Math.Max(1, width - gap);

            using (var surface = new ImageSurface(Format.Argb32, width, Math.Max(1, height)))
            using (var ctx = new Context(surface))
            {
                ctx.Antialias = Antialias.Best;

                double offsetX = 0;
                foreach (UIControl root in roots)
                {
                    int w = (int)Math.Ceiling(root.Size.X);
                    int h = (int)Math.Ceiling(root.Size.Y);

                    // Each tree has to be drawn at the origin of its own surface, then composited.
                    // RectangleControl blurs its borders through SurfaceTransformBlur, which works
                    // in absolute surface pixels and does not see the context transform - drawing
                    // the trees straight onto a shared canvas with a Translate smears the blur
                    // across the neighbouring panels.
                    using (var panel = new ImageSurface(Format.Argb32, w, h))
                    using (var panelCtx = new Context(panel))
                    {
                        panelCtx.Antialias = Antialias.Best;

                        DrawDialogBackground(panelCtx, soilPath, root.LayoutScale, w, h);
                        root.GenerateRenderData(panel, panelCtx);
                        panel.Flush();

                        // Bottom aligned, so the growth reads as growth rather than as drift.
                        ctx.SetSourceSurface(panel, (int)offsetX, height - h);
                        ctx.Paint();
                    }

                    offsetX += w + gap;
                }

                surface.Flush();
                surface.WriteToPng(path);
            }

            Console.WriteLine($"  readme-scales.png  ({width}x{height})");
        }

        #region Scenes
        private static IEnumerable<(string Name, UIControl Root)> BuildAll()
        {
            yield return ("readme-simple-dialog", BuildSimpleDialog());
            yield return ("readme-buttons", BuildButtons());
            yield return ("readme-buttons-hover", BuildButtonsHovered());
            yield return ("readme-stacking", BuildStacking());
            yield return ("readme-mixed-row", BuildMixedRow());
            yield return ("readme-title-bar", BuildTitleBar());
            yield return ("readme-context-menu", BuildContextMenu(hovered: false));
            yield return ("readme-context-menu-hover", BuildContextMenu(hovered: true));
            yield return ("readme-keyboard-focus", BuildKeyboardFocus());
            yield return ("readme-runtime-before", BuildRuntimeEdit(added: false));
            yield return ("readme-runtime-after", BuildRuntimeEdit(added: true));
        }

        /// <summary>
        /// Stands in for CustomDialogElement, which needs a running client. Same values its
        /// constructor uses: vertical stacking, no margin, padding 10.
        /// </summary>
        private static RectangleControl CreateDialogRoot()
        {
            var root = new RectangleControl(_Name: "dialog");
            root.InsideOrientation = Orientation.Top;
            root.Padding = 10;
            return root;
        }

        private static UIControl BuildSimpleDialog()
        {
            RectangleControl root = CreateDialogRoot();
            root.Children.Add(new TextLabelControl("Hi im Fancy!", _Name: "text"));
            return root;
        }

        private static UIControl BuildButtons()
        {
            RectangleControl root = CreateDialogRoot();

            var save = new ButtonControl(_Name: "saveButton");
            save.Text = "Save";
            root.Children.Add(save);

            var cancel = new ButtonControl(_Name: "cancelButton");
            cancel.Text = "Cancel";
            root.Children.Add(cancel);

            return root;
        }

        private static UIControl BuildButtonsHovered()
        {
            RectangleControl root = CreateDialogRoot();

            var save = new ButtonControl(_Name: "saveButton");
            save.Text = "Save";
            root.Children.Add(save);

            var cancel = new ButtonControl(_Name: "cancelButton");
            cancel.Text = "Cancel";
            root.Children.Add(cancel);

            // Drive the real hover handler rather than poking at the visuals, so the picture
            // shows whatever the control actually does on Enter.
            save.InvokeEventEnter(new MouseEvent(0, 0));

            return root;
        }

        /// <summary>
        /// Hover and keyboard focus side by side. Same approach as the hover picture: the real
        /// handlers are driven, so this is what the control does rather than what it is meant to.
        /// </summary>
        private static UIControl BuildKeyboardFocus()
        {
            RectangleControl root = CreateDialogRoot();

            var plain = new ButtonControl(_Name: "plainButton");
            plain.Text = "Save";
            root.Children.Add(plain);

            var focused = new ButtonControl(_Name: "focusedButton");
            focused.Text = "Tabbed to";
            root.Children.Add(focused);

            var hovered = new ButtonControl(_Name: "hoveredButton");
            hovered.Text = "Hovered";
            root.Children.Add(hovered);

            focused.InvokeGotFocus();
            hovered.InvokeEventEnter(new MouseEvent(0, 0));

            return root;
        }

        private static UIControl BuildStacking()
        {
            RectangleControl root = CreateDialogRoot();

            var header = new ButtonControl(_Name: "header");
            header.Text = "Vertical child";
            root.Children.Add(header);

            var row = new RectangleControl(_Name: "row");
            row.InsideOrientation = Orientation.Left;

            foreach (string caption in new[] { "One", "Two", "Three" })
            {
                var button = new ButtonControl();
                button.Text = caption;
                row.Children.Add(button);
            }

            root.Children.Add(row);
            return root;
        }

        private static UIControl BuildMixedRow()
        {
            RectangleControl root = CreateDialogRoot();

            var row = new RectangleControl(_Name: "row");
            row.InsideOrientation = Orientation.Left;

            var left = new ButtonControl();
            left.Text = "Test";
            row.Children.Add(left);

            var label = new TextLabelControl("in between", _Name: "label");
            label.Orientation = TextOrientation.Center;
            row.Children.Add(label);

            var right = new ButtonControl();
            right.Text = "Test";
            row.Children.Add(right);

            root.Children.Add(row);
            return root;
        }

        private static UIControl BuildTitleBar()
        {
            RectangleControl root = CreateDialogRoot();
            root.Padding = 0;

            root.Children.Add(new TitleBarControl("Inventory"));

            var content = new RectangleControl(_Name: "content");
            content.InsideOrientation = Orientation.Top;
            content.Padding = 10;

            var save = new ButtonControl();
            save.Text = "Save";
            content.Children.Add(save);

            root.Children.Add(content);
            return root;
        }

        private static UIControl BuildContextMenu(bool hovered)
        {
            RectangleControl root = CreateDialogRoot();
            root.Padding = 1;

            RectangleControl stack = ContextMenuControl.CreateMenuBackground("menu");

            var entries = new List<ContextMenuItem>
            {
                new ContextMenuItem("Fixed"),
                new ContextMenuItem("Movable"),
                new ContextMenuItem("More")
            };

            foreach (ContextMenuItem entry in entries)
            {
                stack.Children.Add(entry);
            }

            if (hovered)
            {
                entries[1].InvokeEventEnter(new MouseEvent(0, 0));
            }

            root.Children.Add(stack);
            return root;
        }

        private static UIControl BuildRuntimeEdit(bool added)
        {
            RectangleControl root = CreateDialogRoot();

            var row = new RectangleControl(_Name: "row");
            row.InsideOrientation = Orientation.Left;

            var one = new ButtonControl();
            one.Text = "One";
            row.Children.Add(one);

            var two = new ButtonControl();
            two.Text = "Two";
            row.Children.Add(two);

            if (added)
            {
                var extra = new ButtonControl();
                extra.Text = "Added at runtime";
                row.Children.Add(extra);
            }

            root.Children.Add(row);
            return root;
        }
        #endregion

        #region Rendering
        private static void Render(UIControl root, string? soilPath, string path)
        {
            int width = Math.Max(1, (int)Math.Ceiling(root.Size.X));
            int height = Math.Max(1, (int)Math.Ceiling(root.Size.Y));

            using (var surface = new ImageSurface(Format.Argb32, width, height))
            using (var ctx = new Context(surface))
            {
                ctx.Antialias = Antialias.Best;

                DrawDialogBackground(ctx, soilPath, root.LayoutScale, width, height);

                root.GenerateRenderData(surface, ctx);

                surface.Flush();
                surface.WriteToPng(path);
            }
        }

        /// <summary>
        /// The same three steps CustomDialogElement.DrawDialogBackground does: rounded rectangle,
        /// DialogStrongBgColor, dirt pattern on top.
        /// </summary>
        private static void DrawDialogBackground(
            Context ctx, string? soilPath, double layoutScale, int width, int height)
        {
            RoundRectangle(ctx, 0, 0, width, height, GuiStyle.DialogBGRadius);

            ctx.SetSourceRGBA(
                GuiStyle.DialogStrongBgColor[0],
                GuiStyle.DialogStrongBgColor[1],
                GuiStyle.DialogStrongBgColor[2],
                GuiStyle.DialogStrongBgColor[3]);
            ctx.FillPreserve();

            if (soilPath == null)
            {
                ctx.NewPath();
                return;
            }

            using (var texture = new ImageSurface(soilPath))
            using (var pattern = new SurfacePattern(texture))
            {
                pattern.Extend = Extend.Repeat;
                pattern.Filter = Filter.Nearest;

                // getPattern divides the requested scale by the GUI scale, so the grain of the
                // texture grows with the interface instead of staying at a fixed pixel size.
                var matrix = new Matrix();
                double patternScale = DirtPatternScale / Math.Max(0.0001, layoutScale);
                matrix.Scale(patternScale, patternScale);
                pattern.Matrix = matrix;

                ctx.Save();
                ctx.Clip();
                ctx.SetSource(pattern);
                ctx.PaintWithAlpha(DirtPatternAlpha);
                ctx.Restore();
            }
        }

        /// <summary>
        /// GuiElement.RoundRectangle without needing the client API.
        /// </summary>
        private static void RoundRectangle(Context ctx, double x, double y, double w, double h, double radius)
        {
            const double degrees = Math.PI / 180.0;

            ctx.NewPath();
            ctx.Arc(x + w - radius, y + radius, radius, -90 * degrees, 0);
            ctx.Arc(x + w - radius, y + h - radius, radius, 0, 90 * degrees);
            ctx.Arc(x + radius, y + h - radius, radius, 90 * degrees, 180 * degrees);
            ctx.Arc(x + radius, y + radius, radius, 180 * degrees, 270 * degrees);
            ctx.ClosePath();
        }
        #endregion

        private static string? FindSoilTexture()
        {
            string? gameDir = Environment.GetEnvironmentVariable("VINTAGE_STORY");
            if (string.IsNullOrWhiteSpace(gameDir))
                return null;

            string path = IOPath.Combine(
                gameDir, "assets", "game", "textures", "gui", "backgrounds", "soil.png");

            return File.Exists(path) ? path : null;
        }
    }
}
