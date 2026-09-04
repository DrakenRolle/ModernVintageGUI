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
    /// What it does not have yet: selecting a range, cut and paste, and a blinking caret. The
    /// caret is solid on purpose - blinking means redrawing the dialog surface twice a second
    /// for a two pixel bar.
    /// </summary>
    public class TextInputControl : UIControl
    {
        #region Vanilla styling
        /// <summary>GuiElementTextInput sinks its box the way a dropdown does.</summary>
        private const double BoxFillAlpha = 0.2;
        private const double BoxCornerRadius = 1.0;

        /// <summary>Room between the frame and the text.</summary>
        private const double UnscaledTextPadding = 6.0;

        /// <summary>How wide the caret is drawn.</summary>
        private const double UnscaledCaretWidth = 1.5;

        public const double UnscaledDefaultHeight = 30.0;
        private const double UnscaledDefaultWidth = 200.0;

        private const int FontSize = 16;
        #endregion

        #region Properties
        private string _text = "";
        private int _caret;

        /// <summary>
        /// What is in the field. Setting it puts the caret at the end and raises
        /// <see cref="TextChanged"/>, so a handler sees a change from code like one from typing.
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

        /// <summary>Show dots instead of the text.</summary>
        public bool IsPassword { get; set; }

        /// <summary>
        /// Called for every character before it is taken. Return false to refuse it - a number
        /// field lets digits through and nothing else.
        /// </summary>
        public Func<char, bool>? CharacterFilter { get; set; }

        /// <summary>Raised whenever the text changes, by typing or from code.</summary>
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
            Clicked += (sender, e) => Dialog?.FocusControl(this);
            Enter += (sender, e) => { _isHovered = true; Dialog?.Refresh(); };
            Exit += (sender, e) => { _isHovered = false; Dialog?.Refresh(); };
            GotFocus += (sender, e) => Dialog?.Refresh();
            LostFocus += (sender, e) => Dialog?.Refresh();
        }

        #region Editing
        private void SetText(string text, int caret)
        {
            if (MaxLength > 0 && text.Length > MaxLength)
            {
                text = text.Substring(0, MaxLength);
            }

            bool changed = text != _text;

            _text = text;
            _caret = Math.Clamp(caret, 0, _text.Length);

            UpdateLabel();

            if (changed)
            {
                TextChanged?.Invoke(this, _text);
            }
        }

        private void UpdateLabel()
        {
            _label.Text = _text.Length == 0
                ? _placeholder
                : (IsPassword ? new string('*', _text.Length) : _text);

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

        private void OnKeyPress(object? sender, KeyEventArgs e)
        {
            char typed = e.KeyChar;

            // Control characters are the business of OnKeyDown - backspace and enter arrive
            // here as well on some layouts, and inserting them as text would put a box in the
            // middle of the word.
            if (typed < ' ' || typed == 127)
                return;

            if (CharacterFilter != null && !CharacterFilter(typed))
            {
                e.Handled = true;
                return;
            }

            if (MaxLength > 0 && _text.Length >= MaxLength)
            {
                e.Handled = true;
                return;
            }

            SetText(_text.Insert(_caret, typed.ToString()), _caret + 1);
            e.Handled = true;
        }

        private void OnKeyDown(object? sender, KeyEventArgs e)
        {
            switch (e.Key)
            {
                case GlKeys.BackSpace:
                    if (_caret > 0)
                    {
                        SetText(_text.Remove(_caret - 1, 1), _caret - 1);
                    }
                    e.Handled = true;
                    break;

                case GlKeys.Delete:
                    if (_caret < _text.Length)
                    {
                        SetText(_text.Remove(_caret, 1), _caret);
                    }
                    e.Handled = true;
                    break;

                case GlKeys.Left:
                    MoveCaret(-1);
                    e.Handled = true;
                    break;

                case GlKeys.Right:
                    MoveCaret(1);
                    e.Handled = true;
                    break;

                case GlKeys.Home:
                    MoveCaretTo(0);
                    e.Handled = true;
                    break;

                case GlKeys.End:
                    MoveCaretTo(_text.Length);
                    e.Handled = true;
                    break;

                case GlKeys.Enter:
                case GlKeys.KeypadEnter:
                    EnterPressed?.Invoke(this, _text);
                    e.Handled = true;
                    break;
            }
        }

        private void MoveCaret(int by)
        {
            MoveCaretTo(_caret + by);
        }

        private void MoveCaretTo(int position)
        {
            int clamped = Math.Clamp(position, 0, _text.Length);

            if (clamped == _caret)
                return;

            _caret = clamped;
            Dialog?.Refresh();
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

            base.GenerateRenderData(surface, ctx);

            if (HasKeyboardFocus)
            {
                DrawCaret(ctx);
            }
        }

        /// <summary>
        /// The caret, measured from the text in front of it with the same font the label draws
        /// with - anything else puts it next to the wrong character on a proportional font.
        /// </summary>
        private void DrawCaret(Context ctx)
        {
            double padding = UnscaledTextPadding * LayoutScale;
            double offset = MeasureUpToCaret(ctx);

            double caretX = Position.X + padding + offset;
            double top = Position.Y + padding / 2.0;
            double bottom = Position.Y + Size.Y - padding / 2.0;

            ctx.Save();
            ctx.SetSourceRGBA(GuiStyle.DialogDefaultTextColor);
            ctx.Rectangle(caretX, top, UnscaledCaretWidth * LayoutScale, bottom - top);
            ctx.Fill();
            ctx.Restore();
        }

        private double MeasureUpToCaret(Context ctx)
        {
            if (_caret <= 0 || _text.Length == 0)
                return 0;

            string upTo = IsPassword
                ? new string('*', Math.Min(_caret, _text.Length))
                : _text.Substring(0, Math.Min(_caret, _text.Length));

            ctx.Save();
            ctx.SelectFontFace(GuiStyle.StandardFontName, FontSlant.Normal, FontWeight.Normal);
            ctx.SetFontSize(FontSize * LayoutScale);

            TextExtents extents = ctx.TextExtents(upTo);

            ctx.Restore();

            return extents.XAdvance;
        }
        #endregion
    }
}
