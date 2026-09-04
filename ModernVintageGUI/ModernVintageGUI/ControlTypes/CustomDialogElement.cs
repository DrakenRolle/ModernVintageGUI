using Cairo;
using IS2Mod.ControlTypes.Renderer;
using IS2Mod.Enums;
using IS2Mod.Input;
using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.Client.NoObf;

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

        /// <summary>
        /// How much of the window a dialog may fill, per axis. 1.0 - the default - means it may
        /// use the whole thing but not more.
        ///
        /// This is the screen limit, and it deliberately does not go through
        /// <see cref="UIControl.MaxSize"/>: MaxSize is in author units and scales with the GUI
        /// slider, so it can never express "must fit on the screen". An auto sizing dialog grows
        /// linearly with the scale - the test dialog measures 270, 405 and 540 high at 1x, 1.5x
        /// and 2x - so without a limit anchored to the window a big dialog simply runs off the
        /// bottom at high scales, because centering clamps the corner to 0 and does not shrink.
        ///
        /// Clamping alone would leave the content overflowing and the overflow check would
        /// squash it to nothing, so a clamped dialog also clips. Put a scrolling container
        /// inside if the content genuinely needs more room than the screen has.
        /// </summary>
        public PointD MaxScreenFraction { get; set; } = new PointD(1.0, 1.0);
        #endregion

        #region Private Fields
        private CustomUIRenderer? _renderer;
        private CustomUIRenderer? _focusedRenderer;
        private PointD _requestedPosition;
        private readonly string _rendererId;
        private LoadedTexture? _cursorTexture;
        private bool _isDisposed;

        /// <summary>
        /// Something changed and the surface no longer matches the tree. Cleared by
        /// <see cref="EnsureRendered"/> at the start of the next frame.
        /// </summary>
        private bool _needsRedraw;
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
        /// <summary>
        /// Raised after the dialog has been shown and laid out. Use it for whatever has to
        /// happen around a dialog rather than inside a control - opening the inventory a grid in
        /// it works on, say, which has to be announced to the server and is therefore not the
        /// control's business.
        /// </summary>
        public event EventHandler? Shown;

        /// <summary>The counterpart, raised after the dialog has been hidden.</summary>
        public event EventHandler? Hidden;

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

            // After the layout, so a control that reacts to being shown already has its size and
            // position - and after registering, so it may open a popup of its own if it wants.
            OnDialogShown();

            Refresh();

            Shown?.Invoke(this, EventArgs.Empty);
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

            OnDialogHidden();

            // No need to re-grab the mouse by hand: once no dialog is registered any more the
            // patch stops interfering and UpdateFreeMouse restores the vanilla state on its own.
            UIManager.Current?.UnregisterDialog(this);

            Hidden?.Invoke(this, EventArgs.Empty);
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

            // A layout pass always invalidates the drawing - positions and sizes are exactly
            // what the surface was built from. Marking it here rather than relying on every
            // caller to also ask for a redraw is what makes editing the tree at runtime safe:
            // the two can no longer get out of step.
            _needsRedraw = true;

            // The arrange pass resets the root position to 0/0, so the on screen position has
            // to be re-applied afterwards either way.
            base.PerformLayout();

            ClampToScreen();

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
        /// Holds the dialog inside the window, and turns clipping on when it had to.
        ///
        /// Runs after the arrange pass, so the children have already been laid out at their full
        /// size; shrinking the root afterwards is what makes the excess overflow rather than
        /// re-flow. That is on purpose - a re-flow would need a second full pass and could
        /// oscillate, while clipping cannot.
        /// </summary>
        private void ClampToScreen()
        {
            double maxWidth = Api.Render.FrameWidth * MaxScreenFraction.X;
            double maxHeight = Api.Render.FrameHeight * MaxScreenFraction.Y;

            if (maxWidth <= 0 || maxHeight <= 0)
                return;

            if (Size.X <= maxWidth && Size.Y <= maxHeight)
                return;

            SetLayoutSize(new PointD(Math.Min(Size.X, maxWidth), Math.Min(Size.Y, maxHeight)));

            // Without this the part that no longer fits would be drawn outside the surface and,
            // worse, the overflow check would squash those children to nothing.
            ClipsChildren = true;
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

        /// <summary>
        /// Asks for a redraw. This only sets a flag - the surface is actually rebuilt once, at
        /// the start of the next frame, by <see cref="EnsureRendered"/>.
        ///
        /// Coalescing matters because a redraw is not cheap: it allocates a surface the size of
        /// the dialog, draws the whole tree onto it and uploads it to the GPU. Handlers call
        /// this liberally and several of them run for one gesture - moving the cursor from one
        /// button to the next is an Exit plus an Enter, and a click is a focus change plus
        /// MouseDown plus MouseUp plus whatever the caller subscribed. Rendering per call meant
        /// two to four full rebuilds where one frame is drawn.
        ///
        /// Vanilla does the same thing: GuiComposer.Render() checks recomposeOnRender and
        /// recomposes inside the render call, which is also why doing our drawing and our
        /// texture upload from the render stage is safe.
        /// </summary>
        public void Refresh()
        {
            if (!IsVisible)
                return;

            _needsRedraw = true;
        }

        /// <summary>
        /// Rebuilds the surface if anything asked for it since the last frame. Called by the
        /// renderer before it draws.
        ///
        /// Also covers the very first frame: a dialog that was just shown has no texture yet,
        /// and there is nothing to draw until this has run once.
        /// </summary>
        internal void EnsureRendered()
        {
            if (!IsVisible || _isDisposed)
                return;

            if (!_needsRedraw && StaticElementsTexture != null)
                return;

            _needsRedraw = false;
            RenderDialog();
        }
        #endregion

        #region Mouse Event Handling

        private UIControl? currentlyHovered = null;
        private UIControl? pressedControl = null;

        /// <summary>
        /// The control the cursor is on, or null when it is not on this dialog. Read only - the
        /// hover is tracked by the dialog as mouse events arrive.
        /// </summary>
        public UIControl? HoveredControl => currentlyHovered;

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

        /// <summary>
        /// Offers a wheel tick to the control under the cursor and then, if it did not use it,
        /// to each of its ancestors in turn.
        ///
        /// The bubbling is what makes a scrolling list work: the cursor is almost always over a
        /// row rather than over the list itself, and a row has no reason to care about the
        /// wheel. It also lets a list inside a list behave - the inner one scrolls until it hits
        /// its end, and only then does the outer one take over, because it stops marking the
        /// tick handled once there is nowhere left to go.
        /// </summary>
        public void HandleMouseWheel(MouseWheelEventArgs e)
        {
            if (!IsVisible || currentlyHovered == null)
                return;

            var args = new Events.MouseWheelEventArgs(e);

            for (UIControl? control = currentlyHovered; control != null; control = control.Parent)
            {
                control.InvokeEventMouseWheel(args);

                if (args.IsHandled)
                {
                    e.SetHandled(true);
                    return;
                }
            }

            // Nobody wanted it. Swallow it anyway when the dialog is modal, so the tick does not
            // reach through an open window and change the player's hotbar slot.
            if (IsModal)
            {
                e.SetHandled(true);
            }
        }
        #endregion

        #region Interactive rendering
        /// <summary>
        /// The depth the Cairo surface was drawn at this frame, set by the renderer just before
        /// the interactive pass runs.
        ///
        /// Everything drawn in that pass has to go in *front* of it. The ortho stage runs with
        /// the depth test on and GlDepthFunc(Lequal), and ClientMain.OrthoMode puts the model at
        /// z -19849 in a frustum of 0.4 to 20001 - so a larger z is nearer. A focused dialog of
        /// ours sits at 10000 to clear the whole vanilla GUI, which means vanilla's own item
        /// numbers (90 in a slot, 450 on the cursor) are far behind it.
        /// </summary>
        public float SurfaceRenderZ { get; internal set; }

        /// <summary>How far in front of the dialog surface a stack sitting in a slot is drawn.</summary>
        public const float SlotItemZOffset = 10f;

        /// <summary>
        /// The stack size a stack draws next to itself, relative to the stack.
        ///
        /// InventoryItemRenderer draws that number with <c>Render2DLoadedTexture(..., posZ + 100)</c>,
        /// a hundred in front of the model it belongs to. Anything meant to cover a stack in a
        /// slot therefore has to clear the slot by *more than a hundred*, or the model lands in
        /// front while the number stays behind - which looks exactly like it is: the count of the
        /// slot underneath printed over the item being carried.
        /// </summary>
        public const float StackSizeZOffset = 100f;

        /// <summary>
        /// And the stack on the cursor, which has to cover the slots and their numbers both.
        ///
        /// Vanilla's own two numbers are 90 for a stack in a slot and 450 for the one on the
        /// cursor - a gap of 360, and now it is clear why it is not 60: it has to be wider than
        /// the hundred the stack size adds.
        /// </summary>
        public const float HeldItemZOffset = SlotItemZOffset + 360f;

        /// <summary>
        /// Where the vanilla item tooltip is drawn again, in front of the dialog that would
        /// otherwise hide it.
        ///
        /// Only the base has to be given here: GuiElementItemstackInfo puts its own box at z 1000
        /// and the stack it previews above that, so the tooltip lands in front of everything else
        /// this dialog draws - the carried stack included, which is the order vanilla has too.
        /// </summary>
        public const float TooltipZOffset = SlotItemZOffset + StackSizeZOffset + 40f;

        /// <summary>
        /// Where the stack on the cursor sits relative to the pointer, in author units.
        ///
        /// Straight out of HudMouseTools: it puts its slot at <c>mouse + 5</c> with an alignment
        /// offset of <c>-48 * 0.25</c>, and the stack is drawn at the centre of that slot, so
        /// <c>5 - 12 + 24</c>. Guessing this is immediately obvious to the player - the item
        /// sits visibly off the cursor.
        /// </summary>
        public const double UnscaledMouseStackOffset = 5.0 - 12.0 + 24.0;

        /// <summary>
        /// Draws the controls that need the render API, and then the two things the game draws
        /// around the cursor and we would otherwise cover: the stack being carried, and the item
        /// tooltip.
        ///
        /// Both belong to HudMouseTools, which is part of the vanilla GUI renderer at order 1.0,
        /// while a focused dialog of ours draws above that at z 10000. So the game does draw
        /// them - behind our dialog, where the player cannot see them. Drawing them again on top
        /// costs nothing, because the copy underneath is hidden by the dialog anyway and the two
        /// land in exactly the same place.
        ///
        /// The condition is the same one the renderer uses to decide which side of the vanilla
        /// GUI to draw on, and it has to stay that way: a dialog that covers the vanilla copy
        /// without drawing one of its own swallows the item the player is carrying.
        /// </summary>
        public override void GenerateInteractiveRenderData(ICoreClientAPI api, float deltaTime)
        {
            base.GenerateInteractiveRenderData(api, deltaTime);

            if (!IsFocused && Layer != DialogRenderLayer.Overlay)
                return;

            RenderItemTooltip(api, deltaTime);
            RenderHeldStack(api, deltaTime);
        }

        private void RenderHeldStack(ICoreClientAPI api, float deltaTime)
        {
            IInventory? mouseInventory =
                api.World?.Player?.InventoryManager?.GetOwnInventory(GlobalConstants.mousecursorInvClassName);

            ItemSlot? held = mouseInventory?[0];
            if (held?.Itemstack == null)
                return;

            double offset = UnscaledMouseStackOffset * LayoutScale;
            double size = ItemSlotControl.UnscaledItemSize * LayoutScale;

            // In front of our own surface, and in front of the stacks sitting in slots - so
            // picking one up visibly lifts it out.
            api.Render.RenderItemstackToGui(
                held,
                api.Input.MouseX + offset,
                api.Input.MouseY + offset,
                SurfaceRenderZ + HeldItemZOffset,
                (float)size,
                ColorUtil.WhiteArgb,
                deltaTime);
        }

        /// <summary>The vanilla tooltip HUD, looked up once and kept.</summary>
        private GuiDialog? _mouseToolsHud;

        /// <summary>
        /// Draws the game's own item tooltip again, in front of this dialog.
        ///
        /// Rebuilding it here was the alternative and would have been the wrong kind of work:
        /// GuiElementItemstackInfo renders the stack, its name, its description, durability and
        /// the extended debug text, follows the player's tooltip setting and flips itself away
        /// from the screen edges. Redrawing the element the game already filled in gives all of
        /// that, and gives it identically.
        ///
        /// It is filled in from OnMouseEnterSlot, which <see cref="ItemSlotControl"/> raises -
        /// without that this draws nothing, because the game never learned which slot the cursor
        /// is on.
        ///
        /// Vanilla has already rendered and positioned it this frame, at order 1.0 to our 1.1,
        /// so the bounds are current and nothing has to be recomputed here.
        /// </summary>
        private void RenderItemTooltip(ICoreClientAPI api, float deltaTime)
        {
            GuiComposer? tooltip = FindTooltipComposer(api);
            if (tooltip == null)
                return;

            // A translation rather than a z argument: the composer decides the depth of every
            // piece it draws, and the model view matrix is what shifts the whole lot forwards.
            // It is how HudMouseTools lifts the same composer over the rest of the vanilla GUI,
            // with GlTranslate(0, 0, 160).
            api.Render.GlPushMatrix();
            api.Render.GlTranslate(0, 0, SurfaceRenderZ + TooltipZOffset);

            tooltip.Render(deltaTime);

            api.Render.GlPopMatrix();
        }

        private GuiComposer? FindTooltipComposer(ICoreClientAPI api)
        {
            if (_mouseToolsHud == null)
            {
                foreach (GuiDialog dialog in api.Gui.LoadedGuis)
                {
                    if (dialog is HudMouseTools)
                    {
                        _mouseToolsHud = dialog;
                        break;
                    }
                }
            }

            // Not there yet - the HUD builds its composers when the player data arrives.
            GuiComposer? composer = _mouseToolsHud?.Composers["itemstackinfo"];
            if (composer == null)
                return null;

            // The HUD's own answer to "is there anything to show": a source slot with a stack in
            // it. Without the check we would draw an empty composer every frame.
            return _mouseToolsHud!.IsOpened("itemstackinfo") ? composer : null;
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

        /// <summary>
        /// A typed character on its way to the focused control. Only a control that asked for
        /// every key gets one - a dialog full of buttons has no use for characters, and
        /// swallowing them there would stop the player from typing in the chat.
        /// </summary>
        public void HandleKeyPress(Events.KeyEventArgs e)
        {
            if (!IsVisible || FocusedControl?.WantsAllKeyboardInput != true)
                return;

            FocusedControl.InvokeEventKeyPress(e);
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
