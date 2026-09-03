using Cairo;
using IS2Mod.ControlTypes;
using IS2Mod.ControlTypes.Custom;
using IS2Mod.ControlTypes.Events;
using IS2Mod.Enums;
using ModernVintageGUI.Enums;
using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Config;

namespace ModernVintageGUI.ControlTypes
{
    /// <summary>
    /// The bar across the top of a dialog: title, burger menu, close cross - and the handle the
    /// dialog is dragged by.
    ///
    /// Drawn to match GuiElementDialogTitleBar.ComposeTextElements() step for step, including its
    /// quirks: the light inset stroke is in raw pixels while everything around it is scaled, and
    /// the soft edge comes from blurring the surface after the stroke rather than from a gradient.
    /// The two icons are drawn by the game's own IconUtil, so they are the same shapes vanilla
    /// uses rather than a lookalike.
    ///
    /// It takes the whole width of its parent, so put it in a dialog with Padding = 0 and wrap the
    /// content below it in a padded container - otherwise the dialog padding insets the bar and it
    /// no longer reaches the edges the way vanilla does.
    /// </summary>
    public class TitleBarControl : UIControl
    {
        #region Vanilla metrics
        /// <summary>GuiStyle.TitleBarHeight.</summary>
        private const double BarHeight = 31.0;

        /// <summary>GuiElementDialogTitleBar.unscaledCloseIconSize.</summary>
        private const double CloseIconSize = 15.0;

        /// <summary>The menu icon is drawn two units larger than the cross.</summary>
        private const double MenuIconSize = CloseIconSize + 2;

        /// <summary>
        /// Vanilla uses a raw 5.0 here, not scaled(5.0) - both for the inset of the light stroke
        /// and for the width of the dark outline. Kept as is, so the bar looks the same.
        /// </summary>
        private const double EdgeInset = 5.0;

        private const double TitleFontSize = 16.0;

        /// <summary>The dark outline of the bar, GuiElementDialogTitleBar draws it in this.</summary>
        private static readonly double[] OutlineColor = { 0.17647058823529413, 7.0 / 51.0, 11.0 / 85.0, 1.0 };

        private static readonly double[] IconShadowColor = { 0.0, 0.0, 0.0, 0.3 };
        #endregion

        #region Properties
        public string Title { get; set; }

        /// <summary>
        /// Whether the dialog can be dragged by this bar. Switching it on also turns off the
        /// automatic centering of the dialog, because a dragged dialog must keep where it was put.
        /// </summary>
        public bool IsMovable
        {
            get => _isMovable;
            set
            {
                _isMovable = value;

                CustomDialogElement? dialog = Dialog;
                if (dialog == null)
                    return;

                dialog.AutoCenter = !value;

                if (!value)
                {
                    // Back to fixed: let the dialog snap to its automatic position again.
                    dialog.PerformLayout();
                    dialog.Refresh();
                }
            }
        }

        /// <summary>Raised when the close cross is clicked. Closes the dialog if unhandled.</summary>
        public event EventHandler? CloseRequested;

        /// <summary>The Fixed / Movable menu behind the burger icon.</summary>
        public ContextMenuControl? Menu { get; private set; }
        #endregion

        #region Private Fields
        private bool _isMovable;
        private bool _isDragging;
        private PointD _dragStartCursor;
        private PointD _dragStartDialog;
        #endregion

        public TitleBarControl(string title = "")
            : base(_Orientation: Orientation.None, _Margin: 0, _Padding: 0)
        {
            Title = title;

            Clicked += OnClicked;
            MouseDown += OnMouseDown;
            MouseMove += OnMouseMove;
            MouseUp += OnMouseUp;
        }

        #region Layout
        /// <summary>A bar is one piece - clicks are dispatched by region, not by child control.</summary>
        protected override UIControl? HitTestRecursive(UIControl control, double localX, double localY)
        {
            return control.ContainsLocalPoint(localX, localY) ? control : null;
        }

        public override PointD CalculateSize()
        {
            // Wide enough that the title is not run over by the icons. The final width comes from
            // the parent, which stretches the bar across the dialog.
            double minimumWidth =
                Scaled(GuiStyle.ElementToDialogPadding) + MeasureTitleWidth() +
                Scaled(MenuIconSize) + Scaled(CloseIconSize) + Scaled(44.0);

            PointD measured = new PointD(minimumWidth, Scaled(BarHeight));

            CalculatedSize = measured;
            SetLayoutSize(measured);

            return measured;
        }

        private double MeasureTitleWidth()
        {
            if (string.IsNullOrEmpty(Title))
                return 0;

            using (var surface = new ImageSurface(Format.Argb32, 1, 1))
            using (var ctx = new Context(surface))
            {
                SetupTitleFont(ctx);
                return ctx.TextExtents(Title).XAdvance;
            }
        }

        private double Scaled(double value) => value * LayoutScale;

        private void SetupTitleFont(Context ctx)
        {
            ctx.SelectFontFace(GuiStyle.StandardFontName, FontSlant.Normal, FontWeight.Normal);
            ctx.SetFontSize(Scaled(TitleFontSize));
        }
        #endregion

        #region Icon regions
        /// <summary>The close cross, in the same space as Position and Size.</summary>
        private RectD CloseIconRect()
        {
            double size = Scaled(CloseIconSize);
            return new RectD(
                Position.X + Size.X - size - Scaled(12.0),
                Position.Y + Scaled(7.0),
                size,
                size);
        }

        /// <summary>The burger icon, in the same space as Position and Size.</summary>
        private RectD MenuIconRect()
        {
            double closeSize = Scaled(CloseIconSize);
            double size = Scaled(MenuIconSize);

            return new RectD(
                Position.X + Size.X - closeSize - size - Scaled(20.0),
                Position.Y + Scaled(6.0),
                size,
                size);
        }
        #endregion

        #region Rendering
        public override void GenerateRenderData(ImageSurface surface, Context ctx)
        {
            DrawBar(surface, ctx);
            DrawTitle(ctx);
            DrawIcons(ctx);

            base.GenerateRenderData(surface, ctx);
        }

        private void DrawBar(ImageSurface surface, Context ctx)
        {
            double[] strong = GuiStyle.DialogStrongBgColor;
            double[] light = GuiStyle.DialogLightBgColor;

            // Brightened fill - the bar sits a touch lighter than the dialog below it.
            GuiElement.RoundRectangle(ctx, Position.X, Position.Y, Size.X, Size.Y, 0.0);
            ctx.SetSourceRGBA(strong[0] * 1.2, strong[1] * 1.2, strong[2] * 1.2, strong[3]);
            ctx.FillPreserve();

            // Light stroke, inset. On its own this is a hard band; the blur below turns it into
            // the soft inner glow the vanilla bar has.
            GuiElement.RoundRectangle(
                ctx,
                Position.X + EdgeInset,
                Position.Y + EdgeInset,
                Size.X - 2 * EdgeInset,
                Size.Y - 2 * EdgeInset,
                0.0);
            ctx.SetSourceRGBA(light[0] * 1.6, strong[1] * 1.6, strong[2] * 1.6, 1.0);
            ctx.LineWidth = EdgeInset * 1.75;
            ctx.StrokePreserve();

            // Reads the pixel buffer, so the strokes above have to be committed first.
            surface.Flush();

            double blur = Scaled(8.0);
            try
            {
                // The last two arguments are x2/y2, not width/height. Vanilla passes OuterWidth
                // and InnerHeight here, which only lines up because its bar sits at 0/0 - ours
                // has to add the position.
                SurfaceTransformBlur.BlurPartial(
                    surface,
                    blur,
                    (int)(2.0 * blur + 1.0),
                    (int)Position.X,
                    (int)Position.Y,
                    (int)(Position.X + Size.X),
                    (int)(Position.Y + Size.Y));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Title bar blur failed: {ex.Message}");
            }

            // Hard dark outline on top of the blur, open at the bottom where the dialog continues.
            ctx.NewPath();
            ctx.MoveTo(Position.X, Position.Y + Size.Y);
            ctx.LineTo(Position.X, Position.Y);
            ctx.LineTo(Position.X + Size.X, Position.Y);
            ctx.LineTo(Position.X + Size.X, Position.Y + Size.Y);
            ctx.SetSourceRGBA(OutlineColor[0], OutlineColor[1], OutlineColor[2], OutlineColor[3]);
            ctx.LineWidth = EdgeInset;
            ctx.Stroke();
        }

        private void DrawTitle(Context ctx)
        {
            if (string.IsNullOrEmpty(Title))
                return;

            SetupTitleFont(ctx);

            double[] textColor = GuiStyle.DialogDefaultTextColor;
            ctx.SetSourceRGBA(textColor[0], textColor[1], textColor[2], textColor[3]);

            Cairo.FontExtents fe = ctx.FontExtents;
            double x = Position.X + Scaled(GuiStyle.ElementToDialogPadding);
            double y = Position.Y + (Size.Y - fe.Height) / 2.0 + fe.Ascent + Scaled(1.0);

            ctx.MoveTo(x, y);
            ctx.ShowText(Title);
        }

        /// <summary>
        /// The game's own icon renderer. DrawCross and Drawmenuicon_svg only emit Cairo paths and
        /// never touch the client API, so a standalone instance works when there is no running
        /// client - which is what lets the layout harness render the bar complete with its icons.
        /// </summary>
        private static IconUtil? _fallbackIcons;

        private static IconUtil FallbackIcons => _fallbackIcons ??= new IconUtil(null!);

        private void DrawIcons(Context ctx)
        {
            IconUtil icons = Dialog?.Api?.Gui.Icons ?? FallbackIcons;

            double[] iconColor = GuiStyle.DialogDefaultTextColor;
            double lineWidth = Scaled(2.0);

            RectD close = CloseIconRect();
            RectD menu = MenuIconRect();

            // Vanilla draws every icon twice: offset by two in translucent black for the drop
            // shadow, then the icon itself.
            ctx.Operator = Operator.Over;

            ctx.SetSourceRGBA(IconShadowColor[0], IconShadowColor[1], IconShadowColor[2], IconShadowColor[3]);
            icons.DrawCross(ctx, close.X + 2.0, close.Y + 2.0, lineWidth, close.Width);

            ctx.SetSourceRGBA(iconColor[0], iconColor[1], iconColor[2], iconColor[3]);
            icons.DrawCross(ctx, close.X, close.Y, lineWidth, close.Width);

            icons.Drawmenuicon_svg(
                ctx, menu.X + 2.0, menu.Y + 2.0, (float)menu.Width, (float)menu.Height, IconShadowColor);

            icons.Drawmenuicon_svg(
                ctx, menu.X, menu.Y + 1.0, (float)menu.Width, (float)menu.Height, iconColor);
        }
        #endregion

        #region Interaction
        private void OnMouseDown(object? sender, MouseEventArgs e)
        {
            PointD local = ToControlSpace(e.X, e.Y);

            if (CloseIconRect().Contains(local) || MenuIconRect().Contains(local))
            {
                // Handled on the click, so a press on an icon never starts a drag.
                return;
            }

            if (!IsMovable)
                return;

            CustomDialogElement? dialog = Dialog;
            if (dialog == null)
                return;

            _isDragging = true;
            _dragStartCursor = new PointD(e.X, e.Y);
            _dragStartDialog = dialog.Position;

            dialog.CaptureMouse(this);
        }

        private void OnMouseMove(object? sender, MouseEventArgs e)
        {
            if (!_isDragging)
                return;

            CustomDialogElement? dialog = Dialog;
            if (dialog == null)
                return;

            double x = _dragStartDialog.X + (e.X - _dragStartCursor.X);
            double y = _dragStartDialog.Y + (e.Y - _dragStartCursor.Y);

            // Keep a grabbable strip on screen, so a dialog cannot be dragged out of reach.
            double maxX = dialog.Api.Render.FrameWidth - Scaled(40.0);
            double maxY = dialog.Api.Render.FrameHeight - Scaled(BarHeight);

            x = Math.Max(-(dialog.Size.X - Scaled(40.0)), Math.Min(x, maxX));
            y = Math.Max(0, Math.Min(y, maxY));

            // Only the blit position changes, so no redraw of the surface is needed.
            dialog.SetPosition(x, y);
        }

        private void OnMouseUp(object? sender, MouseEventArgs e)
        {
            if (!_isDragging)
                return;

            _isDragging = false;
            Dialog?.ReleaseMouseCapture();
        }

        /// <summary>
        /// Mouse events carry screen coordinates while Position and Size are dialog local.
        /// </summary>
        private PointD ToControlSpace(double screenX, double screenY)
        {
            CustomDialogElement? dialog = Dialog;
            if (dialog == null)
                return new PointD(screenX, screenY);

            return new PointD(screenX - dialog.Position.X, screenY - dialog.Position.Y);
        }

        /// <summary>
        /// Builds the Fixed / Movable menu on first use - it needs a reachable dialog, which only
        /// exists once the bar is part of a laid out tree.
        /// </summary>
        private void EnsureMenu()
        {
            if (Menu != null)
                return;

            Menu = new ContextMenuControl(
                this,
                new List<ContextMenuItem>
                {
                    new ContextMenuItem(Lang.Get("Fixed")),
                    new ContextMenuItem(Lang.Get("Movable"))
                },
                "titleBarMode",
                ContextMenuAnchor.BottomLeft);

            Menu.ItemActivated += (sender, args) =>
            {
                // Index rather than caption, so this keeps working in any language.
                IsMovable = args.Item == Menu!.Items[1];
            };
        }

        /// <summary>
        /// Clicks are dispatched by region: the cross closes, the burger opens the menu, the rest
        /// of the bar is drag surface and does nothing on a plain click.
        /// </summary>
        private void OnClicked(object? sender, MouseEventArgs e)
        {
            PointD local = ToControlSpace(e.X, e.Y);

            if (CloseIconRect().Contains(local))
            {
                CloseRequested?.Invoke(this, EventArgs.Empty);

                if (CloseRequested == null)
                {
                    Dialog?.Hide();
                }

                return;
            }

            if (MenuIconRect().Contains(local))
            {
                EnsureMenu();

                // Line the menu up with the burger icon instead of the left edge of the bar.
                Menu!.Offset = new PointD(MenuIconRect().X - Position.X, 0);
                Menu.Toggle();
            }
        }
        #endregion
    }

    /// <summary>A plain rectangle helper - Cairo has no double rectangle type of its own.</summary>
    internal readonly struct RectD
    {
        public double X { get; }
        public double Y { get; }
        public double Width { get; }
        public double Height { get; }

        public RectD(double x, double y, double width, double height)
        {
            X = x;
            Y = y;
            Width = width;
            Height = height;
        }

        public bool Contains(PointD point)
        {
            return point.X >= X && point.X <= X + Width
                && point.Y >= Y && point.Y <= Y + Height;
        }
    }
}
