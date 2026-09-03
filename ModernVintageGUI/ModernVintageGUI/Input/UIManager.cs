using IS2Mod.ControlTypes.Custom;
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

        public UIManager(ICoreClientAPI api)
        {
            _api = api ?? throw new ArgumentNullException(nameof(api));

            _api.Event.MouseDown += OnMouseDown;
            _api.Event.MouseUp += OnMouseUp;
            _api.Event.MouseMove += OnMouseMove;
            _api.Event.MouseWheelMove += OnMouseWheel;

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
            UpdateMouseState();
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

        private void OnMouseDown(MouseEvent e)
        {
            if (PointerOverVanillaDialog(e.X, e.Y))
            {
                CancelPress();
                return;
            }

            foreach (CustomDialogElement dialog in DialogsTopMostFirst())
            {
                dialog.HandleMouseDown(e);
                if (e.Handled) return;
            }
        }

        private void OnMouseUp(MouseEvent e)
        {
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

        private void OnMouseMove(MouseEvent e)
        {
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

            _openDialogs.Clear();
            RequiresUngrabbedMouse = false;

            if (Current == this)
                Current = null;
        }
    }
}
