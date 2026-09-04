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
        public new TextOrientation Orientation { get; set; }
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
            ElementColor? textColor = null,
            TextOrientation orientation = TextOrientation.Left,
            bool wordWrap = false,
            int lineHeight = 20,
            int padding = 0,
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

        /// <summary>Font size in device pixels.</summary>
        private double ScaledFontSize => FontSize * LayoutScale;

        /// <summary>Line height in device pixels.</summary>
        private double ScaledLineHeight => LineHeight * LayoutScale;

        #region Size Calculation
        public override PointD CalculateSize()
        {
            // An explicitly assigned size wins - that is how ButtonControl stretches its label
            // across the whole button.
            //
            // The decision must be made on IsAutoSize, NOT on "Size.X > 0 && Size.Y > 0": a
            // measurement writes its result into Size, so that condition is true from the
            // second layout pass onwards. The label then fell through to the base
            // implementation, which sums up child sizes - and a label has no children, so it
            // collapsed to 0x0 (plus padding) every time the dialog was laid out again.
            //
            // A label that is not auto-sizing but has no size yet (the state ButtonControl
            // creates it in) still has to measure itself, otherwise the button would size
            // itself to a zero-width label.
            //
            // ExplicitSize, not Size: Size is what the arrange pass produced (stretched,
            // clipped), so measuring against it would let the label inherit the width it was
            // stretched to and never shrink back.
            if (!IsAutoSize && ExplicitSize.X > 0 && ExplicitSize.Y > 0)
            {
                CalculatedSize = ScaledExplicitSize;
                SetLayoutSize(CalculatedSize);
                return CalculatedSize;
            }

            PointD measured = MeasureText();

            CalculatedSize = measured;

            // SetLayoutSize, not the Size setter. The setter would record the measurement as
            // ExplicitSize, and from the next pass on this method would take the branch above
            // and never measure again - so the box would keep the size it had when it was last
            // measured while the text keeps being drawn at the current GUI scale.
            SetLayoutSize(measured);

            return measured;
        }

        /// <summary>
        /// The size the text itself needs, whatever the box was stretched to.
        ///
        /// A container that places the label by hand needs this: a button with an icon has to
        /// know how wide the caption is to centre the two together, and it cannot read that off
        /// <see cref="UIControl.Size"/>, which is whatever the button stretched the label to.
        /// </summary>
        public PointD MeasureNaturalSize()
        {
            return MeasureText();
        }

        /// <summary>
        /// Measures the text without touching any state, so that repeated layout passes always
        /// produce the same result.
        /// </summary>
        private PointD MeasureText()
        {
            using (ImageSurface tempSurface = new ImageSurface(Format.Argb32, 1, 1))
            using (Context ctx = new Context(tempSurface))
            {
                SetupFont(ctx);

                // The height of a line, from the font rather than from the size it was asked
                // for. Those are not the same number: a 16 point font puts its ascent above the
                // baseline and its descent below it, and the two together are a good deal more
                // than 16. Measuring with the nominal size is the reason a button came out too
                // short for its own caption and the tail of a "p" hung out of the bottom - the
                // text was drawn correctly, the box around it was simply wrong.
                Cairo.FontExtents fe = ctx.FontExtents;
                double lineHeight = fe.Ascent + fe.Descent;

                if (string.IsNullOrEmpty(Text))
                {
                    return new PointD(ScaledPadding * 2, lineHeight + ScaledPadding * 2);
                }

                if (WordWrap && Size.X > 0)
                {
                    PointD wrappedSize = CalculateWrappedTextSize(ctx, Text, Size.X - (ScaledPadding * 2));
                    return new PointD(Size.X, wrappedSize.Y + (ScaledPadding * 2));
                }

                // XAdvance, not Width: Width is the inked bounding box and leaves out the side
                // bearings, which makes the box too narrow for the text it is supposed to hold.
                TextExtents te = ctx.TextExtents(Text);

                return new PointD(
                    te.XAdvance + (ScaledPadding * 2),
                    lineHeight + (ScaledPadding * 2)
                );
            }
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

                if (te.XAdvance > maxWidth && currentLine.Length > 0)
                {
                    // Line is too long, start new line
                    TextExtents lineTE = ctx.TextExtents(currentLine.ToString());
                    maxLineWidth = Math.Max(maxLineWidth, lineTE.XAdvance);
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
                maxLineWidth = Math.Max(maxLineWidth, lineTE.XAdvance);
                lineCount++;
            }

            return new PointD(maxLineWidth, lineCount * ScaledLineHeight);
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
            ctx.SetFontSize(ScaledFontSize);
        }

        private void DrawSingleLineText(Context ctx)
        {
            TextExtents te = ctx.TextExtents(Text);
            Cairo.FontExtents fe = ctx.FontExtents;

            (double x, double y) = GetTextPosition(te, fe, CapHeight(ctx));

            ctx.MoveTo(x, y);
            ctx.ShowText(Text);
        }

        /// <summary>
        /// How tall a capital letter is, measured from the font in use.
        ///
        /// This is what vertical centring has to be built on, and the reason is Lora: at 24 it
        /// has an ascent of 29 and a descent of 7. Centring the line box those two make - which
        /// is what this did - leaves 17 pixels of air above the letters and 12 below, because
        /// most of the ascent is room for accents that "Save" does not use. The text then sits
        /// visibly high in a button while being, on paper, perfectly centred.
        ///
        /// A capital is used rather than the string's own ink on purpose. Centring each string
        /// by its own extents would put "Save" and "Open" on different baselines, because one of
        /// them has a descender - a row of buttons would have its captions at different heights.
        /// </summary>
        private static double CapHeight(Context ctx)
        {
            return ctx.TextExtents("H").Height;
        }

        // FIXED: Corrected text positioning logic, especially for vertical centering
        private (double x, double y) GetTextPosition(TextExtents te, Cairo.FontExtents fe, double capHeight)
        {
            double x = Position.X;
            double y = Position.Y;

            double baselineOffset = fe.Ascent;

            // The baseline that puts a capital letter in the middle of the box.
            double middleBaseline = Position.Y + (Size.Y / 2) + (capHeight / 2);

            switch (Orientation)
            {
                case TextOrientation.Left:
                case TextOrientation.TopLeft:
                    x = Position.X + ScaledPadding;
                    y = Position.Y + ScaledPadding + baselineOffset;
                    break;

                case TextOrientation.Center:
                case TextOrientation.MiddleCenter:
                    x = Position.X + (Size.X - te.XAdvance) / 2;
                    y = middleBaseline;
                    break;

                case TextOrientation.Right:
                case TextOrientation.TopRight:
                    x = Position.X + Size.X - te.XAdvance - ScaledPadding;
                    y = Position.Y + ScaledPadding + baselineOffset;
                    break;

                case TextOrientation.TopCenter:
                    x = Position.X + (Size.X - te.XAdvance) / 2;
                    y = Position.Y + ScaledPadding + baselineOffset;
                    break;

                case TextOrientation.MiddleLeft:
                    x = Position.X + ScaledPadding;
                    y = middleBaseline;
                    break;

                case TextOrientation.MiddleRight:
                    x = Position.X + Size.X - te.XAdvance - ScaledPadding;
                    y = middleBaseline;
                    break;

                case TextOrientation.BottomLeft:
                    x = Position.X + ScaledPadding;
                    y = Position.Y + Size.Y - ScaledPadding - fe.Descent;
                    break;

                case TextOrientation.BottomCenter:
                    x = Position.X + (Size.X - te.XAdvance) / 2;
                    y = Position.Y + Size.Y - ScaledPadding - fe.Descent;
                    break;

                case TextOrientation.BottomRight:
                    x = Position.X + Size.X - te.XAdvance - ScaledPadding;
                    y = Position.Y + Size.Y - ScaledPadding - fe.Descent;
                    break;
            }

            return (x, y);
        }

        private void DrawWrappedText(Context ctx)
        {
            string[] words = Text.Split(' ');
            StringBuilder currentLine = new StringBuilder();
            Cairo.FontExtents fe = ctx.FontExtents;
            double baselineOffset = fe.Ascent;
            double currentY = Position.Y + ScaledPadding + baselineOffset;
            double maxWidth = Size.X - (ScaledPadding * 2);

            foreach (string word in words)
            {
                string testLine = currentLine.Length > 0
                    ? $"{currentLine} {word}"
                    : word;

                TextExtents te = ctx.TextExtents(testLine);

                if (te.XAdvance > maxWidth && currentLine.Length > 0)
                {
                    // Draw current line and start new one
                    double x = GetWrappedLineX(ctx, currentLine.ToString());
                    ctx.MoveTo(x, currentY);
                    ctx.ShowText(currentLine.ToString());

                    currentY += ScaledLineHeight;
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
                    => Position.X + (Size.X - te.XAdvance) / 2,

                TextOrientation.Right or
                TextOrientation.TopRight or
                TextOrientation.MiddleRight or
                TextOrientation.BottomRight
                    => Position.X + Size.X - te.XAdvance - ScaledPadding,

                _ => Position.X + ScaledPadding
            };
        }
        #endregion
    }
}