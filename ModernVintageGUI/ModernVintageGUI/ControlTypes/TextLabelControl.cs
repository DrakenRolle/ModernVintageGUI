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
        /// <summary>
        /// Where the text sits inside the label's own box.
        ///
        /// Not to be confused with <see cref="UIControl.Orientation"/>, which is where the label
        /// itself sits inside its parent - this used to be called Orientation as well and hid
        /// that one, which meant a label was the one control that could not be aligned in its
        /// container. Two different questions, two different names.
        /// </summary>
        public TextOrientation TextAlign { get; set; }

        /// <summary>The smallest font size <see cref="TextAutoSize"/> will shrink to, in author units.</summary>
        public const double UnscaledMinFontSize = 6.0;

        private bool _textAutoSize;

        /// <summary>
        /// Whether the text gives way when its box is too small for it. Off by default, and
        /// then it is the other way round: a label with an explicit <see cref="UIControl.Size"/>
        /// that is too small for its text is measured at the text's size instead, so the box -
        /// and everything laid out around it - is never smaller than what it shows.
        ///
        /// On, the box stays what it was told and the text is drawn at the largest font size at
        /// which it fits, never below <see cref="UnscaledMinFontSize"/>. A wrapped label shrinks
        /// until its lines fit the height. Not to be confused with <see cref="UIControl.IsAutoSize"/>:
        /// that lets the box follow the text, this lets the text follow the box.
        /// </summary>
        public bool TextAutoSize
        {
            get => _textAutoSize;
            set
            {
                if (SetProperty(ref _textAutoSize, value))
                    InvalidateLayout();
            }
        }

        /// <summary>
        /// The font size the text is actually drawn at, in author units. The same as
        /// <see cref="FontSize"/> unless <see cref="TextAutoSize"/> shrank it. Read it after a
        /// layout pass, because it depends on the box the label ended up with.
        /// </summary>
        public double EffectiveFontSize => LayoutScale > 0 ? FitFontSize(MeasureContext()) / LayoutScale : FontSize;
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
            TextAlign = orientation;
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
            TextAlign = TextOrientation.Left;
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
                PointD box = ScaledExplicitSize;

                // Off by default, the box is never smaller than its text: whatever it was told,
                // it is at least what the text measures to. Measured at the box's own width
                // first, which for wrapped text is the width the lines are broken at. With
                // TextAutoSize on it is the text that gives way, when it is drawn.
                if (!TextAutoSize)
                {
                    SetLayoutSize(box);
                    PointD needed = MeasureText();
                    box = new PointD(Math.Max(box.X, needed.X), Math.Max(box.Y, needed.Y));
                }

                CalculatedSize = box;
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

        #region Measurement cache
        /// <summary>
        /// The one surface every measurement is taken on.
        ///
        /// Measuring a string needs a Cairo context and nothing else - it never draws - so a
        /// single pixel surface is enough and there is no reason to build a new one per call.
        /// That is what it used to do, and at a thousand measurements per layout pass the
        /// allocation was most of the cost of laying the dialog out.
        ///
        /// One per thread, because a Cairo context is not thread safe and the layout harness
        /// may run passes off the main thread. In the game everything measured here happens on
        /// the render thread anyway.
        /// </summary>
        [ThreadStatic]
        private static Context? _measureContext;

        internal static Context MeasureContext()
        {
            if (_measureContext != null)
                return _measureContext;

            // Deliberately never disposed: it lives as long as the thread does, and a
            // one pixel surface plus its context is a few dozen bytes.
            var surface = new ImageSurface(Format.Argb32, 1, 1);

            _measureContext = new Context(surface);

            return _measureContext;
        }

        /// <summary>Everything the measured size depends on, as of the last measurement.</summary>
        private string? _measuredText;
        private string? _measuredFontName;
        private int _measuredFontSize;
        private FontWeight _measuredWeight;
        private FontSlant _measuredSlant;
        private bool _measuredWordWrap;
        private double _measuredWrapWidth;
        private double _measuredScale;
        private double _measuredPadding;
        private PointD _measuredSize;
        private bool _hasMeasurement;

        /// <summary>
        /// True when nothing that could change the answer has changed since the last
        /// measurement.
        ///
        /// The text of a dialog barely ever changes, and the layout runs on every hover, every
        /// selection and every scroll - so almost every measurement asks a question that was
        /// already answered. The properties are plain fields with no change notification, hence
        /// comparing them rather than invalidating from a setter.
        /// </summary>
        private bool MeasurementIsCurrent()
        {
            return _hasMeasurement
                && _measuredFontSize == FontSize
                && _measuredWeight == FontWeight
                && _measuredSlant == FontSlant
                && _measuredWordWrap == WordWrap
                && _measuredScale == LayoutScale
                && _measuredPadding == Padding
                && string.Equals(_measuredText, Text, StringComparison.Ordinal)
                && string.Equals(_measuredFontName, FontName, StringComparison.Ordinal)

                // Only the wrapping case looks at the box it is given, so only that case may be
                // invalidated by it - otherwise every stretch of the label would re-measure it.
                && (!WordWrap || _measuredWrapWidth == WrapWidth);
        }

        /// <summary>
        /// The width wrapped text is broken at. An auto sizing label that was given a width - a
        /// paragraph - wraps at that width scaled, like every other authored dimension; any other
        /// label wraps at whatever box the layout gave it.
        /// </summary>
        private double WrapWidth => IsAutoSize && ExplicitSize.X > 0 ? ScaledExplicitSize.X : Size.X;

        private void RememberMeasurement(PointD size)
        {
            _measuredText = Text;
            _measuredFontName = FontName;
            _measuredFontSize = FontSize;
            _measuredWeight = FontWeight;
            _measuredSlant = FontSlant;
            _measuredWordWrap = WordWrap;
            _measuredWrapWidth = WrapWidth;
            _measuredScale = LayoutScale;
            _measuredPadding = Padding;
            _measuredSize = size;
            _hasMeasurement = true;
        }
        #endregion

        /// <summary>
        /// Measures the text without touching any state, so that repeated layout passes always
        /// produce the same result.
        /// </summary>
        private PointD MeasureText()
        {
            if (MeasurementIsCurrent())
            {
                IS2Mod.Diagnostics.UIProfiler.Count("text   MeasureText (cached)");
                return _measuredSize;
            }

            IS2Mod.Diagnostics.UIProfiler.Scope profileScope = IS2Mod.Diagnostics.UIProfiler.Begin();

            try
            {
                PointD measured = MeasureTextCore();

                RememberMeasurement(measured);

                return measured;
            }
            finally
            {
                IS2Mod.Diagnostics.UIProfiler.End("text   MeasureText (Cairo)", profileScope);
            }
        }

        private PointD MeasureTextCore()
        {
            Context ctx = MeasureContext();

            SetupFont(ctx);

            // The height of a line, from the font rather than from the size it was asked for.
            // Those are not the same number: a 16 point font puts its ascent above the baseline
            // and its descent below it, and the two together are a good deal more than 16.
            // Measuring with the nominal size is the reason a button came out too short for its
            // own caption and the tail of a "p" hung out of the bottom - the text was drawn
            // correctly, the box around it was simply wrong.
            Cairo.FontExtents fe = ctx.FontExtents;
            double lineHeight = fe.Ascent + fe.Descent;

            if (string.IsNullOrEmpty(Text))
            {
                return new PointD(ScaledPadding * 2, lineHeight + ScaledPadding * 2);
            }

            double wrapWidth = WrapWidth;

            if (WordWrap && wrapWidth > 0)
            {
                PointD wrappedSize = CalculateWrappedTextSize(ctx, Text, wrapWidth - (ScaledPadding * 2), ScaledLineHeight);
                return new PointD(wrapWidth, wrappedSize.Y + (ScaledPadding * 2));
            }

            // XAdvance, not Width: Width is the inked bounding box and leaves out the side
            // bearings, which makes the box too narrow for the text it is supposed to hold.
            TextExtents te = ctx.TextExtents(Text);

            return new PointD(
                te.XAdvance + (ScaledPadding * 2),
                lineHeight + (ScaledPadding * 2)
            );
        }

        /// <summary>
        /// The box the text takes when broken into lines no wider than <paramref name="maxWidth"/>,
        /// at the font the context has selected and <paramref name="lineHeight"/> per line.
        /// </summary>
        private static PointD CalculateWrappedTextSize(Context ctx, string text, double maxWidth, double lineHeight)
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

            return new PointD(maxLineWidth, lineCount * lineHeight);
        }
        #endregion

        #region Fitting the text into the box
        // What the fitted size was last worked out for. The box is part of the key here, unlike
        // the measurement above: the answer is about this text in this box.
        private bool _hasFit;
        private string? _fitText;
        private string? _fitFontName;
        private FontWeight _fitWeight;
        private FontSlant _fitSlant;
        private bool _fitWordWrap;
        private double _fitNominal;
        private double _fitWidth;
        private double _fitHeight;
        private double _fitResult;

        /// <summary>
        /// The font size the text is drawn at, in device pixels: the nominal one, or with
        /// <see cref="TextAutoSize"/> the largest size at which the text fits the box, never
        /// below <see cref="UnscaledMinFontSize"/>. Leaves the context's font where it likes.
        /// </summary>
        private double FitFontSize(Context ctx)
        {
            double nominal = ScaledFontSize;

            if (!TextAutoSize || nominal <= 0 || string.IsNullOrEmpty(Text))
                return nominal;

            double availableWidth = Size.X - ScaledPadding * 2;
            double availableHeight = Size.Y - ScaledPadding * 2;

            // No box yet - before the first layout - is not a box that is too small.
            if (availableWidth <= 0 || availableHeight <= 0)
                return nominal;

            if (_hasFit
                && _fitNominal == nominal
                && _fitWidth == availableWidth
                && _fitHeight == availableHeight
                && _fitWordWrap == WordWrap
                && _fitWeight == FontWeight
                && _fitSlant == FontSlant
                && string.Equals(_fitText, Text, StringComparison.Ordinal)
                && string.Equals(_fitFontName, FontName, StringComparison.Ordinal))
            {
                return _fitResult;
            }

            double minimum = Math.Min(nominal, UnscaledMinFontSize * LayoutScale);
            double size = nominal;

            ctx.SelectFontFace(FontName, FontSlant, FontWeight);

            if (!WordWrap)
            {
                // Glyph advances scale with the font size, so one ratio says how far down the
                // text has to go on each axis; the smaller of the two wins.
                ctx.SetFontSize(nominal);

                TextExtents te = ctx.TextExtents(Text);
                Cairo.FontExtents fe = ctx.FontExtents;
                double width = te.XAdvance;
                double height = fe.Ascent + fe.Descent;
                double factor = 1.0;

                if (width > availableWidth && width > 0)
                    factor = Math.Min(factor, availableWidth / width);

                if (height > availableHeight && height > 0)
                    factor = Math.Min(factor, availableHeight / height);

                if (factor < 1.0)
                    size = Math.Max(minimum, Math.Floor(nominal * factor));
            }
            else
            {
                // Wrapped text has no single ratio - a smaller font also means fewer lines - so
                // it steps down until the lines fit the height and the longest word the width.
                double step = Math.Max(1.0, LayoutScale);

                while (size > minimum)
                {
                    ctx.SetFontSize(size);

                    PointD wrapped = CalculateWrappedTextSize(ctx, Text, availableWidth, ScaledLineHeight * size / nominal);

                    if (wrapped.X <= availableWidth && wrapped.Y <= availableHeight)
                        break;

                    size = Math.Max(minimum, size - step);
                }
            }

            _fitText = Text;
            _fitFontName = FontName;
            _fitWeight = FontWeight;
            _fitSlant = FontSlant;
            _fitWordWrap = WordWrap;
            _fitNominal = nominal;
            _fitWidth = availableWidth;
            _fitHeight = availableHeight;
            _fitResult = size;
            _hasFit = true;

            return size;
        }
        #endregion

        #region Rendering
        public override void GenerateRenderData(ImageSurface surface, Context ctx)
        {
            if (string.IsNullOrEmpty(Text))
                return;

            // The nominal size, or what TextAutoSize brought it down to for this box.
            double fontSize = FitFontSize(ctx);

            ctx.SelectFontFace(FontName, FontSlant, FontWeight);
            ctx.SetFontSize(fontSize);
            ctx.SetSourceRGBA(
                TextColor.RNormalized,
                TextColor.GNormalized,
                TextColor.BNormalized,
                TextColor.ANormalized);

            if (WordWrap)
            {
                // The lines close up in step with the glyphs.
                double lineHeight = ScaledFontSize > 0 ? ScaledLineHeight * fontSize / ScaledFontSize : ScaledLineHeight;

                DrawWrappedText(ctx, lineHeight);
            }
            else
            {
                DrawSingleLineText(ctx, fontSize);
            }

            base.GenerateRenderData(surface, ctx);
        }

        /// <summary>The font at its nominal size, for measuring.</summary>
        private void SetupFont(Context ctx)
        {
            ctx.SelectFontFace(FontName, FontSlant, FontWeight);
            ctx.SetFontSize(ScaledFontSize);
        }

        private void DrawSingleLineText(Context ctx, double fontSize)
        {
            EnsureExtents(ctx, fontSize);

            (double x, double y) = GetTextPosition(_glyphExtents, _lineExtents, CapHeightCached(ctx, fontSize));

            ctx.MoveTo(x, y);
            ctx.ShowText(Text);
        }

        #region Draw metrics cache
        // Everything the glyph metrics depend on, as of the last time they were taken. The box
        // the label sits in is deliberately not in here: the box decides where the text goes,
        // which is arithmetic, not where the glyphs are relative to the baseline, which is the
        // expensive part.
        private string? _extentsText;
        private string? _extentsFontName;
        private double _extentsFontSize;
        private FontWeight _extentsWeight;
        private FontSlant _extentsSlant;
        private TextExtents _glyphExtents;
        private Cairo.FontExtents _lineExtents;
        private bool _hasExtents;

        /// <summary>
        /// Takes the glyph and line metrics of the current text, or reuses the ones already
        /// taken.
        ///
        /// <c>Context.TextExtents</c> is not a lookup, it shapes the string - and it was being
        /// called once per label on every redraw, which is every time anything in the dialog is
        /// hovered. On the showcase that was 1.5 of the 9.6 ms a redraw cost, asked 71 times for
        /// 71 answers that had not changed since the dialog was opened. Drawing the glyphs
        /// afterwards costs about the same again, and that part is real work.
        ///
        /// Cached per label rather than in a table keyed by the string, because a label that
        /// does change its text - a counter - would otherwise fill that table up forever.
        /// </summary>
        private void EnsureExtents(Context ctx, double fontSize)
        {
            if (_hasExtents
                && _extentsFontSize == fontSize
                && _extentsWeight == FontWeight
                && _extentsSlant == FontSlant
                && string.Equals(_extentsText, Text, StringComparison.Ordinal)
                && string.Equals(_extentsFontName, FontName, StringComparison.Ordinal))
            {
                IS2Mod.Diagnostics.UIProfiler.Count("text   extents (cached)");
                return;
            }

            IS2Mod.Diagnostics.UIProfiler.Scope scope = IS2Mod.Diagnostics.UIProfiler.Begin();

            // The font has to be selected on this context already - SetupFont runs first, and
            // these two answers are about that font.
            _glyphExtents = ctx.TextExtents(Text);
            _lineExtents = ctx.FontExtents;

            IS2Mod.Diagnostics.UIProfiler.End("text   extents (Cairo)", scope);

            _extentsText = Text;
            _extentsFontName = FontName;
            _extentsFontSize = fontSize;
            _extentsWeight = FontWeight;
            _extentsSlant = FontSlant;
            _hasExtents = true;
        }
        #endregion

        /// <summary>
        /// Cap heights already measured, by font. One entry per font, size, weight and slant in
        /// use, which for a dialog is a handful.
        ///
        /// It is worth a cache because it is measured *per drawn label per redraw* - a text
        /// extent call on every label of the dialog, every time anything is hovered, for a
        /// number that only depends on the font. Keyed by the scaled size rather than the
        /// authored one, because that is what Cairo is actually asked for.
        /// </summary>
        private static readonly System.Collections.Generic.Dictionary<(string, double, FontWeight, FontSlant), double>
            CapHeights = new System.Collections.Generic.Dictionary<(string, double, FontWeight, FontSlant), double>();

        private double CapHeightCached(Context ctx, double fontSize)
        {
            var key = (FontName, fontSize, FontWeight, FontSlant);

            if (CapHeights.TryGetValue(key, out double cached))
                return cached;

            double measured = CapHeight(ctx);

            CapHeights[key] = measured;

            return measured;
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

            switch (TextAlign)
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

        private void DrawWrappedText(Context ctx, double lineHeight)
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

                    currentY += lineHeight;
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
            return TextAlign switch
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