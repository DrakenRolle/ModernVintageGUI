using Cairo;
using IS2Mod.ControlTypes;
using IS2Mod.ControlTypes.Events;
using IS2Mod.Enums;
using System;
using System.Text;
using Vintagestory.API.Client;

namespace ModernVintageGUI.ControlTypes
{
    /// <summary>
    /// A single line text field: a search box, a name, a number.
    ///
    /// Characters come from <see cref="UIControl.KeyPress"/>, which exists only because
    /// <see cref="IS2Mod.Patches.ClientMainKeyPressPatch"/> puts them there - the game does not
    /// offer typed characters to anything that is not one of its own dialogs. That is why this
    /// control could not be written before that patch, and why it is the one control here that
    /// needs one.
    ///
    /// It asks for every key while it is focused (<see cref="WantsAllKeyboardInput"/>), so
    /// pressing E does not open the inventory in the middle of a word. Escape still leaves,
    /// because a dialog that cannot be closed with Escape is a trap.
    ///
    /// Editing is what a text field anywhere else does: the caret goes where the mouse clicks,
    /// a drag or Shift with the arrow keys selects, Ctrl with the arrows jumps a word, a double
    /// click takes the word, Ctrl+A everything, and Ctrl+X, Ctrl+C and Ctrl+V go through the
    /// game's clipboard. Text longer than the box scrolls sideways so the caret stays in view;
    /// nothing is ever drawn outside the frame.
    ///
    /// The caret blinks without redrawing anything: the dialog surface holds the text, and the
    /// caret is a two pixel texture the interactive pass draws on top of it on the frames it is
    /// on. Where there is no interactive pass - the layout harness, the documentation pictures -
    /// it is drawn solid into the surface instead.
    /// </summary>
    public class TextInputControl : UIControl, IDisposable
    {
        #region Vanilla styling
        /// <summary>GuiElementTextInput sinks its box the way a dropdown does.</summary>
        private const double BoxFillAlpha = 0.2;
        private const double BoxCornerRadius = 1.0;

        /// <summary>Room between the frame and the text.</summary>
        private const double UnscaledTextPadding = 6.0;

        /// <summary>How wide the caret is drawn.</summary>
        private const double UnscaledCaretWidth = 1.5;

        /// <summary>How much of the highlight colour the selection shows through the text.</summary>
        private const double SelectionAlpha = 0.45;

        /// <summary>One on and off of the caret, in seconds.</summary>
        private const float BlinkPeriod = 1.0f;

        /// <summary>Two presses on the same character closer together than this are a double click.</summary>
        private const long DoubleClickMilliseconds = 400;

        public const double UnscaledDefaultHeight = 30.0;
        private const double UnscaledDefaultWidth = 200.0;

        private const int FontSize = 16;
        #endregion

        #region Properties
        private string _text = "";

        /// <summary>Where the caret is: the number of characters in front of it.</summary>
        private int _caret;

        /// <summary>
        /// The other end of the selection. Equal to the caret when nothing is selected; the
        /// selection is whatever lies between the two, whichever way round they are.
        /// </summary>
        private int _anchor;

        /// <summary>
        /// What is in the field. Setting it puts the caret at the end, drops the selection and
        /// raises <see cref="TextChanged"/>, so a handler sees a change from code like one from
        /// typing.
        /// </summary>
        public string Text
        {
            get => _text;
            set => SetText(value ?? "", caret: (value ?? "").Length);
        }

        /// <summary>Shown in place of the text while the field is empty.</summary>
        public string PlaceholderText
        {
            get => _placeholder;
            set
            {
                _placeholder = value ?? "";
                UpdateLabel();
            }
        }

        /// <summary>How many characters fit. 0 - the default - means no limit.</summary>
        public int MaxLength { get; set; }

        private bool _isPassword;

        /// <summary>Show dots instead of the text. A password is not copied to the clipboard either.</summary>
        public bool IsPassword
        {
            get => _isPassword;
            set
            {
                _isPassword = value;
                UpdateLabel();
            }
        }

        /// <summary>
        /// Called for every character before it is taken, typed or pasted. Return false to
        /// refuse it - a number field lets digits through and nothing else.
        /// </summary>
        public Func<char, bool>? CharacterFilter { get; set; }

        /// <summary>
        /// The caret, as the number of characters in front of it. Assigning it moves the caret
        /// and drops the selection; use <see cref="Select"/> to keep one.
        /// </summary>
        public int CaretPosition
        {
            get => _caret;
            set => MoveCaretTo(value, extendSelection: false);
        }

        /// <summary>The first selected character. Meaningful only while <see cref="HasSelection"/>.</summary>
        public int SelectionStart => Math.Min(_caret, _anchor);

        /// <summary>How many characters are selected; zero when none are.</summary>
        public int SelectionLength => Math.Abs(_caret - _anchor);

        public bool HasSelection => _caret != _anchor;

        /// <summary>The selected characters, or an empty string.</summary>
        public string SelectedText => _text.Substring(SelectionStart, SelectionLength);

        /// <summary>
        /// Whether the caret blinks. On by default. Off, it is drawn solid into the dialog
        /// surface, which is also what happens wherever there is no interactive render pass.
        /// </summary>
        public bool CaretBlinks { get; set; } = true;

        /// <summary>
        /// Where Ctrl+V and <see cref="Paste"/> read from. Null - the default - is the game's
        /// clipboard, reached through the dialog; the layout harness, which has no game, gives
        /// the field a string of its own here.
        /// </summary>
        public Func<string?>? ReadClipboard { get; set; }

        /// <summary>Where Ctrl+C, Ctrl+X, <see cref="Copy"/> and <see cref="Cut"/> write to. See <see cref="ReadClipboard"/>.</summary>
        public Action<string>? WriteClipboard { get; set; }

        /// <summary>Raised whenever the text changes, by typing, pasting or from code.</summary>
        public event EventHandler<string>? TextChanged;

        /// <summary>Raised when Enter is pressed in the field. The text is the argument.</summary>
        public event EventHandler<string>? EnterPressed;

        /// <summary>
        /// While focused this field takes every key, so typing does not trigger the game's
        /// hotkeys. The dialog keeps Escape out of that, so there is always a way out.
        /// </summary>
        public override bool WantsAllKeyboardInput => HasKeyboardFocus;
        #endregion

        private readonly TextLabelControl _label;
        private string _placeholder = "";
        private bool _isHovered;
        private bool _isDragging;
        private bool _isDisposed;

        /// <summary>
        /// How far the text is shifted left so the caret is inside the box, in device pixels.
        /// Zero while the text fits.
        /// </summary>
        private double _scrollX;

        /// <summary>
        /// The caret's distance from the start of the text, in device pixels, as measured when
        /// the surface was last drawn. The interactive pass reads it rather than measuring
        /// again: it runs every frame and has no Cairo context to measure with.
        /// </summary>
        private double _caretOffset;

        private float _blinkTime;
        private LoadedTexture? _caretTexture;

        private long _lastPressTime;
        private int _lastPressIndex = -1;

        public TextInputControl(string _Name = "", PointD? _Size = null, double _Margin = 5)
            : base(_Name, _Size ?? new PointD(UnscaledDefaultWidth, UnscaledDefaultHeight),
                   Orientation.None, _Margin, _Padding: 0)
        {
            IsAutoSize = false;
            IsFocusable = true;

            _label = new TextLabelControl(
                text: "",
                fontName: GuiStyle.StandardFontName,
                fontSize: FontSize,
                textColor: new ElementColor(GuiStyle.DialogDefaultTextColor),
                orientation: TextOrientation.MiddleLeft,
                padding: (int)UnscaledTextPadding,
                _Name: _Name + "_label",
                _Margin: 0,
                _Padding: 0)
            {
                IsAutoSize = false
            };

            Children.Add(_label);

            KeyPress += OnKeyPress;
            KeyDown += OnKeyDown;
            MouseDown += OnMouseDown;
            MouseMove += OnMouseMove;
            MouseUp += OnMouseUp;
            Clicked += (sender, e) => Dialog?.FocusControl(this);
            Enter += (sender, e) => { _isHovered = true; Dialog?.Refresh(); };
            Exit += (sender, e) => { _isHovered = false; Dialog?.Refresh(); };
            GotFocus += (sender, e) => { _blinkTime = 0; Dialog?.Refresh(); };
            LostFocus += (sender, e) => Dialog?.Refresh();
        }

        #region Editing
        /// <summary>What the label shows for the text: the text, or a dot per character.</summary>
        private string DisplayText => _isPassword ? new string('*', _text.Length) : _text;

        private void SetText(string text, int caret)
        {
            if (MaxLength > 0 && text.Length > MaxLength)
            {
                text = text.Substring(0, MaxLength);
            }

            bool changed = text != _text;

            _text = text;
            _caret = Math.Clamp(caret, 0, _text.Length);
            _anchor = _caret;
            _blinkTime = 0;

            UpdateLabel();

            if (changed)
            {
                TextChanged?.Invoke(this, _text);
            }
        }

        /// <summary>
        /// Swaps a range - the selection, usually - for other text and puts the caret after
        /// what went in. The one way the text changes from the keyboard, so MaxLength and the
        /// events are handled in exactly one place.
        /// </summary>
        private void Replace(int start, int length, string insert)
        {
            start = Math.Clamp(start, 0, _text.Length);
            length = Math.Clamp(length, 0, _text.Length - start);

            string text = _text.Remove(start, length).Insert(start, insert);
            int caret = start + insert.Length;

            if (MaxLength > 0 && text.Length > MaxLength)
            {
                text = text.Substring(0, MaxLength);
                caret = Math.Min(caret, MaxLength);
            }

            SetText(text, caret);
        }

        /// <summary>
        /// What of a string may go into the field: one line, no control characters, and
        /// nothing the <see cref="CharacterFilter"/> refuses. A line break becomes a space,
        /// which is what a single line field can do with one.
        /// </summary>
        private string Clean(string text)
        {
            var kept = new StringBuilder(text.Length);

            foreach (char character in text)
            {
                if (character == '\r' || character == '\uFEFF')
                    continue;

                char candidate = character == '\n' || character == '\t' ? ' ' : character;

                if (candidate < ' ' || candidate == 127)
                    continue;

                if (CharacterFilter != null && !CharacterFilter(candidate))
                    continue;

                kept.Append(candidate);
            }

            return kept.ToString();
        }

        /// <summary>
        /// Puts text in at the caret, in place of the selection if there is one - what a paste
        /// does, and what a mod does to fill in a suggestion.
        /// </summary>
        public void InsertText(string text)
        {
            string clean = Clean(text ?? "");

            if (clean.Length == 0 && !HasSelection)
                return;

            Replace(SelectionStart, SelectionLength, clean);
        }

        /// <summary>Selects a range. The caret ends up at the end of it.</summary>
        public void Select(int start, int length)
        {
            start = Math.Clamp(start, 0, _text.Length);
            length = Math.Clamp(length, 0, _text.Length - start);

            if (_anchor == start && _caret == start + length)
                return;

            _anchor = start;
            _caret = start + length;
            _blinkTime = 0;

            InvalidateVisual();
        }

        public void SelectAll()
        {
            Select(0, _text.Length);
        }

        /// <summary>Removes the selected characters, if any. True when something went.</summary>
        public bool DeleteSelection()
        {
            if (!HasSelection)
                return false;

            Replace(SelectionStart, SelectionLength, "");
            return true;
        }

        /// <summary>Copies the selection to the clipboard. A password field copies nothing.</summary>
        public void Copy()
        {
            if (!HasSelection || _isPassword)
                return;

            SetClipboard(SelectedText);
        }

        /// <summary>Copies the selection to the clipboard and removes it from the field.</summary>
        public void Cut()
        {
            if (!HasSelection)
                return;

            if (!_isPassword)
            {
                SetClipboard(SelectedText);
            }

            DeleteSelection();
        }

        /// <summary>Puts the clipboard in at the caret, in place of the selection.</summary>
        public void Paste()
        {
            string? text = GetClipboard();

            if (string.IsNullOrEmpty(text))
                return;

            InsertText(text);
        }

        private string? GetClipboard()
        {
            if (ReadClipboard != null)
                return ReadClipboard();

            return Dialog?.Api.Forms.GetClipboardText();
        }

        private void SetClipboard(string text)
        {
            if (WriteClipboard != null)
            {
                WriteClipboard(text);
                return;
            }

            Dialog?.Api.Forms.SetClipboardText(text);
        }

        private void UpdateLabel()
        {
            _label.Text = _text.Length == 0 ? _placeholder : DisplayText;

            // Dimmed while it is showing the placeholder, so it does not read as real content.
            _label.TextColor = _text.Length == 0
                ? Dim(GuiStyle.DialogDefaultTextColor)
                : new ElementColor(GuiStyle.DialogDefaultTextColor);

            Dialog?.Refresh();
        }

        private static ElementColor Dim(double[] color)
        {
            var dimmed = new ElementColor(color);
            dimmed.A = (byte)(dimmed.A * 0.45);
            return dimmed;
        }

        private void OnKeyPress(object? sender, IS2Mod.ControlTypes.Events.KeyEventArgs e)
        {
            char typed = e.KeyChar;

            // Control characters are the business of OnKeyDown - backspace and enter arrive
            // here as well on some layouts, and inserting them as text would put a box in the
            // middle of the word.
            if (typed < ' ' || typed == 127)
                return;

            // A shortcut - Ctrl+V - is OnKeyDown's as well, and on some platforms the letter
            // arrives here too. Only bare Ctrl is a shortcut: AltGr is Ctrl and Alt together,
            // and that is how @ and the euro sign are typed on half the keyboards in Europe.
            if ((e.CtrlPressed || e.CommandPressed) && !e.AltPressed)
                return;

            if (CharacterFilter != null && !CharacterFilter(typed))
            {
                e.Handled = true;
                return;
            }

            // What is selected goes, so the field is not full while it is about to be emptied.
            if (MaxLength > 0 && _text.Length - SelectionLength >= MaxLength)
            {
                e.Handled = true;
                return;
            }

            Replace(SelectionStart, SelectionLength, typed.ToString());
            e.Handled = true;
        }

        private void OnKeyDown(object? sender, IS2Mod.ControlTypes.Events.KeyEventArgs e)
        {
            bool ctrl = e.CtrlPressed || e.CommandPressed;
            bool shift = e.ShiftPressed;

            switch (e.Key)
            {
                case GlKeys.BackSpace:
                    if (!DeleteSelection() && _caret > 0)
                    {
                        int from = ctrl ? WordStart(_caret) : _caret - 1;
                        Replace(from, _caret - from, "");
                    }
                    e.Handled = true;
                    break;

                case GlKeys.Delete:
                    if (!DeleteSelection() && _caret < _text.Length)
                    {
                        int to = ctrl ? WordEnd(_caret) : _caret + 1;
                        Replace(_caret, to - _caret, "");
                    }
                    e.Handled = true;
                    break;

                case GlKeys.Left:
                    // Left out of a selection lands at its start, the way every editor does it,
                    // rather than one to the left of wherever the caret happened to be.
                    if (HasSelection && !shift && !ctrl)
                        MoveCaretTo(SelectionStart, extendSelection: false);
                    else
                        MoveCaretTo(ctrl ? WordStart(_caret) : _caret - 1, shift);
                    e.Handled = true;
                    break;

                case GlKeys.Right:
                    if (HasSelection && !shift && !ctrl)
                        MoveCaretTo(SelectionStart + SelectionLength, extendSelection: false);
                    else
                        MoveCaretTo(ctrl ? WordEnd(_caret) : _caret + 1, shift);
                    e.Handled = true;
                    break;

                case GlKeys.Home:
                    MoveCaretTo(0, shift);
                    e.Handled = true;
                    break;

                case GlKeys.End:
                    MoveCaretTo(_text.Length, shift);
                    e.Handled = true;
                    break;

                case GlKeys.A:
                    if (ctrl)
                    {
                        SelectAll();
                        e.Handled = true;
                    }
                    break;

                case GlKeys.C:
                    if (ctrl)
                    {
                        Copy();
                        e.Handled = true;
                    }
                    break;

                case GlKeys.X:
                    if (ctrl)
                    {
                        Cut();
                        e.Handled = true;
                    }
                    break;

                case GlKeys.V:
                    if (ctrl)
                    {
                        Paste();
                        e.Handled = true;
                    }
                    break;

                case GlKeys.Enter:
                case GlKeys.KeypadEnter:
                    EnterPressed?.Invoke(this, _text);
                    e.Handled = true;
                    break;
            }
        }

        /// <summary>
        /// Moves the caret. With <paramref name="extendSelection"/> the anchor stays where it is
        /// and the selection grows or shrinks to the new caret; without, the selection is gone.
        /// </summary>
        private void MoveCaretTo(int position, bool extendSelection)
        {
            int clamped = Math.Clamp(position, 0, _text.Length);
            bool selectionGoes = !extendSelection && HasSelection;

            if (clamped == _caret && !selectionGoes)
                return;

            _caret = clamped;

            if (!extendSelection)
            {
                _anchor = _caret;
            }

            _blinkTime = 0;
            InvalidateVisual();
        }

        /// <summary>The start of the word the caret is in or after - where Ctrl+Left goes.</summary>
        private int WordStart(int from)
        {
            int i = Math.Clamp(from, 0, _text.Length);

            while (i > 0 && char.IsWhiteSpace(_text[i - 1]))
                i--;

            while (i > 0 && !char.IsWhiteSpace(_text[i - 1]))
                i--;

            return i;
        }

        /// <summary>The start of the next word - where Ctrl+Right goes.</summary>
        private int WordEnd(int from)
        {
            int i = Math.Clamp(from, 0, _text.Length);

            while (i < _text.Length && !char.IsWhiteSpace(_text[i]))
                i++;

            while (i < _text.Length && char.IsWhiteSpace(_text[i]))
                i++;

            return i;
        }

        /// <summary>Selects the word around a character - what a double click does.</summary>
        private void SelectWordAt(int index)
        {
            if (_text.Length == 0)
                return;

            int start = Math.Clamp(index, 0, _text.Length);
            int end = start;

            while (start > 0 && !char.IsWhiteSpace(_text[start - 1]))
                start--;

            while (end < _text.Length && !char.IsWhiteSpace(_text[end]))
                end++;

            Select(start, end - start);
        }
        #endregion

        #region Mouse
        private void OnMouseDown(object? sender, MouseEventArgs e)
        {
            if (e.Button != Vintagestory.API.Common.EnumMouseButton.Left)
                return;

            Dialog?.FocusControl(this);

            int index = IndexAt(e.X);
            long now = Environment.TickCount64;
            bool doubleClick = index == _lastPressIndex && now - _lastPressTime <= DoubleClickMilliseconds;

            _lastPressTime = now;
            _lastPressIndex = index;

            if (doubleClick)
            {
                // The second press takes the word; no drag starts from it, so the selection is
                // not wiped by the first pixel the mouse moves before the button comes up.
                _lastPressIndex = -1;
                SelectWordAt(index);
                return;
            }

            _anchor = index;
            _caret = index;
            _blinkTime = 0;
            _isDragging = true;

            Dialog?.CaptureMouse(this);
            InvalidateVisual();
        }

        private void OnMouseMove(object? sender, MouseEventArgs e)
        {
            if (!_isDragging)
                return;

            MoveCaretTo(IndexAt(e.X), extendSelection: true);
        }

        private void OnMouseUp(object? sender, MouseEventArgs e)
        {
            if (!_isDragging)
                return;

            _isDragging = false;
            Dialog?.ReleaseMouseCapture();
        }

        /// <summary>
        /// The character boundary nearest to a point on the screen: the caret goes in front of
        /// a character when the point is in its left half, behind it when in its right half.
        /// Measured with the font the label draws with, and the scroll taken off first, so a
        /// click lands on the character it is on and not the one that scrolled out.
        /// </summary>
        private int IndexAt(int screenX)
        {
            double dialogX = Dialog?.Position.X ?? 0;
            double local = screenX - dialogX - Position.X - UnscaledTextPadding * LayoutScale + _scrollX;
            string shown = DisplayText;

            if (shown.Length == 0 || local <= 0)
                return 0;

            Context ctx = TextLabelControl.MeasureContext();
            SetupFont(ctx);

            double previous = 0;

            for (int i = 1; i <= shown.Length; i++)
            {
                double width = ctx.TextExtents(shown.Substring(0, i)).XAdvance;

                if (local < (previous + width) / 2.0)
                    return i - 1;

                previous = width;
            }

            return shown.Length;
        }
        #endregion

        #region Layout
        /// <summary>One hit target: the label must not take the click meant for the field.</summary>
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

            ctx.SetSourceRGBA(0.0, 0.0, 0.0, BoxFillAlpha);
            GuiElement.RoundRectangle(ctx, x, y, width, height, BoxCornerRadius);
            ctx.Fill();

            VanillaDraw.EmbossRoundRectangle(ctx, x, y, width, height, inverse: true, depth: 1, radius: 1);

            if (HasKeyboardFocus || _isHovered)
            {
                ctx.SetSourceRGBA(GuiStyle.DialogHighlightColor);
                GuiElement.RoundRectangle(ctx, x, y, width, height, BoxCornerRadius);
                ctx.LineWidth = 2.0 * LayoutScale;
                ctx.Stroke();
            }

            ctx.Restore();

            double padding = UnscaledTextPadding * LayoutScale;
            double visibleWidth = Math.Max(0, width - padding * 2);

            // Where the caret and the selection are, in the font the label draws with - anything
            // else puts them next to the wrong character on a proportional font.
            string shown = DisplayText;

            SetupFont(ctx);

            double textWidth = shown.Length == 0 ? 0 : ctx.TextExtents(shown).XAdvance;
            _caretOffset = MeasureWidth(ctx, shown, _caret);

            ScrollCaretIntoView(visibleWidth, textWidth);

            // Everything from here on is cut at the inside of the frame and shifted by the
            // scroll: the selection, the label - the placeholder included, which used to run
            // out of the box when it was longer than the field - and the caret. The clip is set
            // before the shift so it stays put while the text moves under it.
            ctx.Save();
            ctx.Rectangle(x + padding, y, visibleWidth, height);
            ctx.Clip();
            ctx.Translate(-_scrollX, 0);

            if (HasSelection && HasKeyboardFocus)
            {
                DrawSelection(ctx, shown, padding);
            }

            base.GenerateRenderData(surface, ctx);

            // Solid, in the surface, only where nothing blinks it: the interactive pass draws
            // the blinking one and would otherwise double it.
            if (HasKeyboardFocus && (!CaretBlinks || Dialog == null))
            {
                DrawCaret(ctx, padding);
            }

            ctx.Restore();
        }

        private void SetupFont(Context ctx)
        {
            ctx.SelectFontFace(GuiStyle.StandardFontName, FontSlant.Normal, FontWeight.Normal);
            ctx.SetFontSize(FontSize * LayoutScale);
        }

        /// <summary>The width of the first <paramref name="count"/> characters, with the font already set up on the context.</summary>
        private static double MeasureWidth(Context ctx, string shown, int count)
        {
            int length = Math.Clamp(count, 0, shown.Length);

            if (length == 0)
                return 0;

            return ctx.TextExtents(shown.Substring(0, length)).XAdvance;
        }

        /// <summary>
        /// Moves the text so the caret is inside the box: left when the caret went past the
        /// right edge, right when it went past the left one, and back to the start as soon as
        /// the text fits again.
        /// </summary>
        private void ScrollCaretIntoView(double visibleWidth, double textWidth)
        {
            double caretWidth = UnscaledCaretWidth * LayoutScale;

            if (textWidth + caretWidth <= visibleWidth)
            {
                _scrollX = 0;
                return;
            }

            if (_caretOffset - _scrollX > visibleWidth - caretWidth)
            {
                _scrollX = _caretOffset - visibleWidth + caretWidth;
            }

            if (_caretOffset - _scrollX < 0)
            {
                _scrollX = _caretOffset;
            }

            // Never further than the end of the text: a deletion at the end pulls the text back
            // rather than leaving empty room behind it.
            _scrollX = Math.Clamp(_scrollX, 0, Math.Max(0, textWidth + caretWidth - visibleWidth));
        }

        private void DrawSelection(Context ctx, string shown, double padding)
        {
            double start = MeasureWidth(ctx, shown, SelectionStart);
            double end = MeasureWidth(ctx, shown, SelectionStart + SelectionLength);

            if (end <= start)
                return;

            double[] highlight = GuiStyle.DialogHighlightColor;

            ctx.Save();
            ctx.SetSourceRGBA(highlight[0], highlight[1], highlight[2], SelectionAlpha);
            ctx.Rectangle(Position.X + padding + start, Position.Y + padding / 2.0, end - start, Size.Y - padding);
            ctx.Fill();
            ctx.Restore();
        }

        private void DrawCaret(Context ctx, double padding)
        {
            double caretX = Position.X + padding + _caretOffset;
            double top = Position.Y + padding / 2.0;
            double bottom = Position.Y + Size.Y - padding / 2.0;

            ctx.Save();
            ctx.SetSourceRGBA(GuiStyle.DialogDefaultTextColor);
            ctx.Rectangle(caretX, top, UnscaledCaretWidth * LayoutScale, bottom - top);
            ctx.Fill();
            ctx.Restore();
        }

        /// <summary>
        /// The blinking caret. It is not in the surface, because blinking there would mean
        /// redrawing the whole dialog twice a second for a two pixel bar; it is a tiny texture
        /// drawn over the surface on the frames it is on, the way item stacks are drawn over
        /// their slots. The timer restarts on every edit and every move, so the caret is solid
        /// right where the player is looking.
        /// </summary>
        public override void GenerateInteractiveRenderData(ICoreClientAPI api, float deltaTime)
        {
            base.GenerateInteractiveRenderData(api, deltaTime);

            if (!CaretBlinks || !IsVisible)
                return;

            if (!HasKeyboardFocus)
            {
                _blinkTime = 0;
                return;
            }

            _blinkTime += deltaTime;

            if (_blinkTime % BlinkPeriod >= BlinkPeriod / 2.0f)
                return;

            double padding = UnscaledTextPadding * LayoutScale;
            double caretWidth = UnscaledCaretWidth * LayoutScale;
            double caretX = padding + _caretOffset - _scrollX;

            // The surface pass keeps the caret inside the box; if the field is narrower than a
            // caret there is nothing sensible to draw.
            if (caretX < 0 || caretX + caretWidth > Size.X)
                return;

            PointD screen = GetScreenPosition();
            float z = (Dialog?.SurfaceRenderZ ?? 0) + IS2Mod.ControlTypes.Custom.CustomDialogElement.SlotItemZOffset;

            api.Render.RenderTexture(
                CaretTexture(api).TextureId,
                screen.X + caretX,
                screen.Y + padding / 2.0,
                caretWidth,
                Size.Y - padding,
                z);
        }

        /// <summary>A two by two block of the text colour, stretched to whatever the caret is.</summary>
        private LoadedTexture CaretTexture(ICoreClientAPI api)
        {
            if (_caretTexture != null)
                return _caretTexture;

            using (var surface = new ImageSurface(Format.Argb32, 2, 2))
            using (var ctx = new Context(surface))
            {
                ctx.SetSourceRGBA(GuiStyle.DialogDefaultTextColor);
                ctx.Paint();
                surface.Flush();

                var texture = new LoadedTexture(api);
                api.Gui.LoadOrUpdateCairoTexture(surface, linearMag: false, intoTexture: ref texture);
                _caretTexture = texture;
            }

            return _caretTexture;
        }
        #endregion

        /// <summary>Lets the caret texture go. The dialog calls this for every control it owns; calling it twice is fine.</summary>
        public void Dispose()
        {
            if (_isDisposed)
                return;

            _isDisposed = true;

            _caretTexture?.Dispose();
            _caretTexture = null;
        }
    }
}
