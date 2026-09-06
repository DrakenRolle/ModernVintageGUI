using Cairo;
using IS2Mod.ControlTypes.Custom;
using IS2Mod.ControlTypes.Events;
using IS2Mod.Enums;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using Vintagestory.API.Client;
using Vintagestory.API.Util;

namespace IS2Mod.ControlTypes
{
    public abstract class UIControl : INotifyPropertyChanged
    {
        #region Events
        public event PropertyChangedEventHandler? PropertyChanged;

        public event EventHandler<MouseEventArgs>? Clicked;
        public event EventHandler<MouseEventArgs>? Enter;
        public event EventHandler<MouseEventArgs>? Exit;
        public event EventHandler<MouseEventArgs>? MouseDown;
        public event EventHandler<MouseEventArgs>? MouseUp;
        public event EventHandler<MouseEventArgs>? MouseMove;

        public event EventHandler<Events.MouseWheelEventArgs>? MouseWheel;

        public void InvokeEventClicked(MouseEvent vsArgs)
        {
            var args = new MouseEventArgs(vsArgs);
            Clicked?.Invoke(this, args);
        }

        public void InvokeEventMouseMove(MouseEvent vsArgs)
        {
            var args = new MouseEventArgs(vsArgs);
            MouseMove?.Invoke(this, args);
        }
        public void InvokeEventEnter(MouseEvent vsArgs)
        {
            var args = new MouseEventArgs(vsArgs);
            Enter?.Invoke(this, args);
        }
        public void InvokeEventExit(MouseEvent vsArgs)
        {
            var args = new MouseEventArgs(vsArgs);
            Exit?.Invoke(this, args);
        }
        public void InvokeEventMouseDown(MouseEvent vsArgs)
        {
            var args = new MouseEventArgs(vsArgs);
            MouseDown?.Invoke(this, args);
        }
        public void InvokeEventMouseUp(MouseEvent vsArgs)
        {
            var args = new MouseEventArgs(vsArgs);
            MouseUp?.Invoke(this, args);
        }
        public void InvokeEventMouseWheel(Vintagestory.API.Client.MouseWheelEventArgs vsArgs)
        {
            InvokeEventMouseWheel(new Events.MouseWheelEventArgs(vsArgs));
        }

        /// <summary>
        /// Like the key invokers, this takes the finished arguments: a wheel tick is offered to
        /// the control under the cursor first and then to its ancestors, and each of them has to
        /// see whether somebody below already used it. Building fresh arguments per control
        /// would throw that answer away, which is why the cursor could sit on a button inside a
        /// list and the list would never scroll.
        /// </summary>
        public void InvokeEventMouseWheel(Events.MouseWheelEventArgs args)
        {
            MouseWheel?.Invoke(this, args);
        }

        public event EventHandler<Events.KeyEventArgs>? KeyDown;
        public event EventHandler<Events.KeyEventArgs>? KeyUp;

        /// <summary>
        /// A typed character, with the keyboard layout applied - umlauts, accents, dead keys.
        /// This is what a text field listens on; <see cref="KeyDown"/> carries a raw key code
        /// and cannot tell an "a" from an "A" or produce an "ä" at all.
        ///
        /// It only arrives because <see cref="IS2Mod.Patches.ClientMainKeyPressPatch"/> puts it
        /// there: ClientMain.OnKeyPress, the one the game gets its characters from, triggers
        /// nothing on IClientEventAPI.
        /// </summary>
        public event EventHandler<Events.KeyEventArgs>? KeyPress;

        /// <summary>Raised when this control became the keyboard focus of its dialog.</summary>
        public event EventHandler? GotFocus;

        /// <summary>Raised when it lost that focus again.</summary>
        public event EventHandler? LostFocus;

        /// <summary>
        /// Unlike the mouse invokers this takes the finished arguments rather than the game's
        /// event: the same instance travels to every subscriber so that setting
        /// <see cref="Events.KeyEventArgs.Handled"/> anywhere is seen by the dialog afterwards.
        /// </summary>
        public void InvokeEventKeyDown(Events.KeyEventArgs args)
        {
            KeyDown?.Invoke(this, args);
        }

        public void InvokeEventKeyUp(Events.KeyEventArgs args)
        {
            KeyUp?.Invoke(this, args);
        }

        public void InvokeEventKeyPress(Events.KeyEventArgs args)
        {
            KeyPress?.Invoke(this, args);
        }

        /// <summary>
        /// Raises the focus events. Public for the same reason the mouse invokers are: the
        /// layout harness drives a control into a visual state and renders it, without a client.
        /// Moving the focus in a dialog is <see cref="Custom.CustomDialogElement.FocusControl"/>,
        /// which is what keeps <see cref="HasKeyboardFocus"/> in step with these.
        /// </summary>
        public void InvokeGotFocus()
        {
            GotFocus?.Invoke(this, EventArgs.Empty);
        }

        public void InvokeLostFocus()
        {
            LostFocus?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Raises <see cref="Clicked"/> without a mouse, which is what Enter and Space on the
        /// focused control do. The arguments carry the center of the control, so a handler that
        /// looks at the coordinates - to place a context menu, say - still gets a sensible spot.
        /// </summary>
        public void PerformClick()
        {
            PointD screen = GetScreenPosition();

            var args = new Events.MouseEventArgs(
                (int)(screen.X + Size.X / 2),
                (int)(screen.Y + Size.Y / 2),
                Vintagestory.API.Common.EnumMouseButton.Left);

            Clicked?.Invoke(this, args);
        }

        #endregion

        #region Properties
        private LoadedTexture? _staticElementsTexture;
        public LoadedTexture? StaticElementsTexture
        {
            get => _staticElementsTexture;
            set => _staticElementsTexture = value;
        }

        private ObservableCollection<UIControl> _children = new ObservableCollection<UIControl>();
        public ObservableCollection<UIControl> Children
        {
            get => _children;
            set => _children = value;
        }

        private UIControl? _parent;
        public UIControl? Parent
        {
            get => _parent;
            set => SetProperty(ref _parent, value);
        }

        private CustomDialogElement? _dialog;
        /// <summary>
        /// The dialog this control belongs to, or null while the control is still detached.
        /// Building a subtree before adding it to a dialog is a normal usage pattern, so this
        /// returns null instead of throwing.
        /// </summary>
        public CustomDialogElement? Dialog
        {
            get
            {
                if (_parent != null)
                    return _parent.Dialog;

                return _dialog;
            }
            set => _dialog = value;
        }

        private PointD _position;
        public PointD Position
        {
            get => _position;
            set => SetProperty(ref _position, value);
        }

        private PointD _size = new PointD(0, 0);
        private PointD _explicitSize = new PointD(0, 0);

        /// <summary>
        /// The size this control currently occupies. Assigning it from outside declares a
        /// wanted size (see <see cref="ExplicitSize"/>); the layout passes write it through
        /// <see cref="SetLayoutSize"/> instead so that they do not overwrite that wish.
        /// </summary>
        public PointD Size
        {
            get => _size;
            set
            {
                if (_explicitSize.X == value.X && _explicitSize.Y == value.Y && _size.X == value.X && _size.Y == value.Y)
                    return;

                _explicitSize = value;
                _size = value;

                // Deliberately not SetProperty: this writes two fields, and the layout pass
                // writes _size through SetLayoutSize without ever coming through here. Only the
                // assignment from outside - "make this list 200x150" - invalidates.
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// The size that was last assigned from outside. This is the input of the measure pass
        /// for controls with <see cref="IsAutoSize"/> = false and must never be written by the
        /// layout itself - otherwise measuring would consume its own previous output and the
        /// control would drift with every layout pass.
        /// </summary>
        protected PointD ExplicitSize => _explicitSize;

        /// <summary>
        /// Size assignment for the layout passes. Unlike the <see cref="Size"/> setter this
        /// leaves <see cref="ExplicitSize"/> alone, so stretching and clipping stay repeatable.
        /// </summary>
        protected internal void SetLayoutSize(PointD size)
        {
            _size = size;
        }

        private double _layoutScale = 1.0;

        /// <summary>
        /// Device pixels per author unit, the same idea as GuiElement.scaled() in the vanilla
        /// GUI: everything a caller specifies - Margin, Padding, Size, FontSize, BorderWidth -
        /// is written in unscaled author units, and the layout multiplies by this on the way to
        /// device pixels. Position, Size and CalculatedSize are therefore already device pixels,
        /// which is what the renderer and the hit test need (mouse coordinates are device
        /// pixels too), so nothing has to be transformed back.
        ///
        /// Only the value on the root of a tree matters; every control reports the root value.
        /// <see cref="Custom.CustomDialogElement"/> keeps it in sync with RuntimeEnv.GUIScale,
        /// the layout harness sets it by hand to render a scenario at several scales.
        /// </summary>
        public double LayoutScale
        {
            get => Parent?.LayoutScale ?? _layoutScale;
            set => _layoutScale = value;
        }

        /// <summary>Margin in device pixels.</summary>
        protected double ScaledMargin => Margin * LayoutScale;

        /// <summary>Padding in device pixels.</summary>
        protected double ScaledPadding => Padding * LayoutScale;

        /// <summary>The size assigned from outside, in device pixels.</summary>
        protected PointD ScaledExplicitSize => new PointD(
            ExplicitSize.X * LayoutScale,
            ExplicitSize.Y * LayoutScale);

        private bool _isAutoSize;
        public bool IsAutoSize
        {
            get => _isAutoSize;
            set => SetProperty(ref _isAutoSize, value);
        }

        private double _margin;
        public double Margin
        {
            get => _margin;
            set => SetProperty(ref _margin, value);
        }

        private double _padding;
        public double Padding
        {
            get => _padding;
            set => SetProperty(ref _padding, value);
        }

        private int _index;
        public int Index
        {
            get => _index;
            set => SetProperty(ref _index, value);
        }

        private Orientation _insideOrientation;
        public Orientation InsideOrientation
        {
            get => _insideOrientation;
            set => SetProperty(ref _insideOrientation, value);
        }

        private Orientation _orientation;
        /// <summary>
        /// Where this control sits *across* its parent's stacking direction. In a column that is
        /// horizontal - Left, Right, Center or Fill - and in a row it is vertical - Top, Bottom,
        /// Center or Fill.
        ///
        /// <see cref="Enums.Orientation.Fill"/> is the default and stretches the control to the
        /// full width of the column, or the full height of the row. The other three leave it at
        /// its natural size and decide which end of the free space it takes.
        ///
        /// Along the stacking direction this says nothing: where a control lands there is
        /// decided by the order of the children, so a value naming that axis - Top in a column,
        /// Left in a row - is read as Fill rather than as a contradiction. Same for
        /// <see cref="Enums.Orientation.None"/>, and for every child of an overlay container
        /// (<c>InsideOrientation = None</c>), which has no cross axis to align across.
        /// </summary>
        public Orientation Orientation
        {
            get => _orientation;
            set => SetProperty(ref _orientation, value);
        }

        /// <summary>
        /// What <see cref="Orientation"/> comes out as once the parent's stacking direction is
        /// known. Four cases rather than seven, because most of the enum is two names for the
        /// same two ends of an axis.
        /// </summary>
        protected internal enum CrossAlignment
        {
            /// <summary>Stretched to the full extent of the parent across the stacking axis.</summary>
            Fill,

            /// <summary>Natural size, at the left of a column or the top of a row.</summary>
            Start,

            /// <summary>Natural size, centred.</summary>
            Center,

            /// <summary>Natural size, at the right of a column or the bottom of a row.</summary>
            End
        }

        /// <summary>
        /// <see cref="Orientation"/> resolved against the axis it is actually read on. This is
        /// the single place the mapping lives, so the stretching in the arrange pass and the
        /// positioning cannot disagree about what a value meant.
        /// </summary>
        protected internal CrossAlignment CrossAlign
        {
            get
            {
                Orientation stacking = Parent?.InsideOrientation ?? Orientation.Top;

                switch (stacking)
                {
                    // A column. The cross axis is horizontal, so only the horizontal names say
                    // anything; Top and Bottom name the stacking axis and are read as Fill.
                    case Orientation.Top:
                    case Orientation.Bottom:
                        return _orientation switch
                        {
                            Orientation.Left => CrossAlignment.Start,
                            Orientation.Right => CrossAlignment.End,
                            Orientation.Center => CrossAlignment.Center,
                            _ => CrossAlignment.Fill
                        };

                    // A row, and the mirror image of the above.
                    case Orientation.Left:
                    case Orientation.Right:
                        return _orientation switch
                        {
                            Orientation.Top => CrossAlignment.Start,
                            Orientation.Bottom => CrossAlignment.End,
                            Orientation.Center => CrossAlignment.Center,
                            _ => CrossAlignment.Fill
                        };

                    // An overlay stacks in no direction at all, so there is no cross axis to be
                    // aligned across. Everything sits at the origin at its natural size.
                    default:
                        return CrossAlignment.Start;
                }
            }
        }

        private string _name;
        public string Name
        {
            get => _name;
            set => SetProperty(ref _name, value);
        }

        private bool _isStaticElement;
        public bool IsStaticElement
        {
            get => _isStaticElement;
            set => SetProperty(ref _isStaticElement, value);
        }

        private bool _isVisible = true;
        /// <summary>
        /// Whether this control takes part in the dialog at all.
        ///
        /// Hiding *collapses*: an invisible control is not measured, takes no space in its
        /// parent's stack, is not drawn, cannot be hit by the mouse and is not in the tab order.
        /// That is the WinForms rule rather than the WPF one, and it is the one a stacking
        /// layout wants - a row that hides its third button should close up, not leave a hole.
        /// Reserving the space is what an empty <see cref="RectangleControl"/> of a fixed size
        /// is for.
        ///
        /// The subtree goes with it: hiding a container hides everything in it, and the
        /// children keep their own IsVisible so that showing the container again restores
        /// exactly what was showing before.
        ///
        /// <see cref="Custom.CustomDialogElement"/> reuses this as "is the dialog on screen",
        /// which is the same statement one level up - assigning it there is Show() and Hide().
        /// </summary>
        public bool IsVisible
        {
            get => _isVisible;
            set => SetVisibleCore(value);
        }

        /// <summary>
        /// The one place <see cref="IsVisible"/> is written, so that a subclass can route the
        /// assignment somewhere else without the property and the state drifting apart.
        /// <see cref="Custom.CustomDialogElement"/> overrides it to run its Show/Hide bookkeeping.
        /// </summary>
        protected virtual void SetVisibleCore(bool value)
        {
            SetProperty(ref _isVisible, value, nameof(IsVisible));
        }

        /// <summary>Writes the field without going through an override. For subclass use.</summary>
        protected void SetVisibleField(bool value)
        {
            SetProperty(ref _isVisible, value, nameof(IsVisible));
        }

        /// <summary>
        /// True when this control and every one of its ancestors is visible. What decides
        /// whether the player can actually see it - a visible control inside a hidden panel is
        /// not on screen.
        /// </summary>
        public bool IsEffectivelyVisible
        {
            get
            {
                for (UIControl? control = this; control != null; control = control.Parent)
                {
                    if (!control._isVisible)
                        return false;
                }

                return true;
            }
        }

        private bool _isEnabled = true;
        /// <summary>
        /// Whether this control reacts to the player. On by default.
        ///
        /// A disabled control still takes its space and is still drawn - dimmed, so that the
        /// dialog does not silently change shape - but it receives no mouse events, never
        /// hovers, and is out of the tab order. It also swallows clicks that land on it rather
        /// than letting them through to whatever is behind, which is what keeps a disabled
        /// button from being a hole in the dialog.
        ///
        /// It is inherited downwards: disabling a container disables everything in it, without
        /// touching the children's own IsEnabled, so enabling the container again brings back
        /// exactly the state it had. Read <see cref="IsEffectivelyEnabled"/> when you need the
        /// answer that accounts for the ancestors.
        /// </summary>
        public bool IsEnabled
        {
            get => _isEnabled;
            set => SetProperty(ref _isEnabled, value);
        }

        /// <summary>
        /// True when this control and every one of its ancestors is enabled. This, not
        /// <see cref="IsEnabled"/>, is what input and focus ask.
        /// </summary>
        public bool IsEffectivelyEnabled
        {
            get
            {
                for (UIControl? control = this; control != null; control = control.Parent)
                {
                    if (!control._isEnabled)
                        return false;
                }

                return true;
            }
        }

        /// <summary>
        /// Whether this control can be focused right now: it wants focus, and it is neither
        /// hidden nor disabled. <see cref="IsFocusable"/> on its own is only the wish.
        /// </summary>
        public bool CanTakeFocus => IsFocusable && _isEnabled && _isVisible;

        private bool _isFocusable;
        /// <summary>
        /// Whether this control can take the keyboard focus of its dialog - by being clicked or
        /// by being tabbed to. Off by default: decoration must never end up in the tab order.
        ///
        /// Set it on the control the user interacts with, not on its parts. A composite that
        /// overrides <see cref="HitTestRecursive"/> to be atomic for the mouse should be atomic
        /// for the keyboard as well, i.e. focusable itself with non focusable children.
        /// </summary>
        public bool IsFocusable
        {
            get => _isFocusable;
            set => SetProperty(ref _isFocusable, value);
        }

        private bool _hasKeyboardFocus;
        /// <summary>
        /// Whether this control currently holds the keyboard focus of its dialog. Owned by
        /// <see cref="Custom.CustomDialogElement.FocusControl"/>, which is also what raises
        /// <see cref="GotFocus"/> and <see cref="LostFocus"/> - assigning it elsewhere would let
        /// the two states drift apart.
        ///
        /// Note that this is the focus *within* a dialog. Whether the dialog itself is the one
        /// receiving keys is <see cref="Custom.CustomDialogElement.IsFocused"/>.
        /// </summary>
        public bool HasKeyboardFocus
        {
            get => _hasKeyboardFocus;
            internal set => SetProperty(ref _hasKeyboardFocus, value);
        }

        private PointD _maxSize;
        /// <summary>
        /// An upper limit for the measured size, in unscaled author units. Zero on an axis means
        /// no limit on that axis, which is the default.
        ///
        /// It caps <see cref="IsAutoSize"/>: the control grows with its content and then stops.
        /// Three things are worth knowing before using it.
        ///
        /// **It needs <see cref="ClipsChildren"/>, or scrolling, to be useful.** Capping the box
        /// does not cap the content - the children still measure to their full size, overflow,
        /// and are then squashed to nothing by the overflow check. A capped container without
        /// clipping is worse than an uncapped one.
        ///
        /// **It scales.** Like every authored dimension it is multiplied by LayoutScale, so a
        /// cap of 700 is 1400 device pixels at a GUI scale of 2. That makes it the right tool
        /// for "this list should never be taller than about ten rows" and the wrong one for
        /// "this must fit on the screen" - see
        /// <see cref="Custom.CustomDialogElement.MaxScreenFraction"/> for the second.
        ///
        /// **Across the stacking axis the parent has the last word.** A parent stretches its
        /// children to its own content width; the cap is honoured there too, but a control that
        /// is capped narrower than its siblings simply ends up narrower, it does not make the
        /// parent narrower.
        /// </summary>
        public PointD MaxSize
        {
            get => _maxSize;
            set => SetProperty(ref _maxSize, value);
        }

        /// <summary>The size cap in device pixels. Zero on an axis still means no limit.</summary>
        protected PointD ScaledMaxSize => new PointD(
            _maxSize.X * LayoutScale,
            _maxSize.Y * LayoutScale);

        /// <summary>
        /// Applies <see cref="MaxSize"/> to a measured size. A control that overrides
        /// <see cref="CalculateSize"/> has to run its result through this, otherwise the cap is
        /// silently ignored for that control.
        /// </summary>
        protected PointD ClampToMaxSize(PointD measured)
        {
            PointD cap = ScaledMaxSize;

            return new PointD(
                cap.X > 0 ? Math.Min(measured.X, cap.X) : measured.X,
                cap.Y > 0 ? Math.Min(measured.Y, cap.Y) : measured.Y);
        }

        private bool _clipsChildren;
        /// <summary>
        /// Cut everything this control's descendants draw at its content box, and stop the
        /// layout from shrinking them to fit.
        ///
        /// Off by default, because it is not free and most controls do not need it: the layout
        /// already keeps a child's *box* inside its parent. What escapes is the drawing - a
        /// TextLabelControl calls ShowText and paints the whole string regardless of its box,
        /// and a blur writes wherever its radius reaches. Those are the cases this is for, plus
        /// scrolling, where the content is deliberately larger than the container.
        ///
        /// Turning it on changes two things at once, and they belong together:
        ///
        /// 1. Drawing is clipped to <see cref="ContentBox"/> - the control's box inset by its
        ///    padding, which is the same area the children were laid out into.
        /// 2. Children are no longer shrunk by the overflow check in the layout. They keep their
        ///    natural size and are cut visually instead. Without this a scrolled child would be
        ///    squashed to the visible height rather than sliding out of view.
        /// </summary>
        public bool ClipsChildren
        {
            get => _clipsChildren;
            set => SetProperty(ref _clipsChildren, value);
        }

        /// <summary>
        /// Whether this control wants every key while it is focused, instead of only the ones
        /// the dialog acts on itself.
        ///
        /// The default is false, and that is what keeps the game playable with a dialog open: we
        /// are called before the vanilla hotkey manager, so consuming keys we do not use would
        /// stop the player from opening their inventory. A text field would override this - and
        /// would additionally need a Harmony patch on ClientMain.OnKeyPress, because typed
        /// characters never reach the event API.
        /// </summary>
        public virtual bool WantsAllKeyboardInput => false;

        /// <summary>
        /// What this control's children add up to, in device pixels, as of the last measure
        /// pass - their sizes plus their margins, merged along <see cref="InsideOrientation"/>.
        /// Padding is not included; that is the difference between this and the control's own
        /// measured size when it auto sizes.
        ///
        /// This is the "how much content is there" number a scrolling container needs.
        /// </summary>
        public PointD MeasuredContentSize { get; protected set; }

        // Store the actual size before any clipping
        private PointD _calculatedSize;
        protected PointD CalculatedSize
        {
            get => _calculatedSize;
            set => _calculatedSize = value;
        }
        #endregion

        #region Constructors
        protected UIControl(
            string _Name = "",
            PointD? _Size = null,
            Orientation _Orientation = Orientation.None,
            double _Margin = 0,
            double _Padding = 0,
            int _Index = 0)
        {
            _name = _Name;
            _margin = _Margin;
            _padding = _Padding;
            _index = _Index;
            _insideOrientation = _Orientation;

            // Fill is the default because it is what every control did before this property
            // meant anything: stretched across its parent. Anything else here would silently
            // reshape every dialog that was written before the choice existed.
            _orientation = Orientation.Fill;

            if (_Size.HasValue)
            {
                _size = _Size.Value;
                _explicitSize = _Size.Value;
                _isAutoSize = _size.X == 0 && _size.Y == 0;
            }
            else
            {
                _isAutoSize = true;
            }

            _calculatedSize = _size;
        }
        #endregion

        #region Rendering
        /// <summary>
        /// Draws this control and all of its children onto the shared Cairo surface of the
        /// dialog. Do not upload anything to the GPU here - the dialog uploads the finished
        /// surface exactly once per refresh in <see cref="CustomDialogElement.RenderDialog"/>.
        /// </summary>
        public virtual void GenerateRenderData(ImageSurface surface, Context context)
        {
            if (Children.Count == 0)
                return;

            if (!ClipsChildren)
            {
                foreach (var child in Children)
                {
                    child.DrawProfiled(surface, context);
                }

                return;
            }

            // Non-null here by construction: EffectiveClip always includes our own content box
            // when we clip, and narrows it further by any clipping ancestor.
            LayoutRect clip = EffectiveClip()!.Value;

            // Fully clipped away - nothing below can be visible, so skip the subtree entirely.
            if (clip.IsEmpty)
                return;

            // Save/Restore is what scopes the clip: Cairo can only ever narrow a clip region,
            // never widen it, so it has to be undone rather than replaced. That also makes
            // nesting work - a clipping container inside another one ends up with the overlap.
            context.Save();
            context.Rectangle(clip.X, clip.Y, clip.Width, clip.Height);
            context.Clip();

            foreach (var child in Children)
            {
                child.DrawProfiled(surface, context);
            }

            context.Restore();
        }

        /// <summary>
        /// Draws a child, timing it when <see cref="Diagnostics.UIProfiler"/> is switched on.
        ///
        /// The child loop is the one place every control passes through on its way to the
        /// surface, so instrumenting it once here measures the whole tree without a line of
        /// profiling code in any control. Switched off it is a static bool read.
        /// </summary>
        private void DrawProfiled(ImageSurface surface, Context context)
        {
            // A hidden control draws nothing, and neither does anything below it. This is the
            // single gate for the whole subtree, which is why it sits in the child loop rather
            // than in every control's own GenerateRenderData.
            if (!IsVisible)
                return;

            // Whether *this* control is the one that turned the subtree off. Only the topmost
            // disabled control dims, otherwise nesting would stack the scrims and a disabled
            // panel would be darker where its disabled children are.
            bool dimsSubtree = !IsEnabled && (Parent == null || Parent.IsEffectivelyEnabled);

            if (!Diagnostics.UIProfiler.Enabled)
            {
                GenerateRenderData(surface, context);

                if (dimsSubtree)
                    DrawDisabledOverlay(context);

                return;
            }

            Diagnostics.UIProfiler.Scope scope = Diagnostics.UIProfiler.Begin();
            GenerateRenderData(surface, context);

            if (dimsSubtree)
                DrawDisabledOverlay(context);

            Diagnostics.UIProfiler.End("draw   " + GetType().Name, scope);
        }

        /// <summary>
        /// What a disabled control looks like. The default lays a wash of the dialog background
        /// colour over the control's box, which reads as "greyed out" over every control at
        /// once and, unlike redrawing each control in a second palette, cannot go out of step
        /// with what that control normally draws.
        ///
        /// Override it where a control can do better - a button that draws its own flat, unlit
        /// face, say. Drawing nothing is a valid override for a control that is disabled so
        /// often that dimming would be noise.
        /// </summary>
        protected virtual void DrawDisabledOverlay(Context ctx)
        {
            if (Size.X <= 0 || Size.Y <= 0)
                return;

            ctx.Save();
            ctx.Rectangle(Position.X, Position.Y, Size.X, Size.Y);
            ctx.SetSourceRGBA(
                GuiStyle.DialogDefaultBgColor[0],
                GuiStyle.DialogDefaultBgColor[1],
                GuiStyle.DialogDefaultBgColor[2],
                DisabledOverlayOpacity);
            ctx.Fill();
            ctx.Restore();
        }

        /// <summary>How much of the control the disabled wash covers.</summary>
        protected const double DisabledOverlayOpacity = 0.55;

        /// <summary>
        /// The area this control's children were laid out into: its own box inset by its padding.
        /// This is what <see cref="ClipsChildren"/> cuts at, and what the arrange pass stretches
        /// children across.
        ///
        /// Virtual because a scrolling container has to reserve a strip along its edge for the
        /// scrollbars - see <see cref="RectangleControl"/>. Everything that asks "where do my
        /// children go" goes through here, so narrowing it in one place narrows the clip, the
        /// stretching and the hit test together.
        /// </summary>
        public virtual LayoutRect ContentBox()
        {
            return PaddingBox();
        }

        /// <summary>
        /// The dialog this control is in was just shown. Override to do whatever has to happen
        /// once per opening rather than once per construction.
        ///
        /// An inventory grid needs it: the server only accepts slot moves for an inventory the
        /// player has opened, and that has to be announced on every open and taken back on every
        /// close. Always call the base so the rest of the subtree gets told too.
        /// </summary>
        public virtual void OnDialogShown()
        {
            foreach (UIControl child in Children)
            {
                child.OnDialogShown();
            }
        }

        /// <summary>The counterpart. Same rule about calling the base.</summary>
        public virtual void OnDialogHidden()
        {
            foreach (UIControl child in Children)
            {
                child.OnDialogHidden();
            }
        }

        /// <summary>
        /// The second drawing pass, run every frame after the Cairo surface has been put on
        /// screen. Draw here only what cannot be drawn with Cairo.
        ///
        /// It exists for one reason: an item stack is not a picture. The game draws it with
        /// <c>IRenderAPI.RenderItemstackToGui</c>, out of the block and item atlases, with its
        /// own shader and its own animation - none of which can land in a Cairo surface. Vanilla
        /// draws exactly the same line, between ComposeElements and RenderInteractiveElements.
        ///
        /// The cost is that anything drawn here is redrawn every frame and is not part of the
        /// cached surface, so keep it to what genuinely needs it. Coordinates here are *screen*
        /// coordinates - see <see cref="GetScreenPosition"/> - because the render API draws to
        /// the screen and not to our surface.
        /// </summary>
        public virtual void GenerateInteractiveRenderData(ICoreClientAPI api, float deltaTime)
        {
            // Same gate as the Cairo pass: a hidden subtree draws in neither of them. Without
            // this the item stacks of a hidden inventory grid would keep being drawn, because
            // they never go through the surface that the first pass skipped.
            if (!IsVisible)
                return;

            if (Children.Count == 0)
                return;

            // A Cairo clip cannot help here - this pass does not go through Cairo at all. The
            // GPU equivalent is the scissor rectangle, which is what vanilla uses for the same
            // job in its scrolling inventories.
            LayoutRect? clip = ClipsChildren ? EffectiveClip() : null;
            bool scissored = false;

            if (clip.HasValue)
            {
                if (clip.Value.IsEmpty)
                    return;

                ApplyScissor(api, clip.Value);
                scissored = true;
            }

            foreach (UIControl child in Children)
            {
                // Checked here as well as at the top of the method: a control that overrides
                // this pass and does not call the base - ItemSlotControl does exactly that -
                // would otherwise miss the gate entirely.
                if (!child.IsVisible)
                    continue;

                child.GenerateInteractiveRenderData(api, deltaTime);
            }

            if (scissored)
            {
                RestoreAncestorScissor(api);
            }
        }

        /// <summary>Turns a dialog local rectangle into the scissor box and switches it on.</summary>
        private void ApplyScissor(ICoreClientAPI api, LayoutRect clip)
        {
            PointD dialogPosition = Dialog?.Position ?? new PointD(0, 0);

            // GlScissor counts from the bottom left of the window, the layout from the top
            // left of the dialog.
            int left = (int)(dialogPosition.X + clip.X);
            int top = (int)(dialogPosition.Y + clip.Y);
            int width = (int)clip.Width;
            int height = (int)clip.Height;

            api.Render.GlScissor(left, api.Render.FrameHeight - (top + height), width, height);
            api.Render.GlScissorFlag(true);
        }

        /// <summary>
        /// Puts the scissor box back the way the caller left it.
        ///
        /// The scissor is one piece of global GL state, not a stack like the Cairo clip, so a
        /// clipping container nested in another one cannot simply switch it off when it is done:
        /// that would leave the rest of its parent's children drawing unclipped, spilling stacks
        /// out past the edge of a viewport that is still meant to cut them off. The state to
        /// return to is the clip of the nearest clipping ancestor, which
        /// <see cref="EffectiveClip"/> already knows how to work out.
        /// </summary>
        private void RestoreAncestorScissor(ICoreClientAPI api)
        {
            for (UIControl? ancestor = Parent; ancestor != null; ancestor = ancestor.Parent)
            {
                if (!ancestor.ClipsChildren)
                    continue;

                LayoutRect? outer = ancestor.EffectiveClip();

                if (outer.HasValue && !outer.Value.IsEmpty)
                {
                    ancestor.ApplyScissor(api, outer.Value);
                    return;
                }

                break;
            }

            api.Render.GlScissorFlag(false);
        }

        /// <summary>
        /// The area the arrange pass stretches children across.
        ///
        /// The same as <see cref="ContentBox"/> for everything that does not scroll, and the two
        /// are worth keeping apart precisely where they differ: ContentBox is what the player can
        /// see and what gets clipped, ArrangeBox is how much room the children get. A scrolling
        /// container makes the second one bigger than the first on the axis it scrolls.
        /// </summary>
        public virtual LayoutRect ArrangeBox()
        {
            return ContentBox();
        }

        /// <summary>
        /// The control's own box inset by its padding, with nothing else taken off. This is what
        /// <see cref="ContentBox"/> returns by default; an override that reserves space - a
        /// scrolling container taking a strip for its bars - starts from here rather than
        /// calling ContentBox again, which would recurse.
        /// </summary>
        protected LayoutRect PaddingBox()
        {
            double inset = ScaledPadding;

            return new LayoutRect(
                Position.X + inset,
                Position.Y + inset,
                Math.Max(0, Size.X - inset * 2),
                Math.Max(0, Size.Y - inset * 2));
        }

        /// <summary>
        /// The region this control is actually allowed to paint in, or null when nothing clips
        /// it - the overlap of the content boxes of every clipping ancestor, plus its own when
        /// it clips.
        ///
        /// Derived from the tree rather than passed down, so <see cref="GenerateRenderData"/>
        /// keeps its signature and a custom control written against the old one still compiles.
        /// It matters for anything that writes pixels behind Cairo's back: a Cairo clip is a
        /// property of the context, so <c>SurfaceTransformBlur</c>, which pokes at the surface
        /// buffer directly, does not see it and would smear past the edge of a viewport. Such a
        /// control has to intersect its own rectangle with this one - see
        /// <see cref="RectangleControl"/>.
        /// </summary>
        public LayoutRect? EffectiveClip()
        {
            LayoutRect? clip = ClipsChildren ? ContentBox() : (LayoutRect?)null;

            for (UIControl? ancestor = Parent; ancestor != null; ancestor = ancestor.Parent)
            {
                if (!ancestor.ClipsChildren)
                    continue;

                LayoutRect box = ancestor.ContentBox();
                clip = clip.HasValue ? clip.Value.Intersect(box) : box;
            }

            return clip;
        }
        #endregion

        #region Layout
        /// <summary>
        /// Runs a full layout pass over this control and everything below it: measure, then
        /// arrange (stretch, position, clip). Call this on the root of a tree.
        ///
        /// The pass is idempotent - running it twice on an unchanged tree has to produce the
        /// same result. LayoutHarness verifies that; if you add a control, keep it that way:
        /// measure must read <see cref="ExplicitSize"/>, never <see cref="Size"/>, and arrange
        /// must write through <see cref="SetLayoutSize"/>, never through the Size setter.
        /// </summary>
        public virtual void PerformLayout()
        {
            CalculateChildrenRelationship();
            CalculateSize();
            NormalizeChildrenByDelta();
            CalculateAllPositions();
        }
        #endregion

        /// <summary>
        /// Where this control sits on screen, in device pixels.
        ///
        /// Positions inside a tree are dialog local (the root sits at 0/0, which is the space the
        /// Cairo surface is drawn in), while the dialog itself carries the on screen position -
        /// so the two have to be added. Use this to place a popup at an anchor control: because
        /// the anchor position is recomputed by every layout pass, reading it again after the
        /// host moved gives the new position for free.
        /// </summary>
        public PointD GetScreenPosition()
        {
            CustomDialogElement? dialog = Dialog;

            // The root of a tree already holds a screen position.
            if (dialog == null || ReferenceEquals(dialog, this))
                return Position;

            return new PointD(dialog.Position.X + Position.X, dialog.Position.Y + Position.Y);
        }

        #region Hierarchy Management
        public void CalculateChildrenRelationship()
        {
            foreach (UIControl child in Children)
            {
                child._parent = this;
                child._dialog = this.Dialog;

                child.CalculateChildrenRelationship();
            }

            // Detach first: this method runs on every Show(), and subscribing again without
            // unsubscribing would fire the handler once per previous Show().
            Children.CollectionChanged -= Children_CollectionChanged;
            Children.CollectionChanged += Children_CollectionChanged;
        }

        private void Children_CollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            if (e.NewItems != null)
            {
                foreach (UIControl control in e.NewItems)
                {
                    if (control != null)
                    {
                        control.Parent = this;
                        control._dialog = this.Dialog;
                    }
                }
            }

            if (e.OldItems != null)
            {
                foreach (UIControl control in e.OldItems)
                {
                    if (control != null)
                    {
                        control.Parent = null;
                        control._dialog = null;
                    }
                }
            }

            RecomposeToMain();
        }

        /// <summary>
        /// Re-runs layout and redraws the dialog. Sizes alone are not enough - positions and
        /// the normalization pass depend on them, so a partial update would leave the tree in
        /// an inconsistent state.
        /// </summary>
        public void RecomposeToMain()
        {
            CustomDialogElement? dialog = Dialog;
            if (dialog == null || !dialog.IsVisible)
                return;

            // A control that changes a layout property from inside its own measure or arrange -
            // a list deciding it needs a scrollbar, say - would otherwise start a second pass
            // from within the first and never come back. The pass that is already running will
            // see the new value anyway if it has not measured that control yet, and if it has,
            // the change lands on the next one.
            if (dialog.IsLayingOut)
                return;

            Diagnostics.UIProfiler.Scope scope = Diagnostics.UIProfiler.Begin();

            dialog.PerformLayout();

            Diagnostics.UIProfiler.End("layout PerformLayout (from RecomposeToMain)", scope);

            dialog.Refresh();
        }
        #endregion

        #region Size Calculation
        /// <summary>
        /// Calculates the size of this control based on its children and settings.
        /// Does NOT apply clipping - that happens in position calculation.
        /// </summary>
        public virtual PointD CalculateSize()
        {
            // Children are measured in both branches - the arrange pass needs their sizes even
            // when they do not influence the size of this control.
            PointD content = new PointD(0, 0);

            foreach (UIControl child in Children)
            {
                // A hidden child collapses: it is not measured at all, so it costs nothing and
                // contributes nothing. Its own Size keeps whatever it had, which is what lets
                // it come back unchanged when it is shown again.
                if (!child.IsVisible)
                    continue;

                PointD childSize = child.MeasureProfiled();
                PointD childSizeWithSpacing = GetChildSizeWithSpacing(child, childSize);
                content = MergeSizeByOrientation(childSizeWithSpacing, content);
            }

            // A fixed-size control measures to exactly the size it was given. Reading the
            // current Size here instead of ExplicitSize is what used to make repeated layout
            // passes grow the control: Size is also the output of the arrange pass, so every
            // run folded the previous result plus the children plus the padding back in.
            PointD measured = ClampToMaxSize(IsAutoSize
                ? new PointD(content.X + ScaledPadding * 2, content.Y + ScaledPadding * 2)
                : ScaledExplicitSize);

            // Remember what the children add up to, before padding and before the control's own
            // size has any say. A scrolling container compares this against its viewport to
            // decide whether a bar is needed - and it has to be knowable here, in the measure
            // pass, rather than from the positions afterwards: a decision that only settled on
            // the second pass would make the layout non idempotent.
            MeasuredContentSize = content;

            _calculatedSize = measured;

            // Give the control a usable size right away. The arrange pass (normalization and
            // clipping) refines it afterwards through SetLayoutSize.
            SetLayoutSize(measured);

            return measured;
        }


        /// <summary>Measures a child, timing it when the profiler is switched on.</summary>
        private PointD MeasureProfiled()
        {
            if (!Diagnostics.UIProfiler.Enabled)
                return CalculateSize();

            Diagnostics.UIProfiler.Scope scope = Diagnostics.UIProfiler.Begin();
            PointD measured = CalculateSize();
            Diagnostics.UIProfiler.End("measure " + GetType().Name, scope);

            return measured;
        }

        /// <summary>
        /// Normalizes children sizes based on delta division of parent's available space.
        /// - Top/Bottom orientation: All children get parent's content width
        /// - Left/Right orientation: All children get parent's content height
        /// - None orientation: No normalization
        /// This is applied recursively to all descendants.
        /// </summary>
        public virtual void NormalizeChildrenByDelta()
        {
            Diagnostics.UIProfiler.Count("walk   NormalizeChildrenByDelta");

            if (Children.Count == 0)
                return;

            // The area the children are stretched across. Not ContentBox: that is the *visible*
            // area, and on an axis this control scrolls the children have to be laid out across
            // the whole content instead - otherwise a row could never be wider than the window
            // it is being scrolled through, which would make scrolling on that axis pointless.
            LayoutRect arrange = ArrangeBox();
            double availableWidth = arrange.Width;
            double availableHeight = arrange.Height;

            switch (InsideOrientation)
            {
                case Orientation.Top:
                case Orientation.Bottom:
                    // Vertical stacking: normalize width across all children
                    NormalizeChildrenWidth(availableWidth);
                    break;
                case Orientation.Left:
                case Orientation.Right:
                    // Horizontal stacking: normalize height across all children
                    NormalizeChildrenHeight(availableHeight);
                    break;
                case Orientation.None:
                    // No normalization for overlay mode
                    break;
            }

            // Down the tree, once per child.
            //
            // The stretching above only sets the children's own sizes; this is what carries the
            // pass into them, and it is deliberately the *only* place that does. Recursing from
            // inside the stretching as well - which is what used to happen - meant every level
            // walked its subtree twice, so the cost doubled per level: a nine level tree of two
            // hundred controls took thirteen thousand of these calls instead of two hundred, and
            // every one of them re-measured the text of every button it passed.
            foreach (UIControl child in Children)
            {
                if (!child.IsVisible)
                    continue;

                child.NormalizeChildrenByDelta();
            }
        }

        /// <summary>
        /// Normalizes all children to have the same width based on parent's available content width.
        /// Accounts for each child's margin when distributing space.
        /// </summary>
        private void NormalizeChildrenWidth(double availableWidth)
        {
            foreach (UIControl child in Children)
            {
                if (!child.IsVisible)
                    continue;

                // Only a child that asked to fill gets stretched. The other three alignments
                // are the whole reason for asking: they mean "leave me at the size I measured
                // to and put me at that end of the row", and stretching would make all four
                // look the same.
                if (child.CrossAlign != CrossAlignment.Fill)
                    continue;

                // Calculate width available for this child (subtract child's margins)
                double childAvailableWidth = availableWidth - (child.ScaledMargin * 2);

                // Ensure we don't set negative or zero width
                childAvailableWidth = Math.Max(1, childAvailableWidth);

                // Stretching would otherwise walk straight over a size cap, and the cap would
                // look like it works on the stacking axis and silently not across it.
                childAvailableWidth = child.ClampToMaxSize(new PointD(childAvailableWidth, 0)).X;

                // Stretching is part of arranging, so it goes through SetLayoutSize and
                // leaves the measured size (_calculatedSize) untouched - overwriting that
                // would destroy the natural size the next measure pass builds on.
                //
                // No recursion here: the caller walks into every child once, after all of them
                // have been given their width. See NormalizeChildrenByDelta.
                child.SetLayoutSize(new PointD(childAvailableWidth, child.Size.Y));
            }
        }

        /// <summary>
        /// Normalizes all children to have the same height based on parent's available content height.
        /// Accounts for each child's margin when distributing space.
        /// </summary>
        private void NormalizeChildrenHeight(double availableHeight)
        {
            foreach (UIControl child in Children)
            {
                if (!child.IsVisible)
                    continue;

                // Same as above, on the other axis.
                if (child.CrossAlign != CrossAlignment.Fill)
                    continue;

                // Calculate height available for this child (subtract child's margins)
                double childAvailableHeight = availableHeight - (child.ScaledMargin * 2);

                // Ensure we don't set negative or zero height
                childAvailableHeight = Math.Max(1, childAvailableHeight);

                // Same as above, on the other axis.
                childAvailableHeight = child.ClampToMaxSize(new PointD(0, childAvailableHeight)).Y;

                // Same as above: arrange writes the layout size, not the measured size, and the
                // walk into the children is the caller's.
                child.SetLayoutSize(new PointD(child.Size.X, childAvailableHeight));
            }
        }

        /// <summary>
        /// Adds margin and padding to a child's size for layout calculations.
        /// </summary>
        private PointD GetChildSizeWithSpacing(UIControl child, PointD childSize)
        {
            double totalMargin = 2 * child.ScaledMargin;
            //double totalPadding = 2 * this.Padding;

            return new PointD(
                childSize.X + totalMargin /*+ totalPadding*/,
                childSize.Y + totalMargin /*+ totalPadding*/
            );
        }

        /// <summary>
        /// Merges child size into current size based on orientation.
        /// - Top/Bottom: Stack vertically (add heights, take max width)
        /// - Left/Right: Stack horizontally (add widths, take max height)
        /// - None: Overlay (take max of both)
        /// </summary>
        private PointD MergeSizeByOrientation(PointD childSize, PointD currentSize)
        {
            switch (InsideOrientation)
            {
                case Orientation.Top:
                case Orientation.Bottom:
                    return new PointD(
                        Math.Max(currentSize.X, childSize.X),
                        currentSize.Y + childSize.Y
                    );

                case Orientation.Left:
                case Orientation.Right:
                    return new PointD(
                        currentSize.X + childSize.X,
                        Math.Max(currentSize.Y, childSize.Y)
                    );

                case Orientation.None:
                default:
                    return new PointD(
                        Math.Max(currentSize.X, childSize.X),
                        Math.Max(currentSize.Y, childSize.Y)
                    );
            }
        }
        #endregion

        #region Position Calculation
        /// <summary>
        /// Calculates positions for this control and all its children.
        /// </summary>
        public virtual void CalculateAllPositions()
        {
            Diagnostics.UIProfiler.Count("walk   CalculateAllPositions");

            // Root element starts at origin
            if (Parent == null)
            {
                Position = new PointD(0, 0);
            }

            // Calculate positions for all children.
            //
            // The "previous sibling" a child stacks after is the previous *visible* one, not
            // simply the one before it in the list. Hiding a control has to close the gap it
            // left, and stacking after a collapsed control would leave its two margins behind
            // as a stripe of nothing.
            UIControl? previousSibling = null;

            for (int i = 0; i < Children.Count; i++)
            {
                UIControl child = Children[i];

                if (!child.IsVisible)
                    continue;

                child.CalculatePosition(previousSibling);
                child.CalculateAllPositions();

                previousSibling = child;
            }

            // Normalize children after positions are calculated
            NormalizeChildrenByDelta();
        }

        /// <summary>
        /// Where this control goes: along the stacking axis after its previous visible sibling,
        /// and across that axis wherever its <see cref="Orientation"/> asked for.
        ///
        /// The two axes are decided separately and in that order, because they answer different
        /// questions. The stacking axis is not negotiable - it is the order of the children -
        /// while the cross axis is the only one a control gets a say over.
        /// </summary>
        public void CalculatePosition(UIControl? previousSibling)
        {
            Diagnostics.UIProfiler.Count("walk   CalculatePosition");

            if (Parent == null)
            {
                Position = new PointD(0, 0);
                return;
            }

            // The leading corner of the parent's content area, plus this control's own margin.
            // Both axes start here and only one of them moves.
            double posX = Parent.Position.X + Parent.ScaledPadding + ScaledMargin;
            double posY = Parent.Position.Y + Parent.ScaledPadding + ScaledMargin;

            // Along the stacking axis: after the previous visible sibling. The first child stays
            // at the leading corner, which is what the lack of a previous sibling means.
            if (previousSibling != null)
            {
                switch (Parent.InsideOrientation)
                {
                    case Orientation.Top:
                    case Orientation.Bottom:
                        // Stack vertically - keep X, add to Y
                        posY = previousSibling.Position.Y + previousSibling.Size.Y + previousSibling.ScaledMargin + ScaledMargin;
                        break;

                    case Orientation.Left:
                    case Orientation.Right:
                        // Stack horizontally - add to X, keep Y
                        posX = previousSibling.Position.X + previousSibling.Size.X + previousSibling.ScaledMargin + ScaledMargin;
                        break;
                    case Orientation.None:
                        // Overlay - use parent position (already set above)
                        break;
                }
            }

            // Across it: whatever Orientation resolved to. A Fill control was already stretched
            // to the whole width by the arrange pass, so there is no free space left to move it
            // through and this leaves it exactly where it is.
            LayoutRect arrange = Parent.ArrangeBox();

            switch (Parent.InsideOrientation)
            {
                case Orientation.Top:
                case Orientation.Bottom:
                    posX = AlignAcross(posX, arrange.Width, Size.X);
                    break;

                case Orientation.Left:
                case Orientation.Right:
                    posY = AlignAcross(posY, arrange.Height, Size.Y);
                    break;

                case Orientation.None:
                    // An overlay stacks in no direction, so it has no cross axis either.
                    break;
            }

            // Apply clipping if control extends beyond parent bounds
            PointD clippedSize = CalculateClippedSize(posX, posY);

            Position = new PointD(posX, posY);

            // Clipping is a layout result, not a wish - going through the Size setter here
            // would turn the clipped value into the ExplicitSize that the next measure pass
            // starts from, and the control would shrink a little more on every pass.
            SetLayoutSize(clippedSize);
        }

        /// <summary>
        /// Moves a control through the free space across the stacking axis, according to
        /// <see cref="CrossAlign"/>.
        /// </summary>
        /// <param name="start">The leading edge, margin already added.</param>
        /// <param name="available">
        /// The parent's extent across that axis - the same number the arrange pass stretches a
        /// Fill child to, so that End puts a control's trailing edge exactly where a Fill
        /// control's would have been.
        /// </param>
        /// <param name="size">This control's extent across that axis.</param>
        private double AlignAcross(double start, double available, double size)
        {
            double free = available - size - ScaledMargin * 2;

            // Nothing to move through. Also the case for a Fill control, which was stretched to
            // exactly this much, and for one that overflows its parent - where sliding it
            // further would only push it out on the other side.
            if (free <= 0)
                return start;

            switch (CrossAlign)
            {
                case CrossAlignment.End:
                    return start + free;

                case CrossAlignment.Center:
                    return start + free / 2;

                default:
                    return start;
            }
        }

        /// <summary>
        /// Calculates the clipped size when the control extends beyond parent bounds.
        /// </summary>
        private PointD CalculateClippedSize(double proposedX, double proposedY)
        {
            if (Parent == null)
            {
                return _calculatedSize;
            }

            // A clipping parent cuts the drawing instead, so the box is left at its natural
            // size. This is what makes content larger than its container possible at all: the
            // shrink below would otherwise squash a scrolled child to the visible area rather
            // than letting it slide out of view.
            if (Parent.ClipsChildren)
            {
                return Size;
            }

            // Parent boundaries (accounting for padding)
            double parentMinX = Parent.Position.X + Parent.ScaledPadding;
            double parentMinY = Parent.Position.Y + Parent.ScaledPadding;
            double parentMaxX = Parent.Position.X + Parent.Size.X - Parent.ScaledPadding;
            double parentMaxY = Parent.Position.Y + Parent.Size.Y - Parent.ScaledPadding;

            // Start from the size the control actually has after normalization. The overflow
            // test used to look at _calculatedSize while clamping Size, so it asked about one
            // box and cut a different one.
            double clippedWidth = Size.X;
            double clippedHeight = Size.Y;

            // Only clip if control starts outside bounds or extends significantly beyond
            // Don't clip right edge if control fits within reasonable margin tolerance
            if (proposedX + clippedWidth > parentMaxX + ScaledMargin)
            {
                clippedWidth = Math.Max(0, parentMaxX - proposedX);
            }

            // Only clip bottom edge if control extends significantly beyond
            if (proposedY + clippedHeight > parentMaxY + ScaledMargin)
            {
                clippedHeight = Math.Max(0, parentMaxY - proposedY);
            }

            // Clip left edge (if positioned before parent content area)
            if (proposedX < parentMinX)
            {
                clippedWidth = Math.Max(0, clippedWidth - (parentMinX - proposedX));
            }

            // Clip top edge (if positioned before parent content area)
            if (proposedY < parentMinY)
            {
                clippedHeight = Math.Max(0, clippedHeight - (parentMinY - proposedY));
            }

            return new PointD(clippedWidth, clippedHeight);
        }
        #endregion

        #region Hit Detection
        /// <summary>
        /// Performs hit testing to find which control is at the given screen coordinates.
        /// Returns null if the click is outside the dialog or no control is found.
        /// </summary>
        /// <param name="screenX">Screen X coordinate</param>
        /// <param name="screenY">Screen Y coordinate</param>
        /// <returns>The deepest control at the given position, or null if none found</returns>
        protected UIControl? HitTest(int screenX, int screenY)
        {
            // Only the root (the dialog) knows where the tree sits on screen. Everything below
            // it was laid out in dialog local space, so convert once here and stay local.
            double localX = screenX - Position.X;
            double localY = screenY - Position.Y;

            return HitTestGated(localX, localY);
        }

        /// <summary>
        /// Checks whether a point given in dialog local coordinates lies inside this control.
        /// The root of the tree is the exception: its Position holds the on screen position of
        /// the whole dialog, while its own local rectangle always starts at 0/0.
        /// </summary>
        public bool ContainsLocalPoint(double localX, double localY)
        {
            double left = Parent == null ? 0 : Position.X;
            double top = Parent == null ? 0 : Position.Y;

            return localX >= left &&
                   localX <= left + Size.X &&
                   localY >= top &&
                   localY <= top + Size.Y;
        }

        /// <summary>
        /// The control at a point given in the coordinates <paramref name="root"/> was laid out
        /// in - the same answer a click would get, without needing a click.
        ///
        /// Static, and taking the root as a parameter, for the same reason
        /// <see cref="FocusableControls"/> is: it lets the layout harness ask what is under a
        /// point without a dialog and therefore without the game. Mods can use it to find out
        /// what is under the cursor outside of an event.
        /// </summary>
        public static UIControl? ControlAt(UIControl root, double localX, double localY)
        {
            if (root == null)
                return null;

            return root.HitTestGated(localX, localY);
        }

        /// <summary>
        /// Recursively searches the control hierarchy for the deepest control at the given
        /// dialog local position. Children are tested last to first so that controls drawn on
        /// top of their siblings also win the hit test.
        /// </summary>
        protected virtual UIControl? HitTestRecursive(UIControl control, double localX, double localY)
        {
            if (!control.ContainsLocalPoint(localX, localY))
            {
                return null;
            }

            // What was clipped away is not there as far as the player is concerned, so it must
            // not be clickable either. Without this a row scrolled out of a viewport would still
            // take the click that lands where it used to be.
            if (control.ClipsChildren && !control.ContentBox().Contains(localX, localY))
            {
                return control;
            }

            for (int i = control.Children.Count - 1; i >= 0; i--)
            {
                UIControl child = control.Children[i];
                UIControl? hit = child.HitTestGated(localX, localY);
                if (hit != null)
                {
                    return hit;
                }
            }

            return control;
        }

        /// <summary>
        /// The gate every hit test goes through before <see cref="HitTestRecursive"/> gets a
        /// say. Not virtual, and that is the point.
        ///
        /// Ten controls override HitTestRecursive to be atomic - a button must be hovered as a
        /// button, not as the label inside it - and every one of them does so by returning
        /// itself without consulting anything else. Putting the visible and enabled rules
        /// inside that method would mean each of those overrides silently opting out of them,
        /// which is exactly the sort of thing nobody notices until a disabled button turns out
        /// to still be clickable. So the rules live in front of it, where an override cannot
        /// reach them, and HitTestRecursive stays what it is: the answer to "which of my parts
        /// is this", asked only once the control has agreed to be asked at all.
        /// </summary>
        protected UIControl? HitTestGated(double localX, double localY)
        {
            // Hidden: not there. The click carries on to whatever is behind, exactly as if the
            // control had been taken out of the tree.
            if (!IsVisible)
                return null;

            if (!ContainsLocalPoint(localX, localY))
                return null;

            // Disabled: there, but inert. It swallows the click rather than letting it through,
            // and the search stops here so that no enabled child inside a disabled panel can be
            // found. Refusing to *raise* anything on it is the dialog's job - see
            // CustomDialogElement.InteractiveTarget.
            if (!IsEnabled)
                return this;

            return HitTestRecursive(this, localX, localY);
        }
        #endregion

        #region Focus Traversal
        /// <summary>
        /// Every focusable control below <paramref name="root"/>, in tab order.
        ///
        /// Tab order is the order of the tree, depth first: a control comes before its children
        /// and before its later siblings. In a stacking layout that is the same as reading the
        /// dialog top to bottom, left to right, so there is nothing extra to maintain - moving a
        /// control in the tree moves it in the tab order too.
        ///
        /// Static, and taking the root as a parameter, so the layout harness can check the order
        /// without a dialog and therefore without the game.
        /// </summary>
        public static IEnumerable<UIControl> FocusableControls(UIControl root)
        {
            if (root == null)
                yield break;

            // Hidden and disabled subtrees drop out of the tab order whole. Walking into them
            // and filtering at the leaves would be wrong as well as wasteful: an enabled button
            // inside a disabled panel must not be reachable by Tab either, and the panel is the
            // only place that is known.
            if (!root.IsVisible || !root.IsEnabled)
                yield break;

            if (root.IsFocusable)
                yield return root;

            foreach (UIControl child in root.Children)
            {
                foreach (UIControl focusable in FocusableControls(child))
                {
                    yield return focusable;
                }
            }
        }

        /// <summary>
        /// The control Tab moves to, or Shift+Tab when <paramref name="backwards"/> is set.
        /// Wraps around at the ends. With nothing focused yet it returns the first control -
        /// the last one when going backwards - so the first Tab into a dialog lands sensibly.
        /// Returns null when there is nothing focusable at all.
        /// </summary>
        public static UIControl? NextFocusable(UIControl root, UIControl? current, bool backwards)
        {
            List<UIControl> order = new List<UIControl>(FocusableControls(root));

            if (order.Count == 0)
                return null;

            if (current == null)
                return backwards ? order[order.Count - 1] : order[0];

            int index = order.IndexOf(current);

            // The focused control is no longer in the tree - it was removed while focused, or it
            // belongs somewhere else entirely. Start over rather than getting stuck.
            if (index < 0)
                return backwards ? order[order.Count - 1] : order[0];

            int next = backwards ? index - 1 : index + 1;

            // Wrap. (index + count) keeps -1 from turning into a negative remainder.
            next = (next + order.Count) % order.Count;

            return order[next];
        }
        #endregion

        #region Property Change Notification
        /// <summary>
        /// The properties of this class whose change makes the laid out tree wrong, so that
        /// assigning one has to re-run measure and arrange before the next redraw.
        ///
        /// This is a list rather than "everything that notifies" for one specific reason: the
        /// arrange pass writes <see cref="Position"/>, and Position notifies. Invalidating on
        /// every notification would therefore make the layout re-enter itself forever. Anything
        /// added here must be an *input* of the layout, never a result of it.
        /// </summary>
        private static readonly HashSet<string> LayoutInvalidatingProperties =
            new HashSet<string>(StringComparer.Ordinal)
            {
                nameof(Size),
                nameof(IsAutoSize),
                nameof(Margin),
                nameof(Padding),
                nameof(MaxSize),
                nameof(InsideOrientation),
                nameof(Orientation),
                nameof(ClipsChildren),
                nameof(IsVisible),
            };

        /// <summary>
        /// Properties that change what the control looks like without moving anything. These
        /// only need the surface drawn again, which is a good deal cheaper than a layout pass.
        /// </summary>
        private static readonly HashSet<string> VisualInvalidatingProperties =
            new HashSet<string>(StringComparer.Ordinal)
            {
                nameof(IsEnabled),
                nameof(HasKeyboardFocus),
            };

        protected void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

            if (propertyName == null)
                return;

            // Handlers run first on purpose: a subscriber that reacts by changing something
            // else should have done so before we decide what to re-run, so that one gesture
            // costs one layout pass rather than two.
            if (LayoutInvalidatingProperties.Contains(propertyName))
            {
                InvalidateLayout();
            }
            else if (VisualInvalidatingProperties.Contains(propertyName))
            {
                InvalidateVisual();
            }
        }

        /// <summary>
        /// Says that this control's box may have changed, so the dialog has to measure and
        /// arrange again and then redraw. Does nothing while the control is not in an open
        /// dialog, which is what makes building a tree before showing it free.
        ///
        /// Controls call this after changing something of their own that the layout reads -
        /// a label's text, a list's item count. It is the same work
        /// <see cref="RecomposeToMain"/> does; the name says why rather than what.
        /// </summary>
        protected internal void InvalidateLayout()
        {
            RecomposeToMain();
        }

        /// <summary>
        /// Says that this control draws differently but occupies the same box - a hover, a
        /// colour, a checked state. Only the surface is rebuilt, and only once for the frame:
        /// <see cref="Custom.CustomDialogElement.Refresh"/> coalesces.
        /// </summary>
        protected internal void InvalidateVisual()
        {
            CustomDialogElement? dialog = Dialog;

            if (dialog == null || !dialog.IsVisible)
                return;

            dialog.Refresh();
        }

        protected bool SetProperty<T>(ref T field, T value, [System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
        {
            if (System.Collections.Generic.EqualityComparer<T>.Default.Equals(field, value))
                return false;

            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }
        #endregion
    }
}