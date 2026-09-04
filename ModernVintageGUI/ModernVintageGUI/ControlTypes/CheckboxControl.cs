using Cairo;
using IS2Mod.ControlTypes;
using IS2Mod.ControlTypes.Events;
using IS2Mod.Enums;
using System;
using Vintagestory.API.Client;

namespace ModernVintageGUI.ControlTypes
{
    /// <summary>
    /// A box the player switches on and off, with an optional caption next to it.
    ///
    /// Drawn the way GuiElementSwitch draws itself: a sunken square, and when it is on an inner
    /// square filled with the water pattern the game uses for exactly this. The caption is a
    /// label of ours rather than part of the box, so it wraps, aligns and measures like every
    /// other piece of text in a dialog.
    ///
    /// The whole control is the hit target, caption included - a checkbox whose label does not
    /// toggle it is a small daily annoyance.
    /// </summary>
    public class CheckboxControl : UIControl
    {
        #region Vanilla styling
        /// <summary>GuiElementSwitch's default size and padding.</summary>
        public const double UnscaledBoxSize = 30.0;
        private const double UnscaledBoxPadding = 4.0;

        /// <summary>Room between the box and its caption.</summary>
        private const double UnscaledCaptionGap = 8.0;

        /// <summary>The pattern vanilla fills a switched on box with, at its own alpha and scale.</summary>
        private const int PatternAlpha = 255;
        private const float PatternScale = 0.5f;

        private const int FontSize = 16;
        #endregion

        #region Properties
        private bool _isChecked;

        /// <summary>
        /// Whether the box is ticked. Setting it raises <see cref="CheckedChanged"/>, so a
        /// handler sees a change made from code the same way it sees one made by the player.
        /// </summary>
        public bool IsChecked
        {
            get => _isChecked;
            set => SetChecked(value);
        }

        /// <summary>The caption beside the box. Empty for a box on its own.</summary>
        public string Text
        {
            get => _label.Text;
            set
            {
                _label.Text = value ?? "";
                RecomposeToMain();
            }
        }

        /// <summary>Raised whenever the tick changes, by click, by keyboard or from code.</summary>
        public event EventHandler<bool>? CheckedChanged;
        #endregion

        private readonly TextLabelControl _label;
        private bool _isHovered;

        public CheckboxControl(string text = "", bool isChecked = false, string _Name = "", double _Margin = 5)
            : base(_Name, _Size: null, Orientation.None, _Margin, _Padding: 0)
        {
            _isChecked = isChecked;

            _label = new TextLabelControl(
                text: text,
                fontName: GuiStyle.StandardFontName,
                fontSize: FontSize,
                textColor: new ElementColor(GuiStyle.DialogDefaultTextColor),
                orientation: TextOrientation.MiddleLeft,
                _Name: _Name + "_label",
                _Margin: 0,
                _Padding: 0);

            Children.Add(_label);

            IsFocusable = true;

            Clicked += (sender, e) => SetChecked(!_isChecked);
            Enter += (sender, e) => { _isHovered = true; Dialog?.Refresh(); };
            Exit += (sender, e) => { _isHovered = false; Dialog?.Refresh(); };
            GotFocus += (sender, e) => Dialog?.Refresh();
            LostFocus += (sender, e) => Dialog?.Refresh();
        }

        private void SetChecked(bool value)
        {
            if (_isChecked == value)
                return;

            _isChecked = value;
            Dialog?.Refresh();

            CheckedChanged?.Invoke(this, value);
        }

        #region Layout
        /// <summary>
        /// The control is one hit target: the caption toggles the box, and the label must not
        /// take the click on its way there.
        /// </summary>
        protected override UIControl? HitTestRecursive(UIControl control, double localX, double localY)
        {
            return control.ContainsLocalPoint(localX, localY) ? control : null;
        }

        public override PointD CalculateSize()
        {
            foreach (UIControl child in Children)
            {
                child.CalculateSize();
            }

            double box = UnscaledBoxSize * LayoutScale;
            double caption = string.IsNullOrEmpty(_label.Text)
                ? 0
                : UnscaledCaptionGap * LayoutScale + _label.Size.X;

            PointD measured = ClampToMaxSize(IsAutoSize
                ? new PointD(box + caption, Math.Max(box, _label.Size.Y))
                : ScaledExplicitSize);

            CalculatedSize = measured;
            SetLayoutSize(measured);

            PlaceLabel();

            return measured;
        }

        public override void NormalizeChildrenByDelta()
        {
            PlaceLabel();
            base.NormalizeChildrenByDelta();
        }

        public override void CalculateAllPositions()
        {
            base.CalculateAllPositions();
            PlaceLabel();
        }

        private void PlaceLabel()
        {
            double left = (UnscaledBoxSize + UnscaledCaptionGap) * LayoutScale;

            _label.SetLayoutSize(new PointD(Math.Max(0, Size.X - left), Size.Y));
            _label.Position = new PointD(Position.X + left, Position.Y);
        }
        #endregion

        #region Rendering
        public override void GenerateRenderData(ImageSurface surface, Context ctx)
        {
            double size = Math.Min(UnscaledBoxSize * LayoutScale, Size.Y);
            double x = Position.X;

            // Centred on the caption's line rather than on the top, so a wrapped caption does
            // not leave the box floating next to its first line.
            double y = Position.Y + (Size.Y - size) / 2.0;

            ctx.Save();

            ctx.SetSourceRGBA(0.0, 0.0, 0.0, 0.2);
            GuiElement.RoundRectangle(ctx, x, y, size, size, 1.0);
            ctx.Fill();

            VanillaDraw.EmbossRoundRectangle(ctx, x, y, size, size, inverse: true, depth: 1, radius: 1);

            if (_isChecked)
            {
                DrawTick(ctx, x, y, size);
            }

            if (_isHovered || HasKeyboardFocus)
            {
                // The same ring the rest of the framework uses to say "this is the one".
                ctx.SetSourceRGBA(GuiStyle.DialogHighlightColor);
                GuiElement.RoundRectangle(ctx, x, y, size, size, 1.0);
                ctx.LineWidth = 2.0 * LayoutScale;
                ctx.Stroke();
            }

            ctx.Restore();

            base.GenerateRenderData(surface, ctx);
        }

        private void DrawTick(Context ctx, double x, double y, double size)
        {
            double padding = UnscaledBoxPadding * LayoutScale;
            double inner = size - padding * 2;

            if (inner <= 0)
                return;

            GuiElement.RoundRectangle(ctx, x + padding, y + padding, inner, inner, 1.0);
            ctx.SetSourceRGBA(0.0, 0.0, 0.0, 1.0);
            ctx.FillPreserve();

            ICoreClientAPI? api = Dialog?.Api;

            if (api == null)
            {
                // No client - the layout harness. The flat fill above is the picture.
                ctx.NewPath();
                return;
            }

            // The texture vanilla fills a switched on box with. Without it the box reads as a
            // black hole rather than as the game's own switch.
            SurfacePattern pattern = GuiElement.getPattern(
                api, GuiElement.waterTextureName, doCache: true, PatternAlpha, PatternScale);

            ctx.SetSource(pattern);
            ctx.Fill();
        }
        #endregion
    }
}
