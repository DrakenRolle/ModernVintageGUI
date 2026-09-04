using Cairo;
using IS2Mod.Enums;
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

            // Force all children to match button size exactly
            if (_border != null && _textLabel != null)
            {
                _border.SetLayoutSize(this.Size);
                _textLabel.SetLayoutSize(this.Size);
                _textLabel.IsAutoSize = false;
                _borderTop.SetLayoutSize(this.Size);
                _borderBottom.SetLayoutSize(this.Size);
                _focusRing.SetLayoutSize(this.Size);
            }
            return size;
        }

        public override void NormalizeChildrenByDelta()
        {
            if (_border != null && _textLabel != null)
            {
                // Force all sizes to match button size
                _border.SetLayoutSize(this.Size);
                _textLabel.SetLayoutSize(this.Size);
                _borderTop.SetLayoutSize(this.Size);
                _borderBottom.SetLayoutSize(this.Size);
                _focusRing.SetLayoutSize(this.Size);

                // Force all positions to match border position (overlay)
                _border.Position = this.Position;
                _textLabel.Position = _border.Position;
                _borderTop.Position = _border.Position;
                _borderBottom.Position = _border.Position;
                _focusRing.Position = _border.Position;
            }
            base.NormalizeChildrenByDelta();
        }

        public override void CalculateAllPositions()
        {
            base.CalculateAllPositions();

            // Override all positions and sizes after layout
            if (_border != null && _textLabel != null)
            {
                _border.SetLayoutSize(this.Size);
                _border.Position = this.Position;

                _textLabel.SetLayoutSize(this.Size);
                _textLabel.Position = _border.Position;

                _borderTop.SetLayoutSize(this.Size);
                _borderTop.Position = _border.Position;

                _borderBottom.SetLayoutSize(this.Size);
                _borderBottom.Position = _border.Position;

                _focusRing.SetLayoutSize(this.Size);
                _focusRing.Position = _border.Position;
            }
        }

        public override void GenerateRenderData(ImageSurface surface, Context ctx)
        {
            base.GenerateRenderData(surface, ctx);
        }
    }
}