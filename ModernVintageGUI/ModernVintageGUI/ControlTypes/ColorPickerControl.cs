using Cairo;
using IS2Mod.ControlTypes;
using IS2Mod.ControlTypes.Events;
using IS2Mod.Enums;
using System;
using Vintagestory.API.Client;

namespace ModernVintageGUI.ControlTypes
{
    /// <summary>
    /// A colour picker: a square for saturation and brightness, a strip of hues beside it, and a
    /// swatch showing what came out.
    ///
    /// The square is drawn the way every picker draws it, and the way Cairo makes cheap: the
    /// chosen hue flat, a white to transparent gradient across it, and a transparent to black
    /// gradient down it. Three fills instead of a per pixel loop, and the same picture.
    ///
    /// Dragging inside either part keeps working when the cursor leaves the control, because the
    /// press captures the mouse - letting go of a colour halfway through because the pointer
    /// slipped over the edge is the thing that makes a picker feel broken.
    /// </summary>
    public class ColorPickerControl : UIControl
    {
        #region Styling
        private const double UnscaledDefaultSize = 140.0;

        /// <summary>How wide the hue strip is, and how far it sits from the square.</summary>
        private const double UnscaledHueWidth = 18.0;
        private const double UnscaledGap = 8.0;

        /// <summary>The swatch under the hue strip.</summary>
        private const double UnscaledSwatchHeight = 22.0;

        /// <summary>The ring that marks the picked spot in the square.</summary>
        private const double UnscaledMarkerRadius = 5.0;
        #endregion

        #region Properties
        private double _hue;          // 0..1
        private double _saturation = 1.0;
        private double _value = 1.0;

        /// <summary>The picked colour. Setting it moves the marks to match.</summary>
        public ElementColor SelectedColor
        {
            get => FromHsv(_hue, _saturation, _value);
            set => SetFromColor(value);
        }

        /// <summary>Hue, saturation and brightness of the pick, each 0 to 1.</summary>
        public double Hue => _hue;
        public double Saturation => _saturation;
        public double Brightness => _value;

        /// <summary>Raised whenever the pick changes, by dragging or from code.</summary>
        public event EventHandler<ElementColor>? ColorChanged;
        #endregion

        private enum DragTarget { None, Square, Hue }
        private DragTarget _dragging;

        public ColorPickerControl(string _Name = "", PointD? _Size = null, double _Margin = 5)
            : base(_Name,
                   _Size ?? new PointD(UnscaledDefaultSize + UnscaledGap + UnscaledHueWidth, UnscaledDefaultSize),
                   Orientation.None, _Margin, _Padding: 0)
        {
            IsAutoSize = false;
            IsFocusable = true;

            MouseDown += OnMouseDownHere;
            MouseMove += OnMouseMoveHere;
            MouseUp += OnMouseUpHere;
        }

        #region Geometry
        private double HueWidth => UnscaledHueWidth * LayoutScale;
        private double Gap => UnscaledGap * LayoutScale;
        private double SwatchHeight => UnscaledSwatchHeight * LayoutScale;

        /// <summary>The saturation and brightness square: everything left of the hue strip.</summary>
        private LayoutRect SquareBox()
        {
            double width = Math.Max(0, Size.X - HueWidth - Gap);
            return new LayoutRect(Position.X, Position.Y, width, Size.Y);
        }

        /// <summary>The hue strip, with the swatch taken off its bottom.</summary>
        private LayoutRect HueBox()
        {
            double height = Math.Max(0, Size.Y - SwatchHeight - Gap);
            return new LayoutRect(Position.X + Size.X - HueWidth, Position.Y, HueWidth, height);
        }

        private LayoutRect SwatchBox()
        {
            return new LayoutRect(
                Position.X + Size.X - HueWidth,
                Position.Y + Size.Y - SwatchHeight,
                HueWidth,
                SwatchHeight);
        }
        #endregion

        #region Interaction
        /// <summary>
        /// Mouse events carry screen coordinates while the layout is dialog local, so every
        /// point has to be brought into the same space as the boxes before it is compared
        /// against them. Getting this wrong is invisible in a dialog at the top left corner of
        /// the screen and breaks the control everywhere else.
        /// </summary>
        private PointD ToLocal(MouseEventArgs e)
        {
            PointD dialogPosition = Dialog?.Position ?? new PointD(0, 0);
            return new PointD(e.X - dialogPosition.X, e.Y - dialogPosition.Y);
        }

        private void OnMouseDownHere(object? sender, MouseEventArgs e)
        {
            PointD local = ToLocal(e);

            if (SquareBox().Contains(local.X, local.Y))
            {
                _dragging = DragTarget.Square;
            }
            else if (HueBox().Contains(local.X, local.Y))
            {
                _dragging = DragTarget.Hue;
            }
            else
            {
                return;
            }

            // The pointer leaves the control the moment a drag starts near an edge; without the
            // capture the rest of the drag would go to whatever is underneath.
            Dialog?.CaptureMouse(this);
            Apply(local.X, local.Y);
        }

        private void OnMouseMoveHere(object? sender, MouseEventArgs e)
        {
            if (_dragging != DragTarget.None)
            {
                PointD local = ToLocal(e);
                Apply(local.X, local.Y);
            }
        }

        private void OnMouseUpHere(object? sender, MouseEventArgs e)
        {
            _dragging = DragTarget.None;
        }

        /// <summary>Takes a point already in dialog local space - see <see cref="ToLocal"/>.</summary>
        private void Apply(double localX, double localY)
        {
            if (_dragging == DragTarget.Square)
            {
                LayoutRect box = SquareBox();

                if (box.Width <= 0 || box.Height <= 0)
                    return;

                _saturation = Math.Clamp((localX - box.X) / box.Width, 0, 1);

                // Down is darker, which is how every picker of this shape reads.
                _value = 1.0 - Math.Clamp((localY - box.Y) / box.Height, 0, 1);
            }
            else if (_dragging == DragTarget.Hue)
            {
                LayoutRect box = HueBox();

                if (box.Height <= 0)
                    return;

                _hue = Math.Clamp((localY - box.Y) / box.Height, 0, 1);
            }
            else
            {
                return;
            }

            Dialog?.Refresh();
            ColorChanged?.Invoke(this, SelectedColor);
        }

        private void SetFromColor(ElementColor color)
        {
            (double h, double s, double v) = ToHsv(color);

            _hue = h;
            _saturation = s;
            _value = v;

            Dialog?.Refresh();
            ColorChanged?.Invoke(this, SelectedColor);
        }

        /// <summary>One hit target - the picker has no children to lose a click to anyway.</summary>
        protected override UIControl? HitTestRecursive(UIControl control, double localX, double localY)
        {
            return control.ContainsLocalPoint(localX, localY) ? control : null;
        }
        #endregion

        #region Layout
        public override PointD CalculateSize()
        {
            PointD measured = ClampToMaxSize(ScaledExplicitSize);

            CalculatedSize = measured;
            SetLayoutSize(measured);

            return measured;
        }
        #endregion

        #region Rendering
        public override void GenerateRenderData(ImageSurface surface, Context ctx)
        {
            if (Size.X <= 0 || Size.Y <= 0)
                return;

            DrawSquare(ctx, SquareBox());
            DrawHueStrip(ctx, HueBox());
            DrawSwatch(ctx, SwatchBox());

            base.GenerateRenderData(surface, ctx);
        }

        private void DrawSquare(Context ctx, LayoutRect box)
        {
            if (box.Width <= 0 || box.Height <= 0)
                return;

            ctx.Save();

            ElementColor pure = FromHsv(_hue, 1, 1);

            ctx.Rectangle(box.X, box.Y, box.Width, box.Height);
            ctx.SetSourceRGB(pure.R / 255.0, pure.G / 255.0, pure.B / 255.0);
            ctx.Fill();

            // White on the left, fading out to the right: that is saturation.
            using (var toWhite = new LinearGradient(box.X, box.Y, box.X + box.Width, box.Y))
            {
                toWhite.AddColorStop(0, new Cairo.Color(1, 1, 1, 1));
                toWhite.AddColorStop(1, new Cairo.Color(1, 1, 1, 0));

                ctx.Rectangle(box.X, box.Y, box.Width, box.Height);
                ctx.SetSource(toWhite);
                ctx.Fill();
            }

            // Black at the bottom, fading out upwards: that is brightness.
            using (var toBlack = new LinearGradient(box.X, box.Y, box.X, box.Y + box.Height))
            {
                toBlack.AddColorStop(0, new Cairo.Color(0, 0, 0, 0));
                toBlack.AddColorStop(1, new Cairo.Color(0, 0, 0, 1));

                ctx.Rectangle(box.X, box.Y, box.Width, box.Height);
                ctx.SetSource(toBlack);
                ctx.Fill();
            }

            VanillaDraw.EmbossRoundRectangle(ctx, box.X, box.Y, box.Width, box.Height, inverse: true, depth: 1, radius: 1);

            DrawMarker(ctx,
                box.X + _saturation * box.Width,
                box.Y + (1.0 - _value) * box.Height);

            ctx.Restore();
        }

        private void DrawHueStrip(Context ctx, LayoutRect box)
        {
            if (box.Width <= 0 || box.Height <= 0)
                return;

            ctx.Save();

            using (var hues = new LinearGradient(box.X, box.Y, box.X, box.Y + box.Height))
            {
                // Six stops is the whole wheel - everything between them is a straight line in
                // RGB, which is exactly what the hue ramp is.
                for (int i = 0; i <= 6; i++)
                {
                    ElementColor stop = FromHsv(i / 6.0, 1, 1);
                    hues.AddColorStop(i / 6.0, new Cairo.Color(stop.R / 255.0, stop.G / 255.0, stop.B / 255.0));
                }

                ctx.Rectangle(box.X, box.Y, box.Width, box.Height);
                ctx.SetSource(hues);
                ctx.Fill();
            }

            VanillaDraw.EmbossRoundRectangle(ctx, box.X, box.Y, box.Width, box.Height, inverse: true, depth: 1, radius: 1);

            // A bar across the strip rather than a ring - the strip is too narrow for one.
            double y = box.Y + _hue * box.Height;

            ctx.SetSourceRGBA(1, 1, 1, 0.9);
            ctx.Rectangle(box.X, y - 1 * LayoutScale, box.Width, 2 * LayoutScale);
            ctx.Fill();

            ctx.Restore();
        }

        private void DrawSwatch(Context ctx, LayoutRect box)
        {
            if (box.Width <= 0 || box.Height <= 0)
                return;

            ElementColor color = SelectedColor;

            ctx.Save();

            GuiElement.RoundRectangle(ctx, box.X, box.Y, box.Width, box.Height, 1.0);
            ctx.SetSourceRGB(color.R / 255.0, color.G / 255.0, color.B / 255.0);
            ctx.Fill();

            VanillaDraw.EmbossRoundRectangle(ctx, box.X, box.Y, box.Width, box.Height, inverse: true, depth: 1, radius: 1);

            ctx.Restore();
        }

        private void DrawMarker(Context ctx, double x, double y)
        {
            double radius = UnscaledMarkerRadius * LayoutScale;

            // Black under white, so the ring stays visible on both a pale and a dark square.
            ctx.SetSourceRGBA(0, 0, 0, 0.7);
            ctx.Arc(x, y, radius + 1 * LayoutScale, 0, Math.PI * 2);
            ctx.LineWidth = 2 * LayoutScale;
            ctx.Stroke();

            ctx.SetSourceRGBA(1, 1, 1, 0.9);
            ctx.Arc(x, y, radius, 0, Math.PI * 2);
            ctx.LineWidth = 2 * LayoutScale;
            ctx.Stroke();
        }
        #endregion

        #region Colour maths
        /// <summary>Hue, saturation and brightness to a colour. All three are 0 to 1.</summary>
        public static ElementColor FromHsv(double hue, double saturation, double value)
        {
            hue = ((hue % 1.0) + 1.0) % 1.0;

            double sector = hue * 6.0;
            int index = (int)Math.Floor(sector) % 6;
            double fraction = sector - Math.Floor(sector);

            double p = value * (1 - saturation);
            double q = value * (1 - saturation * fraction);
            double t = value * (1 - saturation * (1 - fraction));

            (double r, double g, double b) = index switch
            {
                0 => (value, t, p),
                1 => (q, value, p),
                2 => (p, value, t),
                3 => (p, q, value),
                4 => (t, p, value),
                _ => (value, p, q)
            };

            return new ElementColor(new[] { r, g, b, 1.0 });
        }

        /// <summary>And back again.</summary>
        public static (double Hue, double Saturation, double Value) ToHsv(ElementColor color)
        {
            double r = color.R / 255.0;
            double g = color.G / 255.0;
            double b = color.B / 255.0;

            double max = Math.Max(r, Math.Max(g, b));
            double min = Math.Min(r, Math.Min(g, b));
            double span = max - min;

            double hue = 0;

            if (span > 0.00001)
            {
                if (max == r)
                {
                    hue = (g - b) / span / 6.0;
                }
                else if (max == g)
                {
                    hue = (2.0 + (b - r) / span) / 6.0;
                }
                else
                {
                    hue = (4.0 + (r - g) / span) / 6.0;
                }
            }

            hue = ((hue % 1.0) + 1.0) % 1.0;

            return (hue, max <= 0 ? 0 : span / max, max);
        }
        #endregion
    }
}
