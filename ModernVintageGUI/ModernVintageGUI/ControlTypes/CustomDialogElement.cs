using Cairo;
using IS2Mod.ControlTypes.Renderer;
using IS2Mod.Enums;
using IS2Mod.Input;
using System;
using Vintagestory.API.Client;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace IS2Mod.ControlTypes.Custom
{
    public class CustomDialogElement : UIControl, IDisposable
    {
        #region Properties
        public string DialogName { get; set; }
        public string Title { get; set; }
        public ICoreClientAPI Api { get; private set; }
        public bool IsVisible { get; private set; }
        public Vec2i MousePosition { get; set; }

        /// <summary>
        /// While this dialog is open the mouse cursor is released and world interaction is
        /// suppressed. See <see cref="IS2Mod.Patches.ClientMainUpdateFreeMousePatch"/>.
        /// </summary>
        public bool PrefersUngrabbedMouse { get; set; } = true;

        /// <summary>
        /// Swallow clicks that land on the dialog background as well, so they do not fall
        /// through to vanilla GUIs or to the world.
        /// </summary>
        public bool IsModal { get; set; } = true;

        /// <summary>The render band this dialog was created in.</summary>
        public DialogRenderLayer Layer { get; }

        /// <summary>
        /// Whether this dialog currently has focus. A focused dialog draws above the vanilla GUI
        /// and takes clicks in the overlap; an unfocused one draws below it and yields them.
        /// That is the rule the game applies to its own windows, and <see cref="Input.UIManager"/>
        /// keeps it in sync when the player clicks.
        /// </summary>
        public bool IsFocused { get; internal set; }

        /// <summary>
        /// Re-center the dialog on every layout pass. Turn this off for anything that is
        /// positioned by its opener - a context menu at its anchor, a tooltip at the cursor.
        /// <see cref="ShowAt"/> does that for you.
        /// </summary>
        public bool AutoCenter { get; set; } = true;

        /// <summary>
        /// Draw the vanilla style dialog background. Off gives a fully transparent surface, so a
        /// popup can consist of nothing but its own controls.
        /// </summary>
        public bool DrawsBackground { get; set; } = true;

        /// <summary>
        /// Close this dialog when a mouse button goes down anywhere outside of it. This is what
        /// makes a context menu dismissable; <see cref="Input.UIManager"/> applies it, because a
        /// click outside never reaches this dialog through the normal event path.
        /// </summary>
        public bool CloseOnOutsideClick { get; set; }

        /// <summary>
        /// Close this dialog when Escape is pressed while it owns the keyboard. Turn it off for
        /// a dialog the player must dismiss deliberately.
        ///
        /// With this off the key is not consumed either, so Escape falls through to the game and
        /// opens the pause menu - the same thing it does when no dialog of ours is focused.
        /// </summary>
        public bool CloseOnEscape { get; set; } = true;
        #endregion

        #region Private Fields
        private CustomUIRenderer? _renderer;
        private CustomUIRenderer? _focusedRenderer;
        private PointD _requestedPosition;
        private readonly string _rendererId;
        private LoadedTexture? _cursorTexture;
        private bool _isDisposed;
        #endregion

        #region Constructor
        /// <param name="_Layer">
        /// Which render band this dialog draws in. Has to be decided here rather than through a
        /// property, because the game sorts its renderer list when the renderer is registered
        /// and never re-sorts it. Popups (context menus, dropdowns, tooltips) belong in
        /// <see cref="DialogRenderLayer.Overlay"/> so they cover ordinary dialogs.
        /// </param>
        public CustomDialogElement(
            ICoreClientAPI capi,
            string _DialogName,
            string _Title = "",
            DialogRenderLayer _Layer = DialogRenderLayer.Normal)
            : base(_Orientation: Orientation.Top, _Margin: 0, _Padding: 10)
        {
            Dialog = this;
            DialogName = _DialogName;
            Title = _Title;
            Api = capi;
            Layer = _Layer;
            MousePosition = new Vec2i();
            _rendererId = $"customdialog_{_DialogName}_{Guid.NewGuid()}";

            // Register renderer
            RegisterRenderer();
        }
        #endregion

        #region Renderer Management

        /// <summary>
        /// Two renderers, one below the vanilla GUI and one above it. Which of them draws is
        /// decided per frame by <see cref="IsFocused"/>.
        ///
        /// It has to be two registrations rather than one whose order changes: the game sorts
        /// its renderer list when a renderer is registered and never re-sorts it, so moving a
        /// dialog between the bands would mean unregistering and registering again - in the
        /// middle of input handling, while the render loop may be walking that very list.
        /// </summary>
        private void RegisterRenderer()
        {
            int sequence = CustomUIRenderer.NextSequence(Layer);

            _renderer = new CustomUIRenderer(Api, this, Layer, aboveVanilla: false, sequence);
            Api.Event.RegisterRenderer(_renderer, EnumRenderStage.Ortho, _rendererId);

            _focusedRenderer = new CustomUIRenderer(Api, this, Layer, aboveVanilla: true, sequence);
            Api.Event.RegisterRenderer(_focusedRenderer, EnumRenderStage.Ortho, _rendererId + "_focused");
        }

        private void UnregisterRenderer()
        {
            if (_renderer != null)
            {
                Api.Event.UnregisterRenderer(_renderer, EnumRenderStage.Ortho);
                _renderer.Dispose();
                _renderer = null;
            }

            if (_focusedRenderer != null)
            {
                Api.Event.UnregisterRenderer(_focusedRenderer, EnumRenderStage.Ortho);
                _focusedRenderer.Dispose();
                _focusedRenderer = null;
            }
        }
        #endregion

        #region Rendering
        public void RenderDialog()
        {
            int width = Math.Max(1, (int)Size.X);
            int height = Math.Max(1, (int)Size.Y);

            using (ImageSurface surface = new ImageSurface(Format.Argb32, width, height))
            using (Context context = GuiElement.GenContext(surface))
            {
                // Draw dialog background
                DrawDialogBackground(context);

                // Let every control draw itself onto the shared surface
                GenerateRenderData(surface, context);

                // The upload reads surface.DataPtr directly, so pending Cairo drawing
                // operations have to be committed to the backing buffer first.
                surface.Flush();

                // A single upload of the finished surface. LoadOrUpdateCairoTexture reuses the
                // GL texture of the passed LoadedTexture, so repeated refreshes do not leak.
                LoadedTexture texture = StaticElementsTexture ?? new LoadedTexture(Api);
                Api.Gui.LoadOrUpdateCairoTexture(surface, linearMag: true, intoTexture: ref texture);
                StaticElementsTexture = texture;
            }
        }

        protected virtual void DrawDialogBackground(Context context)
        {
            if (!DrawsBackground)
                return;

            // Draw rounded rectangle
            GuiElement.RoundRectangle(
                context,
                0,
                0,
                Size.X,
                Size.Y,
                GuiStyle.DialogBGRadius
            );




            // Fill with background color
            context.SetSourceRGBA(
                GuiStyle.DialogStrongBgColor[0],
                GuiStyle.DialogStrongBgColor[1],
                GuiStyle.DialogStrongBgColor[2],
                GuiStyle.DialogStrongBgColor[3]
            );
            context.FillPreserve();

            // Apply texture pattern
            SurfacePattern pattern = GuiElement.getPattern(
                Api,
                GuiElement.dirtTextureName,
                doCache: true,
                64,
                0.125f
            );
            context.SetSource(pattern);
            context.FillPreserve();
        }
        #endregion

        #region Visibility Management
        public void Show()
        {
            if (IsVisible)
                return;

            IsVisible = true;

            PerformLayout();

            // The mouse is deliberately not released here: UpdateFreeMouse would overwrite that
            // on the very next frame. Registering with the UIManager makes the Harmony patch
            // keep the cursor free for as long as this dialog stays open.
            UIManager.Current?.RegisterDialog(this);

            Refresh();
        }

        public void Hide()
        {
            if (!IsVisible)
                return;

            IsVisible = false;

            // Drop stale hover/press state so a reopened dialog does not start out believing the
            // cursor is still on the control it was on when it closed.
            //
            // Clearing the fields is not enough: a control paints its own hover look and only
            // undoes that when it gets Exit. Without this the last control the cursor was on -
            // the entry that was just clicked, typically - stays lit the next time the dialog is
            // shown. IsVisible is already false here, so the redraw those handlers ask for is a
            // no-op; the corrected state is drawn by the Refresh in Show().
            ClearHoverState(new MouseEvent(MousePosition.X, MousePosition.Y));
            pressedControl = null;
            ReleaseMouseCapture();

            // Same reasoning for the keyboard: a control paints its own focus ring and only
            // removes it on LostFocus, so a dialog closed while something was focused would come
            // back with a stale ring on it.
            FocusControl(null);

            // No need to re-grab the mouse by hand: once no dialog is registered any more the
            // patch stops interfering and UpdateFreeMouse restores the vanilla state on its own.
            UIManager.Current?.UnregisterDialog(this);
        }

        public void Toggle()
        {
            if (IsVisible)
                Hide();
            else
                Show();
        }
        #endregion

        #region Position and Update
        /// <summary>
        /// Places the dialog at a screen position. The value is remembered, because the arrange
        /// pass resets the root position to 0/0 on every layout - without remembering it, a
        /// dialog that does not auto-center would jump to the top left corner.
        /// </summary>
        public void SetPosition(double x, double y)
        {
            _requestedPosition = new PointD(x, y);
            Position = _requestedPosition;
        }

        /// <summary>
        /// Opens the dialog at a screen position instead of centered - what a context menu wants
        /// at its anchor point. Use <see cref="UIControl.GetScreenPosition"/> on the anchor
        /// control to get that position.
        /// </summary>
        public void ShowAt(double screenX, double screenY)
        {
            AutoCenter = false;
            SetPosition(screenX, screenY);
            Show();
        }

        /// <summary>
        /// Runs the full layout pass. Children are laid out in dialog local space (root at
        /// 0/0) because that is the space the Cairo surface is drawn in; only afterwards is the
        /// dialog itself moved to its position on screen.
        /// </summary>
        public override void PerformLayout()
        {
            LayoutAt(RuntimeEnv.GUIScale);
        }

        private void LayoutAt(double scale)
        {
            LayoutScale = scale;

            // The arrange pass resets the root position to 0/0, so the on screen position has
            // to be re-applied afterwards either way.
            base.PerformLayout();

            if (AutoCenter)
            {
                CenterOnScreen();
            }
            else
            {
                Position = _requestedPosition;
            }
        }

        /// <summary>
        /// Re-lays out and redraws the dialog for a changed GUI scale while it is open. Vanilla
        /// does the same thing from its own watcher on that setting - it calls
        /// GuiComposers.MarkAllDialogsForRecompose(), which our dialogs are not part of.
        ///
        /// The new value is passed in rather than read from RuntimeEnv.GUIScale: the game
        /// updates that field from its own watcher on the same setting, and watchers run in
        /// registration order, so ours may well run first and still see the old value.
        /// </summary>
        public void OnGuiScaleChanged(double newScale)
        {
            // A hidden dialog needs no work - Show() lays it out at the scale current then.
            if (!IsVisible || _isDisposed)
                return;

            LayoutAt(newScale);
            Refresh();
        }

        /// <summary>
        /// Centers the dialog on the screen.
        /// </summary>
        public void CenterOnScreen()
        {
            double screenWidth = Api.Render.FrameWidth;
            double screenHeight = Api.Render.FrameHeight;

            double x = (screenWidth - Size.X) / 2;
            double y = (screenHeight - Size.Y) / 2;

            // Ensure dialog is not positioned off-screen
            x = Math.Max(0, x);
            y = Math.Max(0, y);

            Position = new PointD(x, y);
        }

        public void Refresh()
        {
            if (!IsVisible)
                return;

            RenderDialog();
        }
        #endregion

        #region Mouse Event Handling

        private UIControl? currentlyHovered = null;
        private UIControl? pressedControl = null;

        /// <summary>
        /// Screen space bounds test. The Position of the dialog is in screen coordinates while
        /// all of its descendants live in dialog local coordinates.
        /// </summary>
        public bool ContainsScreenPoint(double screenX, double screenY)
        {
            return screenX >= Position.X &&
                   screenX <= Position.X + Size.X &&
                   screenY >= Position.Y &&
                   screenY <= Position.Y + Size.Y;
        }

        public void HandleMouseDown(MouseEvent e)
        {
            if (!IsVisible)
                return;

            UIControl? clickedControl = HitTest(e.X, e.Y);
            pressedControl = clickedControl;

            // Clicking a focusable control gives it the keyboard, the way every desktop UI
            // does it. Clicking anything else - the background, the title bar - deliberately
            // leaves focus where it was instead of clearing it, so dragging a dialog around
            // does not cost the player their place in the tab order.
            if (clickedControl != null && clickedControl.IsFocusable)
            {
                FocusControl(clickedControl);
            }

            clickedControl?.InvokeEventMouseDown(e);

            if (clickedControl != null || (IsModal && ContainsScreenPoint(e.X, e.Y)))
            {
                e.Handled = true;
            }
        }

        /// <summary>
        /// The control that is currently receiving all mouse movement and the next release,
        /// regardless of where the cursor actually is. Needed for anything that is dragged: the
        /// cursor leaves the control almost immediately, and without capture the hit test would
        /// hand the events to whatever is underneath instead.
        /// </summary>
        public UIControl? CapturedControl { get; private set; }

        public void CaptureMouse(UIControl control)
        {
            CapturedControl = control;
        }

        public void ReleaseMouseCapture()
        {
            CapturedControl = null;
        }

        public void HandleMouseUp(MouseEvent e)
        {
            if (!IsVisible)
                return;

            if (CapturedControl != null)
            {
                UIControl captured = CapturedControl;
                ReleaseMouseCapture();

                captured.InvokeEventMouseUp(e);

                // Releasing without having left the control still counts as a click, so a
                // draggable control can also just be clicked.
                if (HitTest(e.X, e.Y) == captured)
                {
                    captured.InvokeEventClicked(e);
                }

                pressedControl = null;
                e.Handled = true;
                return;
            }

            UIControl? releasedControl = HitTest(e.X, e.Y);

            releasedControl?.InvokeEventMouseUp(e);

            // A click is only complete when press and release happened on the same control
            if (pressedControl != null && ReferenceEquals(releasedControl, pressedControl))
            {
                pressedControl.InvokeEventClicked(e);
            }

            pressedControl = null;

            if (releasedControl != null || (IsModal && ContainsScreenPoint(e.X, e.Y)))
            {
                e.Handled = true;
            }
        }

        public void HandleMouseMove(MouseEvent e)
        {
            if (!IsVisible)
                return;

            MousePosition = new Vec2i(e.X, e.Y);

            // While something is being dragged it keeps every move, wherever the cursor is.
            if (CapturedControl != null)
            {
                CapturedControl.InvokeEventMouseMove(e);
                e.Handled = true;
                return;
            }

            UIControl? controlUnderMouse = HitTest(e.X, e.Y);

            if (!ReferenceEquals(controlUnderMouse, currentlyHovered))
            {
                currentlyHovered?.InvokeEventExit(e);
                controlUnderMouse?.InvokeEventEnter(e);
                currentlyHovered = controlUnderMouse;
            }

            if (currentlyHovered != null)
            {
                currentlyHovered.InvokeEventMouseMove(e);
                e.Handled = true;
            }
        }

        /// <summary>
        /// Sends Exit to the currently hovered control and forgets it. Used when the cursor
        /// moves somewhere this dialog no longer receives events from, e.g. onto a vanilla GUI.
        /// </summary>
        public void ClearHoverState(MouseEvent e)
        {
            if (currentlyHovered == null)
                return;

            currentlyHovered.InvokeEventExit(e);
            currentlyHovered = null;
        }

        /// <summary>
        /// Forgets an in-progress press without completing it as a click.
        /// </summary>
        public void CancelPress()
        {
            pressedControl = null;
        }

        public void HandleMouseWheel(MouseWheelEventArgs e)
        {
            if (!IsVisible)
                return;

            if (currentlyHovered != null)
            {
                currentlyHovered.InvokeEventMouseWheel(e);
                e.SetHandled(true);
            }
        }
        #endregion

        #region Keyboard Handling
        /// <summary>
        /// The control inside this dialog that receives keys, or null when none does. Set by
        /// clicking a focusable control and by Tab; see <see cref="FocusControl"/>.
        /// </summary>
        public UIControl? FocusedControl { get; private set; }

        /// <summary>
        /// Moves the keyboard focus, raising LostFocus on the old control and GotFocus on the
        /// new one. Pass null to take focus away from everything.
        /// </summary>
        public void FocusControl(UIControl? control)
        {
            if (ReferenceEquals(FocusedControl, control))
                return;

            UIControl? previous = FocusedControl;

            // Assign before raising, so a handler that asks the dialog what is focused during
            // the switch sees the new state rather than a half applied one.
            FocusedControl = control;

            if (previous != null)
            {
                previous.HasKeyboardFocus = false;
                previous.InvokeLostFocus();
            }

            if (control != null)
            {
                control.HasKeyboardFocus = true;
                control.InvokeGotFocus();
            }

            Refresh();
        }

        /// <summary>
        /// Moves focus to the next focusable control, or the previous one for Shift+Tab.
        /// </summary>
        /// <returns>false when the dialog has nothing focusable, so the caller can let the key
        /// through instead of swallowing it for no effect.</returns>
        public bool MoveFocus(bool backwards)
        {
            UIControl? next = NextFocusable(this, FocusedControl, backwards);
            if (next == null)
                return false;

            FocusControl(next);
            return true;
        }

        /// <summary>
        /// Routes a key press: first to the focused control, then to the dialog's own bindings.
        ///
        /// Only keys that actually did something are marked handled. That is not politeness, it
        /// is required: <see cref="Input.UIManager"/> is called from ClientMain before the
        /// vanilla hotkey manager runs, so consuming a key we have no use for would stop the
        /// player from opening their inventory while this dialog is focused.
        /// </summary>
        public void HandleKeyDown(Events.KeyEventArgs e)
        {
            if (!IsVisible)
                return;

            FocusedControl?.InvokeEventKeyDown(e);
            if (e.Handled)
                return;

            // A control that takes over the keyboard - a text field - keeps everything else the
            // dialog would otherwise interpret, except Escape, which has to stay a way out.
            if (FocusedControl?.WantsAllKeyboardInput == true && e.Key != GlKeys.Escape)
            {
                e.Handled = true;
                return;
            }

            switch (e.Key)
            {
                case GlKeys.Escape:
                    if (!CloseOnEscape)
                        break;

                    Hide();
                    e.Handled = true;
                    break;

                case GlKeys.Tab:
                    e.Handled = MoveFocus(backwards: e.ShiftPressed);
                    break;

                // In a stacking layout the tab order runs down the dialog, so the arrow keys
                // along that axis are the same movement and players expect them to work.
                case GlKeys.Down:
                    e.Handled = MoveFocus(backwards: false);
                    break;

                case GlKeys.Up:
                    e.Handled = MoveFocus(backwards: true);
                    break;

                case GlKeys.Enter:
                case GlKeys.KeypadEnter:
                case GlKeys.Space:
                    if (FocusedControl == null)
                        break;

                    FocusedControl.PerformClick();
                    e.Handled = true;
                    break;
            }
        }

        public void HandleKeyUp(Events.KeyEventArgs e)
        {
            if (!IsVisible)
                return;

            FocusedControl?.InvokeEventKeyUp(e);

            // Match the press: a key we consumed going down must be consumed coming up as well,
            // otherwise the hotkey manager sees a release without a press.
            if (!e.Handled && FocusedControl?.WantsAllKeyboardInput == true && e.Key != GlKeys.Escape)
            {
                e.Handled = true;
            }
        }
        #endregion

        #region Dispose Pattern
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_isDisposed)
                return;

            if (disposing)
            {
                // Hide first: it only touches managed state and has to run before the renderer
                // and the textures are gone.
                if (IsVisible)
                    Hide();

                UnregisterRenderer();

                StaticElementsTexture?.Dispose();
                StaticElementsTexture = null;

                _cursorTexture?.Dispose();
                _cursorTexture = null;
            }

            _isDisposed = true;
        }
        #endregion
    }
}
