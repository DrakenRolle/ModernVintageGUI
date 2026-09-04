using Cairo;
using IS2Mod.Enums;
using ModernVintageGUI.ControlTypes;
using System;
using System.Diagnostics;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace IS2Mod.ControlTypes
{
    public class ButtonControl : UIControl
    {
        private readonly RectangleControl _border = CreateBorder();
        private readonly RectangleControl _borderTop = CreateBorderTop();
        private readonly RectangleControl _borderBottom = CreateBorderBottom();
        private readonly RectangleControl _focusRing = CreateFocusRing();

        private readonly TextLabelControl _textLabel = CreateTextLabel();

        /// <summary>Alpha of the focus ring while the button holds the keyboard focus.</summary>
        private const double FocusRingAlpha = 0.85;

        public string Text
        {
            get => _textLabel.Text;
            set => _textLabel.Text = value;
        }

        /// <summary>
        /// One of the game's GUI icons drawn on the button - a wrench, a trash can, an arrow.
        /// With a caption it sits to the left of it; on its own it is centred, which is the
        /// icon-only button.
        /// </summary>
        public string? IconName { get; set; }

        /// <summary>A picture from the mod's assets instead of a named icon.</summary>
        public AssetLocation? IconAsset { get; set; }

        /// <summary>
        /// How big the icon is drawn, in author units. Zero - the default - takes it from the
        /// button instead, so the icon grows with the button and with the GUI scale rather than
        /// sitting there at a fixed size no matter how tall the button got.
        /// </summary>
        public double UnscaledIconSize { get; set; }

        /// <summary>How much of the button's height an automatically sized icon takes.</summary>
        public double IconHeightFraction { get; set; } = 0.6;

        /// <summary>How far the icon sits from the left edge when there is a caption too.</summary>
        public double UnscaledIconInset { get; set; } = 8.0;

        /// <summary>The icon's edge length in device pixels, however it was decided.</summary>
        private double IconSize()
        {
            double fromButton = Size.Y * IconHeightFraction;
            double requested = UnscaledIconSize > 0 ? UnscaledIconSize * LayoutScale : fromButton;

            return Math.Max(0, Math.Min(requested, Math.Min(Size.X, Size.Y)));
        }

        /// <summary>The gap between the icon and the caption, when there is both.</summary>
        private double IconGap()
        {
            return HasIcon && !string.IsNullOrEmpty(Text) ? UnscaledIconInset * LayoutScale : 0;
        }

        /// <summary>
        /// How wide the icon and the caption are together.
        ///
        /// The two are placed as one block and that block is centred, which is the difference
        /// between a button that reads right and one where the caption sits off to the right of
        /// the middle: indenting the label and then centring it in what is left over pushes it
        /// half the icon's width too far.
        /// </summary>
        private double GroupWidth(double textWidth)
        {
            return (HasIcon ? IconSize() : 0) + IconGap() + textWidth;
        }

        /// <summary>Where the icon was placed last, so the drawing agrees with the layout.</summary>
        private double _iconLeft;

        private bool HasIcon => IconName != null || IconAsset != null;

        private bool _showEmboss = true;

        /// <summary>
        /// The bevel: a light edge along the top left and a dark one along the bottom right.
        /// On by default, because it is what the game's own buttons have.
        ///
        /// It is a switch because the light edge is deliberately blurred and reaches a little
        /// past the button, which reads as depth on a button standing on its own and as noise on
        /// a panel packed with them. A dense panel can turn it off and keep the rest of the look.
        /// </summary>
        public bool ShowEmboss
        {
            get => _showEmboss;
            set
            {
                if (_showEmboss == value)
                    return;

                _showEmboss = value;

                _borderTop.BorderWidth = value ? 4 : 0;
                _borderBottom.BorderWidth = value ? 3 : 0;

                Dialog?.Refresh();
            }
        }

        public ButtonControl(
            string _Name = "",
            PointD? _Size = null,
            Orientation _Orientation = Orientation.Top,
            double _Margin = 5,
            double _Padding = 0,
            int _Index = 0)
            : base(_Name, _Size, _Orientation, _Margin, _Padding, _Index)
        {
            InitializeComponents();
        }

        private static RectangleControl CreateBorder()
        {
            return new RectangleControl(
                _Orientation: Orientation.Top
            );
        }

        // FIXED: Removed margin from border overlays - they should align with the button bounds
        private static RectangleControl CreateBorderBottom()
        {
            return new RectangleControl(
                borderWidth: 3,
                _Margin: 0,  // Changed from 10 to 0
                borderColor: new ElementColor(new double[] { 0.0, 0.0, 0.0, 0.3 }),
                _Orientation: Orientation.None,  // Changed to None so it overlays
                hiddenBorders: new RectangleBorderStyle[] { RectangleBorderStyle.Left, RectangleBorderStyle.Top }
            );
        }

        private static RectangleControl CreateBorderTop()
        {
            return new RectangleControl(
                borderWidth: 4,
                _Margin: 0,  // Changed from 10 to 0
                borderColor: new ElementColor(new double[] { 1.0, 1.0, 1.0, 0.3 }),
                _Orientation: Orientation.None,  // Changed to None so it overlays
                hiddenBorders: new RectangleBorderStyle[] { RectangleBorderStyle.Bottom, RectangleBorderStyle.Right }, blurEdgeWidth: 3, blurRange: 3
            );
        }

        /// <summary>
        /// The ring that shows which control the keyboard is on. Drawn in the game's own
        /// highlight colour so it reads as "selected" rather than as another border, and fully
        /// transparent until the button is focused.
        ///
        /// It is a separate overlay rather than a change to the existing borders on purpose:
        /// hover and focus are independent states and a button can be in both at once, so they
        /// must not write to the same colour.
        /// </summary>
        private static RectangleControl CreateFocusRing()
        {
            return new RectangleControl(
                borderWidth: 2,
                borderColor: FocusRingColor(0.0),
                _Margin: 0,
                _Orientation: Orientation.None);
        }

        private static ElementColor FocusRingColor(double alpha)
        {
            var color = new ElementColor(GuiStyle.DialogHighlightColor);
            color.A = (byte)(alpha * 255);
            return color;
        }

        // Create text label - will auto-size initially, then fill border
        private static TextLabelControl CreateTextLabel()
        {
            var buttonFont = CairoFont.ButtonText();
            var label = new TextLabelControl(
                text: "Button",
                fontName: buttonFont.Fontname,
                fontSize: (int)buttonFont.UnscaledFontsize,
                fontWeight: buttonFont.FontWeight,
                fontSlant: buttonFont.Slant,
                textColor: new ElementColor(buttonFont.Color),
                orientation: TextOrientation.MiddleCenter,
                wordWrap: false,
                padding: 5,
                _Margin: 0,
                _Orientation: Orientation.None  // None so it fills the border area
            );
            label.IsAutoSize = false;
            return label;
        }

        private void InitializeComponents()
        {
            Children.Add(_border);

            _border.Children.Add(_borderTop);
            _border.Children.Add(_borderBottom);
            _border.Children.Add(_textLabel);

            // Last, so the ring is drawn over the emboss and the label instead of under them.
            _border.Children.Add(_focusRing);

            // A button is something the player operates, so it belongs in the tab order. Its
            // parts do not - they stay non focusable, which is what makes Tab land on the button
            // itself and Enter reach the Clicked handler a caller subscribed to.
            IsFocusable = true;

            this.Clicked += ButtonControl_Clicked;
            this.Enter += ButtonControl_Enter;
            this.Exit += ButtonControl_Exit;
            this.MouseDown += ButtonControl_MouseDown;
            this.MouseUp += ButtonControl_MouseUp;
            this.GotFocus += ButtonControl_GotFocus;
            this.LostFocus += ButtonControl_LostFocus;
        }

        private void ButtonControl_GotFocus(object? sender, System.EventArgs e)
        {
            _focusRing.BorderColor = FocusRingColor(FocusRingAlpha);
            Dialog?.Refresh();
        }

        private void ButtonControl_LostFocus(object? sender, System.EventArgs e)
        {
            _focusRing.BorderColor = FocusRingColor(0.0);
            Dialog?.Refresh();
        }

        private void ButtonControl_MouseUp(object? sender, Events.MouseEventArgs e)
        {
            _borderBottom.BorderColor.A = (byte)(0.4 * 255);
            _borderBottom.BorderWidth = 4;

            _border.BackgroundColor.A = (byte)(0.1 * 255);
            _border.BlurEdgeWidth = 3;
            _border.BlurRange = 3;
            Dialog?.Refresh();

        }

        private void ButtonControl_MouseDown(object? sender, Events.MouseEventArgs e)
        {
            _borderBottom.BorderColor.A = (byte)(0.4 * 255);
            _borderBottom.BorderWidth = 4;

            _border.BackgroundColor.A = (byte)(0.3 * 255);
            _border.BlurEdgeWidth = 3;
            _border.BlurRange = 3;
            Dialog?.Refresh();

        }

        private void ButtonControl_Exit(object? sender, Events.MouseEventArgs e)
        {
            _borderBottom.BorderColor.A = (byte)(0.3 * 255);
            _border.BackgroundColor.A = (byte)(0.0 * 255);

            _borderBottom.BorderWidth = 3;
            _border.BlurEdgeWidth = 3;
            _border.BlurRange = 0;
            Dialog?.Refresh();
            Debug.WriteLine("Button Exit");
        }

        private void ButtonControl_Enter(object? sender, Events.MouseEventArgs e)
        {
            _borderBottom.BorderColor.A = (byte)(0.4 * 255);
            _borderBottom.BorderWidth = 4;

            _border.BackgroundColor.A = (byte)(0.1 * 255);
            _border.BlurEdgeWidth = 3;
            _border.BlurRange = 3;


            Dialog?.Refresh();
            Debug.WriteLine("Button Enter");
        }

        private void ButtonControl_Clicked(object? sender, Events.MouseEventArgs e)
        {
            Debug.WriteLine("Button Clicked");
        }

        /// <summary>
        /// A button is an atomic hit target: its border and label children must never become
        /// the hovered/pressed control, otherwise the visual state handlers below never fire.
        /// </summary>
        protected override UIControl? HitTestRecursive(UIControl control, double localX, double localY)
        {
            return control.ContainsLocalPoint(localX, localY) ? control : null;
        }
        public override PointD CalculateSize()
        {
            if(IsAutoSize == false)
            {
                // If not auto-sizing, the assigned size is the measurement - converted to device
                // pixels, like every other authored dimension. CalculatedSize has to be kept in
                // sync: CalculateClippedSize uses it to decide whether the control overflows its
                // parent, and a stale 0 there makes that check meaningless.
                CalculatedSize = ClampToMaxSize(ScaledExplicitSize);
                SetLayoutSize(CalculatedSize);
                return CalculatedSize;
            }
            // Let base calculate size normally
            PointD size = base.CalculateSize();

            // An auto sizing button has to be wide enough for its caption *and* its icon, or
            // the icon eats into the text it was put next to.
            // Room for the icon and the gap next to the caption the base already measured.
            double extra = (HasIcon ? IconSize() : 0) + IconGap();

            if (extra > 0)
            {
                size = ClampToMaxSize(new PointD(size.X + extra, size.Y));
                CalculatedSize = size;
                SetLayoutSize(size);
            }

            _textLabel.IsAutoSize = false;
            LayoutParts();

            return size;
        }

        public override void NormalizeChildrenByDelta()
        {
            LayoutParts();
            base.NormalizeChildrenByDelta();
        }

        public override void CalculateAllPositions()
        {
            base.CalculateAllPositions();
            LayoutParts();
        }

        /// <summary>
        /// Puts the parts on top of each other at the button.s own size - the frame, the two
        /// bevel overlays and the focus ring are all the same rectangle - and insets the caption
        /// by whatever the icon takes, so the two cannot end up in the same place.
        ///
        /// One method rather than the same block repeated in three layout steps, which is how
        /// the icon inset came to be missing from all of them.
        /// </summary>
        private void LayoutParts()
        {
            if (_border == null || _textLabel == null)
                return;

            _border.SetLayoutSize(Size);
            _border.Position = Position;

            _borderTop.SetLayoutSize(Size);
            _borderTop.Position = Position;

            _borderBottom.SetLayoutSize(Size);
            _borderBottom.Position = Position;

            _focusRing.SetLayoutSize(Size);
            _focusRing.Position = Position;

            // Icon and caption are laid out as one block and that block is centred.
            //
            // The label keeps the button's full width on purpose. Narrowing it to the text does
            // not survive: CalculateAllPositions ends by normalizing, which stretches every
            // child back to its parent's width - it overwrites the size and leaves the position
            // alone. So the size is left to the normalizer and only the position is used, which
            // is the one thing that holds.
            //
            // A label that centres its text in a box of the button's width therefore only needs
            // shifting by half the icon block to make the two centred together.
            double block = (HasIcon ? IconSize() : 0) + IconGap();
            double textWidth = string.IsNullOrEmpty(Text) ? 0 : _textLabel.MeasureNaturalSize().X;

            _textLabel.SetLayoutSize(Size);
            _textLabel.Position = new PointD(Position.X + block / 2.0, Position.Y);

            _iconLeft = Position.X + Math.Max(0, (Size.X - (block + textWidth)) / 2.0);
        }

        /// <summary>
        /// The button and its parts, and then the icon on top of them.
        ///
        /// Drawn here rather than as a child control on purpose: the button forces every child
        /// to its own size and position so the frame, the emboss and the label all sit exactly
        /// on top of each other, and an icon is the one thing that must not.
        /// </summary>
        public override void GenerateRenderData(ImageSurface surface, Context ctx)
        {
            base.GenerateRenderData(surface, ctx);

            if (!HasIcon)
                return;

            ICoreClientAPI? api = Dialog?.Api;

            if (api == null)
                return;

            double size = IconSize();

            if (size <= 0)
                return;

            // Where LayoutParts put it - the left edge of the icon and caption block.
            double x = _iconLeft;

            double y = Position.Y + (Size.Y - size) / 2.0;

            if (IconAsset != null)
            {
                _icon.Asset = IconAsset;
                _icon.IconName = null;
            }
            else
            {
                _icon.IconName = IconName;
                _icon.Asset = null;
            }

            _icon.Dialog = Dialog;
            _icon.Position = new PointD(x, y);
            _icon.SetLayoutSize(new PointD(size, size));
            _icon.GenerateRenderData(surface, ctx);
        }

        /// <summary>
        /// Draws the icon. Not a child - see GenerateRenderData - but an ImageControl all the
        /// same, so a named icon and an asset are loaded and cached in exactly one place.
        /// </summary>
        private readonly ImageControl _icon = new ImageControl();
    }
}