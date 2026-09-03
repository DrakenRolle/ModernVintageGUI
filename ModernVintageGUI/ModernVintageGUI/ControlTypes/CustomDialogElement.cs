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
        #endregion

        #region Private Fields
        private CustomUIRenderer? _renderer;
        private readonly string _rendererId;
        private LoadedTexture? _cursorTexture;
        private bool _isDisposed;
        #endregion

        #region Constructor
        public CustomDialogElement(
            ICoreClientAPI capi,
            string _DialogName,
            string _Title = "")
            : base(_Orientation: Orientation.Top, _Margin: 0, _Padding: 10)
        {
            Dialog = this;
            DialogName = _DialogName;
            Title = _Title;
            Api = capi;
            MousePosition = new Vec2i();
            _rendererId = $"customdialog_{_DialogName}_{Guid.NewGuid()}";

            // Register renderer
            RegisterRenderer();
        }
        #endregion

        #region Renderer Management

        private void RegisterRenderer()
        {
            _renderer = new CustomUIRenderer(Api, this);
            Api.Event.RegisterRenderer(_renderer, EnumRenderStage.Ortho, _rendererId);
        }

        private void UnregisterRenderer()
        {
            if (_renderer != null)
            {
                Api.Event.UnregisterRenderer(_renderer, EnumRenderStage.Ortho);
                _renderer?.Dispose();
                _renderer = null;
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

        private void DrawDialogBackground(Context context)
        {
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

            // Drop stale hover/press state so a reopened dialog does not start out believing
            // the cursor is still on the control it was on when it closed.
            currentlyHovered = null;
            pressedControl = null;

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
        public void SetPosition(double x, double y)
        {
            Position = new PointD(x, y);
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

            base.PerformLayout();
            CenterOnScreen();
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

            clickedControl?.InvokeEventMouseDown(e);

            if (clickedControl != null || (IsModal && ContainsScreenPoint(e.X, e.Y)))
            {
                e.Handled = true;
            }
        }

        public void HandleMouseUp(MouseEvent e)
        {
            if (!IsVisible)
                return;

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
