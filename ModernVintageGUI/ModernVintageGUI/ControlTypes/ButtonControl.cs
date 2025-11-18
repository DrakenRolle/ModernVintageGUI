using Cairo;
using IS2Mod.Enums;
using System.Diagnostics;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace IS2Mod.ControlTypes
{
    public class ButtonControl : UIControl
    {
        private RectangleControl _border;
        private RectangleControl _borderLeft;
        private RectangleControl _borderRight;
        private RectangleControl _borderTop;
        private RectangleControl _borderBottom;

        private TextLabelControl _textLabel;
        private bool _isPressed = false;
        private bool _isHovered = false;

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

        private void InitializeComponents()
        {
            _border = new RectangleControl(
                _Orientation: Orientation.Top
                
            );

            // FIXED: Removed margin from border overlays - they should align with the button bounds
            _borderBottom = new RectangleControl(
                borderWidth: 3,
                _Margin: 0,  // Changed from 10 to 0
                borderColor: new ElementColor(new double[] { 0.0, 0.0, 0.0, 0.3 }),
                _Orientation: Orientation.None,  // Changed to None so it overlays
                hiddenBorders: new RectangleBorderStyle[] { RectangleBorderStyle.Left, RectangleBorderStyle.Top }
            );

            _borderTop = new RectangleControl(
                borderWidth: 4,
                _Margin: 0,  // Changed from 10 to 0
                borderColor: new ElementColor(new double[] { 1.0, 1.0, 1.0, 0.3 }),
                _Orientation: Orientation.None,  // Changed to None so it overlays
                hiddenBorders: new RectangleBorderStyle[] { RectangleBorderStyle.Bottom, RectangleBorderStyle.Right }, blurEdgeWidth: 3,blurRange: 3
            );

            Children.Add(_border);

            // Create text label - will auto-size initially, then fill border
            var buttonFont = CairoFont.ButtonText();
            _textLabel = new TextLabelControl(
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
            _textLabel.IsAutoSize = false;
            _border.Children.Add(_borderTop);
            _border.Children.Add(_borderBottom);


            _border.Children.Add(_textLabel);

            this.Clicked += ButtonControl_Clicked;
            this.Enter += ButtonControl_Enter;
            this.Exit += ButtonControl_Exit;
            this.MouseDown += ButtonControl_MouseDown;
            this.MouseUp += ButtonControl_MouseUp;
        }

        private void ButtonControl_MouseUp(object? sender, Events.MouseEventArgs e)
        {
            _borderBottom.BorderColor.A = (byte)(0.4 * 255);
            _borderBottom.BorderWidth = 4;

            _border.BackgroundColor.A = (byte)(0.1 * 255);
            _border.BlurEdgeWidth = 3;
            _border.BlurRange = 3;
            Dialog.Refresh();

        }

        private void ButtonControl_MouseDown(object? sender, Events.MouseEventArgs e)
        {
            _borderBottom.BorderColor.A = (byte)(0.4 * 255);
            _borderBottom.BorderWidth = 4;

            _border.BackgroundColor.A = (byte)(0.3 * 255);
            _border.BlurEdgeWidth = 3;
            _border.BlurRange = 3;
            Dialog.Refresh();

        }

        private void ButtonControl_Exit(object? sender, Events.MouseEventArgs e)
        {
            _borderBottom.BorderColor.A = (byte)(0.3 * 255);
            _border.BackgroundColor.A = (byte)(0.0 * 255);

            _borderBottom.BorderWidth = 3;
            _border.BlurEdgeWidth = 3;
            _border.BlurRange = 0;
            Dialog.Refresh();
            Debug.WriteLine("Button Exit");
        }

        private void ButtonControl_Enter(object? sender, Events.MouseEventArgs e)
        {
            _borderBottom.BorderColor.A = (byte)(0.4 * 255);
            _borderBottom.BorderWidth = 4;

            _border.BackgroundColor.A = (byte)(0.1 * 255);
            _border.BlurEdgeWidth = 3;
            _border.BlurRange = 3;


            Dialog.Refresh();
            Debug.WriteLine("Button Enter");
        }

        private void ButtonControl_Clicked(object? sender, Events.MouseEventArgs e)
        {
            Debug.WriteLine("Button Clicked");
        }

        protected override UIControl HitTestRecursive(UIControl control, double relativeX, double relativeY)
        {
            // Check if point is within this control's bounds
            if (!IsPointInDialog((int)relativeX, (int)relativeY))
            {
                return null;
            }

            // No children contain the point, so this control is the hit target
            // Don't return the dialog itself as a hit target
            return control;
        }
        public override PointD CalculateSize()
        {
            if(IsAutoSize == false)
            {
                // If not auto-sizing, just return current size
                return base.Size;
            }
            // Let base calculate size normally
            PointD size = base.CalculateSize();

            // Force all children to match button size exactly
            if (_border != null && _textLabel != null)
            {
                _border.Size = this.Size;
                _textLabel.Size = this.Size;
                _textLabel.IsAutoSize = false;
                _borderTop.Size = this.Size;
                _borderBottom.Size = this.Size;
            }
            return size;
        }

        public override void NormalizeChildrenByDelta()
        {
            if (_border != null && _textLabel != null)
            {
                // Force all sizes to match button size
                _border.Size = this.Size;
                _textLabel.Size = this.Size;
                _borderTop.Size = this.Size;
                _borderBottom.Size = this.Size;

                // Force all positions to match border position (overlay)
                _border.Position = this.Position;
                _textLabel.Position = _border.Position;
                _borderTop.Position = _border.Position;
                _borderBottom.Position = _border.Position;
            }
            base.NormalizeChildrenByDelta();
        }

        public override void CalculateAllPositions()
        {
            base.CalculateAllPositions();

            // Override all positions and sizes after layout
            if (_border != null && _textLabel != null)
            {
                _border.Size = this.Size;
                _border.Position = this.Position;

                _textLabel.Size = this.Size;
                _textLabel.Position = _border.Position;

                _borderTop.Size = this.Size;
                _borderTop.Position = _border.Position;

                _borderBottom.Size = this.Size;
                _borderBottom.Position = _border.Position;
            }
        }

        public override void GenerateRenderData(ImageSurface surface, Context ctx)
        {
            base.GenerateRenderData(surface, ctx);
        }
    }
}