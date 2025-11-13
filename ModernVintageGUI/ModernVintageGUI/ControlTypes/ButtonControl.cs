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
        private TextLabelControl _textLabel;

        public string Text
        {
            get => _textLabel.Text;
            set => _textLabel.Text = value;
        }

        public ButtonControl(
            string _Name = "",
            PointD? _Size = null,
            Orientation _Orientation = Orientation.Top,
            double _Margin = 0,
            double _Padding = 0,
            int _Index = 0)
            : base(_Name, _Size, _Orientation, _Margin, _Padding, _Index)
        {
            InitializeComponents();
        }

        private void InitializeComponents()
        {
            // Create border with None orientation so text can position freely
            _border = new RectangleControl(
                borderWidth: 3,
                roundedCorners: 7,
                borderColor: ElementColor.White,
                _Orientation: Orientation.None
            );
            _border.Padding = 10;
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
                _Margin: 0
            );
            _border.Children.Add(_textLabel);

            this.Clicked += ButtonControl_Clicked;
            this.Enter += ButtonControl_Enter;
            this.Exit += ButtonControl_Exit;
        }

        private void ButtonControl_Exit(object? sender, Events.MouseEventArgs e)
        {
            Debug.WriteLine("Button Exit");
        }

        private void ButtonControl_Enter(object? sender, Events.MouseEventArgs e)
        {
            Debug.WriteLine("Button Enter");
        }

        private void ButtonControl_Clicked(object? sender, Events.MouseEventArgs e)
        {
            
        }




        protected override UIControl HitTestRecursive(UIControl control, double relativeX, double relativeY)
        {
            // Check if point is within this control's bounds
            if (!IsPointInControl(control, relativeX, relativeY))
            {
                return null;
            }

            // No children contain the point, so this control is the hit target
            // Don't return the dialog itself as a hit target
            return control;
        }
        public override PointD CalculateSize()
        {
            // Let base calculate size normally
            PointD size = base.CalculateSize();

            // After sizing, set text to fill border's content area for proper centering
            if (_border != null && _textLabel != null)
            {
                double contentWidth = _border.Size.X - (_border.Padding * 2);
                double contentHeight = _border.Size.Y - (_border.Padding * 2);

                _textLabel.Size = new PointD(contentWidth, contentHeight);
                _textLabel.IsAutoSize = false;
            }            
            return size;
        }

        public override void CalculateAllPositions()
        {
            // Store the manually set text size before positions are calculated
            PointD savedTextSize = _textLabel.Size;

            // Let base calculate positions (this will overwrite TextLabel.Size)
            base.CalculateAllPositions();

            // Restore the text size we want
            if (_border != null && _textLabel != null && !_textLabel.IsAutoSize)
            {
                _textLabel.Size = savedTextSize;
            }
        }

        public override void GenerateRenderData(ImageSurface surface, Context ctx)
        {
            base.GenerateRenderData(surface, ctx);
        }
    }
}