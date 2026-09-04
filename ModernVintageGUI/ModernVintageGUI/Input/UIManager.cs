using IS2Mod.ControlTypes.Custom;
using IS2Mod.Enums;
using System;
using System.Collections.Generic;
using Vintagestory.API.Client;

namespace IS2Mod.Input
{
    /// <summary>
    /// Central input router for all <see cref="CustomDialogElement"/> instances.
    ///
    /// Why this exists: the game hands every entry in
    /// <c>ClientPlatformWindows.mouseEventHandlers</c> its own freshly allocated
    /// <see cref="MouseEvent"/>, so setting Handled on one of them cannot stop the game from
    /// also processing the click. The events on <see cref="IClientEventAPI"/> on the other
    /// hand are triggered by ClientMain *before* it forwards to its client systems and it
    /// aborts as soon as Handled is set - that is the only hook that can actually swallow
    /// input. Mutating mouseEventHandlers from a dialog was also unsafe, because the platform
    /// iterates that list with a foreach while dispatching.
    /// </summary>
    public class UIManager : IDisposable
    {
        /// <summary>
        /// The manager of the currently running client. Static because the Harmony patches
        /// need a cheap way to reach it from inside the game's render loop.
        /// </summary>
        public static UIManager? Current { get; private set; }

        /// <summary>The client setting the GUI scale slider writes to.</summary>
        private const string GuiScaleSettingKey = "guiScale";

        private readonly ICoreClientAPI _api;
        private readonly List<CustomDialogElement> _openDialogs = new List<CustomDialogElement>();
        private bool _isDisposed;

        /// <summary>
        /// True while at least one dialog is open that wants a free mouse cursor. Read every
        /// frame by the UpdateFreeMouse patch.
        /// </summary>
        public bool RequiresUngrabbedMouse { get; private set; }

        /// <summary>
        /// True while the cursor sits on an item slot of one of our dialogs.
        ///
        /// Read on every mouse movement by
        /// <see cref="IS2Mod.Patches.GuiManagerHoverSlotPatch"/>, which is where the reason it
        /// exists is written down: the game takes the hovered slot back on every move unless one
        /// of its own windows claims it, and none of ours is one of its windows.
        /// </summary>
        public bool IsItemSlotHovered
        {
            get
            {
                foreach (CustomDialogElement dialog in _openDialogs)
                {
                    if (dialog.IsVisible &&
                        dialog.HoveredControl is Interfaces.IItemTooltipSource source &&
                        source.TooltipSlot?.Itemstack != null)
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        public UIManager(ICoreClientAPI api)
        {
            _api = api ?? throw new ArgumentNullException(nameof(api));

            _api.Event.MouseDown += OnMouseDown;
            _api.Event.MouseUp += OnMouseUp;
            _api.Event.MouseMove += OnMouseMove;
            _api.Event.MouseWheelMove += OnMouseWheel;

            // ClientMain.OnKeyDown triggers these before the hotkey manager and before its own
            // client systems, and returns as soon as Handled is set - the same property the
            // mouse events have, and the reason we can close a dialog with Escape without the
            // pause menu also opening.
            _api.Event.KeyDown += OnKeyDown;
            _api.Event.KeyUp += OnKeyUp;

            // Same setting the game watches to update RuntimeEnv.GUIScale and recompose its own
            // dialogs. ISettings has no RemoveWatcher, so this handler outlives the manager -
            // hence the disposed guard in it.
            _api.Settings.AddWatcher<float>(GuiScaleSettingKey, OnGuiScaleChanged);

            Current = this;
        }

        #region Dialog registry
        public void RegisterDialog(CustomDialogElement dialog)
        {
            if (dialog == null || _openDialogs.Contains(dialog))
                return;

            _openDialogs.Add(dialog);

            // A window that just opened is the one the player is looking at.
            if (dialog.Layer == DialogRenderLayer.Normal)
            {
                FocusDialog(dialog);
            }

            UpdateMouseState();
        }

        /// <summary>
        /// Gives a dialog focus and takes it from all the others, which is what decides whether it
        /// draws above or below the vanilla GUI and who wins a click in an overlap. Moving it to
        /// the end of the list also makes it topmost for input, so both orders agree.
        /// </summary>
        public void FocusDialog(CustomDialogElement dialog)
        {
            foreach (CustomDialogElement other in _openDialogs)
            {
                other.IsFocused = ReferenceEquals(other, dialog);
            }

            if (_openDialogs.Remove(dialog))
            {
                _openDialogs.Add(dialog);
            }
        }

        /// <summary>
        /// Drops focus from all of our dialogs - the player clicked a vanilla window, so that one
        /// belongs on top now and ours go back below it.
        /// </summary>
        public void UnfocusAll()
        {
            foreach (CustomDialogElement dialog in _openDialogs)
            {
                dialog.IsFocused = false;
            }
        }

        /// <summary>The topmost of our visible dialogs under the given point, if any.</summary>
        private CustomDialogElement? TopMostAt(int x, int y)
        {
            foreach (CustomDialogElement dialog in DialogsTopMostFirst())
            {
                if (dialog.IsVisible && dialog.ContainsScreenPoint(x, y))
                    return dialog;
            }

            return null;
        }

        public void UnregisterDialog(CustomDialogElement dialog)
        {
            if (dialog == null || !_openDialogs.Remove(dialog))
                return;

            UpdateMouseState();
        }

        /// <summary>
        /// The player moved the GUI scale slider. Every open dialog has to be laid out and
        /// redrawn at the new scale right away - otherwise it keeps the old geometry until it is
        /// closed and reopened.
        /// </summary>
        private void OnGuiScaleChanged(float newScale)
        {
            if (_isDisposed)
                return;

            foreach (CustomDialogElement dialog in DialogsTopMostFirst())
            {
                dialog.OnGuiScaleChanged(newScale);
            }
        }

        private void UpdateMouseState()
        {
            bool required = false;
            foreach (CustomDialogElement dialog in _openDialogs)
            {
                if (dialog.IsVisible && dialog.PrefersUngrabbedMouse)
                {
                    required = true;
                    break;
                }
            }

            RequiresUngrabbedMouse = required;
        }
        #endregion

        #region Input routing
        /// <summary>
        /// True when the cursor sits on top of an open vanilla dialog (inventory, block GUI,
        /// escape menu, ...). Those get the event instead of us.
        ///
        /// This is deliberately a per-point test and not "is any vanilla dialog open": we are
        /// called from the event API *before* the game forwards to its client systems, so a
        /// blanket opt-out would leave our own dialogs dead - no hover, no clicks - for as long
        /// as any vanilla GUI is open somewhere else on the screen.
        ///
        /// HUD elements (hotbar, health bar) are not considered. They are not modal, they cover
        /// large parts of the screen, and letting them block our dialogs would bring back the
        /// same problem in a smaller form.
        /// </summary>
        private bool PointerOverVanillaDialog(int x, int y)
        {
            foreach (GuiDialog gui in _api.Gui.OpenedGuis)
            {
                if (gui.DialogType != EnumDialogType.Dialog || !gui.ShouldReceiveMouseEvents())
                    continue;

                foreach (GuiComposer composer in gui.Composers.Values)
                {
                    if (composer?.Bounds != null && composer.Bounds.PointInside(x, y))
                        return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Dialogs are asked topmost first, i.e. in reverse registration order. The snapshot
        /// is required because a handler may open or close a dialog while we iterate.
        /// </summary>
        private CustomDialogElement[] DialogsTopMostFirst()
        {
            var snapshot = new CustomDialogElement[_openDialogs.Count];
            for (int i = 0; i < snapshot.Length; i++)
            {
                snapshot[i] = _openDialogs[_openDialogs.Count - 1 - i];
            }
            return snapshot;
        }

        /// <summary>
        /// Closes every open dialog that asked to be dismissed by an outside click and does not
        /// contain the point. This cannot live in the dialog itself: a click outside is never
        /// delivered there, because the hit test finds nothing and the event moves on.
        /// </summary>
        /// <returns>true when something was closed, so the caller can swallow the click.</returns>
        private bool DismissPopups(int x, int y)
        {
            bool closedAny = false;
            bool insideOne = false;

            // Topmost first, and stop at the first dialog the click landed in. That handles
            // cascades: clicking a sub menu entry stops before the parent menu, so the parent
            // survives, while clicking the parent closes only the sub menu below it.
            //
            // Snapshot, because Hide() unregisters and would mutate the list we are walking.
            foreach (CustomDialogElement dialog in DialogsTopMostFirst())
            {
                if (!dialog.IsVisible)
                    continue;

                if (dialog.ContainsScreenPoint(x, y))
                {
                    insideOne = true;
                    break;
                }

                if (!dialog.CloseOnOutsideClick)
                    continue;

                dialog.Hide();
                closedAny = true;
            }

            // Only a click that landed on nothing of ours gets eaten by the dismissal. A click
            // inside one of our dialogs has to go through, otherwise picking another entry while
            // a sub menu is open would silently do nothing.
            return closedAny && !insideOne;
        }

        private void OnMouseDown(MouseEvent e)
        {
            // Whoever is drawn on top gets the click. Ours is on top only while it has focus -
            // a popup always is, since it is transient and must cover everything.
            CustomDialogElement? ours = TopMostAt(e.X, e.Y);
            bool oursOnTop = ours != null && (ours.Layer == DialogRenderLayer.Overlay || ours.IsFocused);

            if (!oursOnTop && PointerOverVanillaDialog(e.X, e.Y))
            {
                // The player clicked a vanilla window that covers us - it takes focus, we drop it.
                UnfocusAll();
                CancelPress();
                return;
            }

            // The click that dismisses a menu is consumed by the dismissal, so it does not also
            // press whatever sits underneath. This is what makes clicking the opener a toggle.
            if (DismissPopups(e.X, e.Y))
            {
                CancelPress();
                e.Handled = true;
                return;
            }

            // Clicking one of our dialogs brings it to the front, the way the game does it.
            if (ours != null && ours.Layer == DialogRenderLayer.Normal)
            {
                FocusDialog(ours);
            }

            foreach (CustomDialogElement dialog in DialogsTopMostFirst())
            {
                dialog.HandleMouseDown(e);
                if (e.Handled) return;
            }
        }

        /// <summary>
        /// The dialog holding a mouse capture, if any. While a drag is running it gets movement
        /// and the release no matter where the cursor went - past the hit test, past the vanilla
        /// dialog check and past every other dialog.
        /// </summary>
        private CustomDialogElement? CapturingDialog()
        {
            foreach (CustomDialogElement dialog in DialogsTopMostFirst())
            {
                if (dialog.IsVisible && dialog.CapturedControl != null)
                    return dialog;
            }

            return null;
        }

        private void OnMouseUp(MouseEvent e)
        {
            CustomDialogElement? capturing = CapturingDialog();
            if (capturing != null)
            {
                capturing.HandleMouseUp(e);
                return;
            }

            if (PointerOverVanillaDialog(e.X, e.Y))
            {
                CancelPress();
                return;
            }

            foreach (CustomDialogElement dialog in DialogsTopMostFirst())
            {
                dialog.HandleMouseUp(e);
                if (e.Handled) return;
            }
        }

        /// <summary>
        /// Routes movement to our dialogs - and then always hands the event on to the game.
        ///
        /// A dialog marks a move as handled to stop the ones underneath it from also hovering,
        /// which is right among ourselves and wrong towards the game. The event API aborts as
        /// soon as Handled is set, so a swallowed move never reaches ClientMain's client systems
        /// - and HudMouseTools moves the stack the player is carrying from its OnMouseMove. The
        /// result is the bug this clearing fixes: the moment the cursor enters one of our
        /// dialogs the game stops being told where it is, and the item stays behind, drawn at
        /// the edge of the dialog.
        ///
        /// Nothing is lost by letting the move through. A press is still swallowed, which is
        /// what keeps the click off the world and out of the GUI underneath; movement on its own
        /// changes nothing while the cursor is free.
        /// </summary>
        private void OnMouseMove(MouseEvent e)
        {
            RouteMouseMove(e);

            e.Handled = false;
        }

        private void RouteMouseMove(MouseEvent e)
        {
            CustomDialogElement? capturing = CapturingDialog();
            if (capturing != null)
            {
                capturing.HandleMouseMove(e);
                return;
            }

            if (PointerOverVanillaDialog(e.X, e.Y))
            {
                // The cursor moved onto a vanilla GUI. Drop our hover state, otherwise the
                // control it was last over stays visually hovered until the cursor comes back.
                foreach (CustomDialogElement dialog in DialogsTopMostFirst())
                {
                    dialog.ClearHoverState(e);
                }
                return;
            }

            foreach (CustomDialogElement dialog in DialogsTopMostFirst())
            {
                dialog.HandleMouseMove(e);
                if (e.Handled) return;
            }
        }

        private void OnMouseWheel(MouseWheelEventArgs e)
        {
            // Wheel events carry no coordinates, so use the last known cursor position.
            if (PointerOverVanillaDialog(_api.Input.MouseX, _api.Input.MouseY))
                return;

            foreach (CustomDialogElement dialog in DialogsTopMostFirst())
            {
                dialog.HandleMouseWheel(e);
                if (e.IsHandled) return;
            }
        }

        /// <summary>
        /// The dialog that currently owns the keyboard, or null when the game does.
        ///
        /// An open popup wins over everything: it is transient, it is drawn on top, and Escape
        /// has to close it before it closes the dialog underneath. Deepest first, so Escape in a
        /// cascade closes one sub menu per press rather than the whole chain at once.
        ///
        /// Otherwise it is the focused dialog - the same one that draws above the vanilla GUI.
        /// With none of ours focused the player clicked a vanilla window or is looking at the
        /// world, and the keyboard is not ours to take.
        /// </summary>
        private CustomDialogElement? KeyboardTarget()
        {
            foreach (CustomDialogElement dialog in DialogsTopMostFirst())
            {
                if (dialog.IsVisible && dialog.Layer == DialogRenderLayer.Overlay)
                    return dialog;
            }

            foreach (CustomDialogElement dialog in DialogsTopMostFirst())
            {
                if (dialog.IsVisible && dialog.IsFocused)
                    return dialog;
            }

            return null;
        }

        /// <summary>
        /// A typed character, handed over by
        /// <see cref="IS2Mod.Patches.ClientMainKeyPressPatch"/> before the game sees it.
        ///
        /// Characters are the one input the event API does not carry, so this is the only way in
        /// - and the patch has to know whether we took it, because a character we consumed must
        /// not also land in the chat box.
        /// </summary>
        /// <returns>true when one of our controls took it.</returns>
        public bool HandleKeyPress(KeyEvent e)
        {
            CustomDialogElement? target = KeyboardTarget();

            if (target == null)
                return false;

            var args = new ControlTypes.Events.KeyEventArgs(e);
            target.HandleKeyPress(args);

            if (args.Handled)
            {
                e.Handled = true;
            }

            return args.Handled;
        }

        private void OnKeyDown(KeyEvent e)
        {
            CustomDialogElement? target = KeyboardTarget();
            if (target == null)
                return;

            var args = new ControlTypes.Events.KeyEventArgs(e);
            target.HandleKeyDown(args);

            // Only copy a true back. Writing false would clear a flag somebody upstream set.
            if (args.Handled)
                e.Handled = true;
        }

        private void OnKeyUp(KeyEvent e)
        {
            CustomDialogElement? target = KeyboardTarget();
            if (target == null)
                return;

            var args = new ControlTypes.Events.KeyEventArgs(e);
            target.HandleKeyUp(args);

            if (args.Handled)
                e.Handled = true;
        }

        /// <summary>
        /// Forget any in-progress press so releasing the button over a vanilla dialog does not
        /// later complete as a click on one of our controls.
        /// </summary>
        private void CancelPress()
        {
            foreach (CustomDialogElement dialog in DialogsTopMostFirst())
            {
                dialog.CancelPress();
            }
        }
        #endregion

        public void Dispose()
        {
            if (_isDisposed)
                return;

            _isDisposed = true;

            _api.Event.MouseDown -= OnMouseDown;
            _api.Event.MouseUp -= OnMouseUp;
            _api.Event.MouseMove -= OnMouseMove;
            _api.Event.MouseWheelMove -= OnMouseWheel;
            _api.Event.KeyDown -= OnKeyDown;
            _api.Event.KeyUp -= OnKeyUp;

            _openDialogs.Clear();
            RequiresUngrabbedMouse = false;

            if (Current == this)
                Current = null;
        }
    }
}
