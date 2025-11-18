using Cairo;
using IS2Mod.ControlTypes.Renderer;
using IS2Mod.Enums;
using System;
using Vintagestory.API.Client;
using Vintagestory.API.MathTools;
using Vintagestory.Client;
using Vintagestory.Client.NoObf;

namespace IS2Mod.ControlTypes.Custom
{
    public class CustomDialogElement : UIControl, IDisposable, MouseEventHandler
    {
        #region Properties
        public string DialogName { get; set; }
        public string Title { get; set; }
        public ICoreClientAPI Api { get; private set; }
        public bool IsVisible { get; private set; }
        public Vec2i MousePosition { get; set; }
        #endregion

        #region Private Fields
        private CustomUIRenderer _renderer;
        private readonly string _rendererId;
        private LoadedTexture _cursorTexture;
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
            // Dispose old texture
            StaticElementsTexture?.Dispose();

            // Create new texture
            StaticElementsTexture = new LoadedTexture(Api)
            {
                Width = (int)Size.X,
                Height = (int)Size.Y
            };

            using (ImageSurface surface = new ImageSurface(Format.Argb32, (int)Size.X, (int)Size.Y))
            using (Context context = GuiElement.GenContext(surface))
            {
                // Draw dialog background
                DrawDialogBackground(context);

                // Generate render data for all children
                GenerateRenderData(surface, context);

                // Convert to texture
                var cairoTexture = new LoadedTexture(Api);
                Api.Gui.LoadOrUpdateCairoTexture(surface, linearMag: true, intoTexture: ref cairoTexture);
                StaticElementsTexture = cairoTexture;
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

            // Setup hierarchy
            CalculateChildrenRelationship();

            // Release mouse
            var client = (ClientMain)Api.World;
            client.mouseOverrideGrab = true;
            client.MouseGrabbed = false;

            var platform = ((ClientPlatformWindows)ScreenManager.Platform);
            platform.MouseGrabbed = false;

            platform.mouseEventHandlers.Add(this);
            // Update state
            IsVisible = true;

            // Calculate layout (must happen after hierarchy is set up)
            CalculateSize();
            NormalizeChildrenByDelta();
            CalculateAllPositions();
            // Center on screen (after size is known)
            CenterOnScreen();

            Refresh();
        }

        public void Hide()
        {
            if (!IsVisible)
                return;

            // Restore mouse
            var client = (ClientMain)Api.World;
            client.mouseOverrideGrab = false;

            var platform = ((ClientPlatformWindows)ScreenManager.Platform);
            platform.MouseGrabbed = true;
            platform.mouseEventHandlers.Remove(this);

            IsVisible = false;
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
            RenderDialog();
        }
        #endregion

        #region Mouse Event Handlers (Placeholder implementations)

        // Fixed Mouse Event Handlers for CustomDialogElement

        private UIControl currentlyHovered = null;
        private UIControl pressedControl = null;

        public void OnMouseDown(MouseEvent e)
        {
            UIControl clickedControl = HitTest(e.X, e.Y);

            if (clickedControl != null)
            {
                pressedControl = clickedControl;
                clickedControl.InvokeEventMouseDown(e);
                e.Handled = true;
            }
            else
            {
                pressedControl = null;
                // Clicked outside controls or outside dialog
            }
        }

        public void OnMouseUp(MouseEvent e)
        {
            UIControl releasedControl = HitTest(e.X, e.Y);

            // Invoke mouse up on the control under cursor
            if (releasedControl != null)
            {
                releasedControl.InvokeEventMouseUp(e);
                e.Handled = true;
            }

            // Check if this is a complete click (mouse down and up on same control)
            if (pressedControl != null && releasedControl == pressedControl)
            {
                pressedControl.InvokeEventClicked(e);
                e.Handled = true;
            }

            // Clear pressed state
            pressedControl = null;
        }

        public void OnMouseMove(MouseEvent e)
        {
            UIControl controlUnderMouse = HitTest(e.X, e.Y);

            // Check if we moved to a different control
            if (controlUnderMouse != currentlyHovered)
            {
                // Mouse left previous control
                if (currentlyHovered != null)
                {
                    currentlyHovered.InvokeEventExit(e);
                }

                // Mouse entered new control
                if (controlUnderMouse != null)
                {
                    controlUnderMouse.InvokeEventEnter(e);
                }

                // Update current hover state
                currentlyHovered = controlUnderMouse;
            }

            // Always invoke mouse move on currently hovered control
            if (currentlyHovered != null)
            {
                currentlyHovered.InvokeEventMouseMove(e);
                e.Handled = true;
            }
        }

        public void OnMouseWheel(MouseWheelEventArgs e)
        {
            // Use current hovered control for mouse wheel events
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
                // Unregister renderer
                UnregisterRenderer();

                // Dispose textures
                StaticElementsTexture?.Dispose();
                StaticElementsTexture = null;

                _cursorTexture?.Dispose();
                _cursorTexture = null;

                // Hide if visible
                if (IsVisible)
                    Hide();
            }

            _isDisposed = true;
        }

        ~CustomDialogElement()
        {
            Dispose(false);
        }
        #endregion
    }
}