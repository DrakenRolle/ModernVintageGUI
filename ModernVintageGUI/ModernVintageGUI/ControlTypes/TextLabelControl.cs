using Cairo;
using IS2Mod.Enums;
using System;
using System.Linq;
using System.Text;

namespace IS2Mod.ControlTypes
{
    public enum TextOrientation
    {
        Left,
        Center,
        Right,
        TopLeft,
        TopCenter,
        TopRight,
        MiddleLeft,
        MiddleCenter,
        MiddleRight,
        BottomLeft,
        BottomCenter,
        BottomRight
    }

    public class TextLabelControl : UIControl
    {
        #region Properties
        public string Text { get; set; }
        public string FontName { get; set; }
        public int FontSize { get; set; }
        public FontWeight FontWeight { get; set; }
        public FontSlant FontSlant { get; set; }
        public ElementColor TextColor { get; set; }
        public TextOrientation Orientation { get; set; }
        public bool WordWrap { get; set; }
        public int LineHeight { get; set; }
        #endregion

        #region Constructors
        public TextLabelControl(
            string text = "",
            string fontName = "Arial",
            int fontSize = 16,
            FontWeight fontWeight = FontWeight.Normal,
            FontSlant fontSlant = FontSlant.Normal,
            ElementColor textColor = null,
            TextOrientation orientation = TextOrientation.Left,
            bool wordWrap = false,
            int lineHeight = 20,
            int padding = 5,
            string _Name = "",
            PointD? _Size = null,
            Orientation _Orientation = Enums.Orientation.Top,
            double _Margin = 0,
            double _Padding = 0,
            int _Index = 0)
            : base(_Name, _Size, _Orientation, _Margin, _Padding, _Index)
        {
            Text = text;
            FontName = fontName;
            FontSize = fontSize;
            FontWeight = fontWeight;
            FontSlant = fontSlant;
            TextColor = textColor ?? ElementColor.White;
            Orientation = orientation;
            WordWrap = wordWrap;
            LineHeight = lineHeight;
            Padding = padding;
        }

        public TextLabelControl() : base()
        {
            Text = "";
            FontName = "Arial";
            FontSize = 16;
            FontWeight = FontWeight.Normal;
            FontSlant = FontSlant.Normal;
            TextColor = ElementColor.Black;
            Orientation = TextOrientation.Left;
            WordWrap = false;
            LineHeight = 20;
            Padding = 5;
        }
        #endregion

        #region Size Calculation
        public override PointD CalculateSize()
        {
            // If size is explicitly set, use it
            if (Size.X > 0 && Size.Y > 0)
            {
                return base.CalculateSize();
            }

            // If no text, return minimum size
            if (string.IsNullOrEmpty(Text))
            {
                Size = new PointD(Padding * 2, Padding * 2 + FontSize);
                return Size;
            }

            // Measure text with Cairo
            using (ImageSurface tempSurface = new ImageSurface(Format.Argb32, 1, 1))
            using (Context ctx = new Context(tempSurface))
            {
                SetupFont(ctx);

                if (WordWrap && Size.X > 0)
                {
                    // Calculate wrapped text size
                    PointD wrappedSize = CalculateWrappedTextSize(ctx, Text, Size.X - (Padding * 2));
                    Size = new PointD(Size.X, wrappedSize.Y + (Padding * 2));
                }
                else
                {
                    // Calculate single-line text size
                    TextExtents te = ctx.TextExtents(Text);
                    Size = new PointD(
                        te.Width + (Padding * 2),
                        FontSize + (Padding * 2)
                    );
                }
            }

            return Size;
        }

        private PointD CalculateWrappedTextSize(Context ctx, string text, double maxWidth)
        {
            string[] words = text.Split(' ');
            StringBuilder currentLine = new StringBuilder();
            int lineCount = 0;
            double maxLineWidth = 0;

            foreach (string word in words)
            {
                string testLine = currentLine.Length > 0
                    ? $"{currentLine} {word}"
                    : word;

                TextExtents te = ctx.TextExtents(testLine);

                if (te.Width > maxWidth && currentLine.Length > 0)
                {
                    // Line is too long, start new line
                    TextExtents lineTE = ctx.TextExtents(currentLine.ToString());
                    maxLineWidth = Math.Max(maxLineWidth, lineTE.Width);
                    lineCount++;

                    currentLine.Clear();
                    currentLine.Append(word);
                }
                else
                {
                    currentLine.Append(currentLine.Length > 0 ? $" {word}" : word);
                }
            }

            // Add the last line
            if (currentLine.Length > 0)
            {
                TextExtents lineTE = ctx.TextExtents(currentLine.ToString());
                maxLineWidth = Math.Max(maxLineWidth, lineTE.Width);
                lineCount++;
            }

            return new PointD(maxLineWidth, lineCount * LineHeight);
        }
        #endregion

        #region Rendering
        public override void GenerateRenderData(ImageSurface surface, Context ctx)
        {
            if (string.IsNullOrEmpty(Text))
                return;

            SetupFont(ctx);
            ctx.SetSourceRGBA(
                TextColor.RNormalized,
                TextColor.GNormalized,
                TextColor.BNormalized,
                TextColor.ANormalized);

            if (WordWrap)
            {
                DrawWrappedText(ctx);
            }
            else
            {
                DrawSingleLineText(ctx);
            }

            base.GenerateRenderData(surface, ctx);
        }

        private void SetupFont(Context ctx)
        {
            ctx.SelectFontFace(FontName, FontSlant, FontWeight);
            ctx.SetFontSize(FontSize);
        }

        private void DrawSingleLineText(Context ctx)
        {
            TextExtents te = ctx.TextExtents(Text);
            double baseY = FontSize * 0.8;

            (double x, double y) = GetTextPosition(te, baseY);

            ctx.MoveTo(x, y);
            ctx.ShowText(Text);
        }

        private (double x, double y) GetTextPosition(TextExtents te, double baseY)
        {
            double x = Position.X;
            double y = Position.Y;

            switch (Orientation)
            {
                case TextOrientation.Left:
                case TextOrientation.TopLeft:
                    x = Position.X + Padding;
                    y = Position.Y + Padding + baseY;
                    break;

                case TextOrientation.Center:
                case TextOrientation.MiddleCenter:
                    x = Position.X + (Size.X - te.Width) / 2;
                    y = Position.Y + Size.Y / 2 + baseY / 2;
                    break;

                case TextOrientation.Right:
                case TextOrientation.TopRight:
                    x = Position.X + Size.X - te.Width - Padding;
                    y = Position.Y + Padding + baseY;
                    break;

                case TextOrientation.TopCenter:
                    x = Position.X + (Size.X - te.Width) / 2;
                    y = Position.Y + Padding + baseY;
                    break;

                case TextOrientation.MiddleLeft:
                    x = Position.X + Padding;
                    y = Position.Y + Size.Y / 2 + baseY / 2;
                    break;

                case TextOrientation.MiddleRight:
                    x = Position.X + Size.X - te.Width - Padding;
                    y = Position.Y + Size.Y / 2 + baseY / 2;
                    break;

                case TextOrientation.BottomLeft:
                    x = Position.X + Padding;
                    y = Position.Y + Size.Y - Padding;
                    break;

                case TextOrientation.BottomCenter:
                    x = Position.X + (Size.X - te.Width) / 2;
                    y = Position.Y + Size.Y - Padding;
                    break;

                case TextOrientation.BottomRight:
                    x = Position.X + Size.X - te.Width - Padding;
                    y = Position.Y + Size.Y - Padding;
                    break;
            }

            return (x, y);
        }

        private void DrawWrappedText(Context ctx)
        {
            string[] words = Text.Split(' ');
            StringBuilder currentLine = new StringBuilder();
            double baseY = FontSize * 0.8;
            double currentY = Position.Y + Padding + baseY;
            double maxWidth = Size.X - (Padding * 2);

            foreach (string word in words)
            {
                string testLine = currentLine.Length > 0
                    ? $"{currentLine} {word}"
                    : word;

                TextExtents te = ctx.TextExtents(testLine);

                if (te.Width > maxWidth && currentLine.Length > 0)
                {
                    // Draw current line and start new one
                    double x = GetWrappedLineX(ctx, currentLine.ToString());
                    ctx.MoveTo(x, currentY);
                    ctx.ShowText(currentLine.ToString());

                    currentY += LineHeight;
                    currentLine.Clear();
                    currentLine.Append(word);

                    // Stop if we've exceeded the control's height
                    if (currentY > Position.Y + Size.Y)
                        break;
                }
                else
                {
                    currentLine.Append(currentLine.Length > 0 ? $" {word}" : word);
                }
            }

            // Draw the last line
            if (currentLine.Length > 0 && currentY <= Position.Y + Size.Y)
            {
                double x = GetWrappedLineX(ctx, currentLine.ToString());
                ctx.MoveTo(x, currentY);
                ctx.ShowText(currentLine.ToString());
            }
        }

        private double GetWrappedLineX(Context ctx, string line)
        {
            TextExtents te = ctx.TextExtents(line);

            // For wrapped text, only support horizontal alignment
            return Orientation switch
            {
                TextOrientation.Center or
                TextOrientation.TopCenter or
                TextOrientation.MiddleCenter or
                TextOrientation.BottomCenter
                    => Position.X + (Size.X - te.Width) / 2,

                TextOrientation.Right or
                TextOrientation.TopRight or
                TextOrientation.MiddleRight or
                TextOrientation.BottomRight
                    => Position.X + Size.X - te.Width - Padding,

                _ => Position.X + Padding
            };
        }
        #endregion
    }
}
