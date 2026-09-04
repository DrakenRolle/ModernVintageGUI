using Cairo;
using IS2Mod.ControlTypes;
using IS2Mod.Enums;
using System;
using Vintagestory.API.Client;

namespace ModernVintageGUI.ControlTypes
{
    /// <summary>
    /// A bar that fills up: a furnace burning down, a recipe in progress, a loading step.
    ///
    /// Drawn the way GuiElementStatbar draws itself - a dark trough with a raised bevel and a
    /// filled bar over the part that is done - because that is the bar players already read as
    /// "how far along is this".
    ///
    /// Set <see cref="Value"/> from a tick listener or from the change events of whatever it is
    /// measuring. Nothing here polls: a control that reads the world every frame is a control
    /// that keeps redrawing a dialog nobody is looking at.
    /// </summary>
    public class ProgressBarControl : UIControl
    {
        #region Vanilla styling
        /// <summary>The trough, from GuiElementStatbar.ComposeElements.</summary>
        private const double TroughGrey = 0.15;

        /// <summary>Vanilla rounds both the trough and the bar at 1.</summary>
        private const double CornerRadius = 1.0;

        /// <summary>A sensible bar when the caller does not give one - the game's own health red.</summary>
        private static readonly double[] DefaultBarColor = { 0.6, 0.1, 0.1, 1.0 };

        /// <summary>Vanilla's default statbar height.</summary>
        public const double UnscaledDefaultHeight = 26.0;
        private const double UnscaledDefaultWidth = 200.0;

        private const int FontSize = 16;
        #endregion

        #region Properties
        private double _value;
        private double _min;
        private double _max = 1.0;

        /// <summary>Where the bar stands. Clamped into <see cref="Min"/>..<see cref="Max"/>.</summary>
        public double Value
        {
            get => _value;
            set => SetValue(value);
        }

        public double Min
        {
            get => _min;
            set { _min = value; SetValue(_value); }
        }

        public double Max
        {
            get => _max;
            set { _max = value; SetValue(_value); }
        }

        /// <summary>How full it is, 0 to 1. What the drawing actually uses.</summary>
        public double Fraction
        {
            get
            {
                double span = _max - _min;
                return span <= 0 ? 0 : Math.Clamp((_value - _min) / span, 0, 1);
            }
        }

        /// <summary>The colour of the filled part.</summary>
        public ElementColor BarColor { get; set; } = new ElementColor(DefaultBarColor);

        /// <summary>Fill from the right instead of the left.</summary>
        public bool RightToLeft { get; set; }

        /// <summary>
        /// Text drawn over the bar - a percentage, a count, a name. Empty for a plain bar.
        /// </summary>
        public string Text
        {
            get => _label.Text;
            set
            {
                _label.Text = value ?? "";
                Dialog?.Refresh();
            }
        }

        /// <summary>Raised when the value changes, whoever changed it.</summary>
        public event EventHandler<double>? ValueChanged;
        #endregion

        private readonly TextLabelControl _label;

        public ProgressBarControl(string _Name = "", double _Margin = 5)
            : base(_Name, new PointD(UnscaledDefaultWidth, UnscaledDefaultHeight), Orientation.None, _Margin, _Padding: 0)
        {
            IsAutoSize = false;

            _label = new TextLabelControl(
                text: "",
                fontName: GuiStyle.StandardFontName,
                fontSize: FontSize,
                textColor: new ElementColor(GuiStyle.DialogDefaultTextColor),
                orientation: TextOrientation.MiddleCenter,
                _Name: _Name + "_label",
                _Margin: 0,
                _Padding: 0)
            {
                IsAutoSize = false
            };

            Children.Add(_label);
        }

        private void SetValue(double value)
        {
            double clamped = Math.Clamp(value, Math.Min(_min, _max), Math.Max(_min, _max));

            if (Math.Abs(clamped - _value) < 0.0000001)
                return;

            _value = clamped;
            Dialog?.Refresh();

            ValueChanged?.Invoke(this, clamped);
        }

        #region Layout
        public override PointD CalculateSize()
        {
            foreach (UIControl child in Children)
            {
                child.CalculateSize();
            }

            PointD measured = ClampToMaxSize(IsAutoSize
                ? new PointD(UnscaledDefaultWidth * LayoutScale, UnscaledDefaultHeight * LayoutScale)
                : ScaledExplicitSize);

            CalculatedSize = measured;
            SetLayoutSize(measured);

            StretchLabel();

            return measured;
        }

        public override void NormalizeChildrenByDelta()
        {
            StretchLabel();
            base.NormalizeChildrenByDelta();
        }

        public override void CalculateAllPositions()
        {
            base.CalculateAllPositions();
            StretchLabel();
        }

        private void StretchLabel()
        {
            _label.SetLayoutSize(Size);
            _label.Position = Position;
        }
        #endregion

        #region Rendering
        public override void GenerateRenderData(ImageSurface surface, Context ctx)
        {
            double x = Position.X;
            double y = Position.Y;
            double width = Size.X;
            double height = Size.Y;

            if (width <= 0 || height <= 0)
                return;

            ctx.Save();

            GuiElement.RoundRectangle(ctx, x, y, width, height, CornerRadius);
            ctx.SetSourceRGBA(TroughGrey, TroughGrey, TroughGrey, 1.0);
            ctx.Fill();

            VanillaDraw.EmbossRoundRectangle(ctx, x, y, width, height, inverse: false, depth: 3, radius: 1);

            double filled = width * Fraction;

            // Vanilla skips the bar below a hundredth, where it would be a sliver of colour
            // rather than a bar.
            if (Fraction > 0.01 && filled > 0)
            {
                double barX = RightToLeft ? x + width - filled : x;

                GuiElement.RoundRectangle(ctx, barX, y, filled, height, CornerRadius);
                ctx.SetSourceRGBA(BarColor.R / 255.0, BarColor.G / 255.0, BarColor.B / 255.0, BarColor.A / 255.0);
                ctx.Fill();
            }

            ctx.Restore();

            // The label last, so it sits on top of the bar rather than under it.
            base.GenerateRenderData(surface, ctx);
        }
        #endregion
    }
}
