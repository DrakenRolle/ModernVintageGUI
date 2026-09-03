using System;
using Vintagestory.API.Client;

namespace IS2Mod.ControlTypes.Events
{
    /// <summary>
    /// A key press or release on its way through the control tree.
    ///
    /// Unlike <see cref="MouseEventArgs"/> this is created once per event by the dialog and
    /// handed down unchanged, because <see cref="Handled"/> has to travel back up: the dialog
    /// copies it onto the game's KeyEvent, and only then does the game stop forwarding the key
    /// to its hotkey manager and client systems.
    ///
    /// There is deliberately no KeyPress here. ClientMain.OnKeyPress - the one carrying typed
    /// characters with the keyboard layout applied - does not trigger anything on
    /// IClientEventAPI, so character input is not reachable without a second Harmony patch.
    /// Everything in this class comes from KeyDown/KeyUp, which are triggered.
    /// </summary>
    public class KeyEventArgs : EventArgs
    {
        /// <summary>The raw key code. Compare against <see cref="Key"/> instead.</summary>
        public int KeyCode { get; }

        /// <summary>The second key when two were pressed in quick succession.</summary>
        public int? KeyCode2 { get; }

        /// <summary>The key that was pressed.</summary>
        public GlKeys Key => (GlKeys)KeyCode;

        /// <summary>
        /// The character the game associated with the key. Not usable for text input - it comes
        /// from the raw key and not from the keyboard layout, so no umlauts and no dead keys.
        /// </summary>
        public char KeyChar { get; }

        public bool CtrlPressed { get; }
        public bool ShiftPressed { get; }
        public bool AltPressed { get; }
        public bool CommandPressed { get; }

        /// <summary>
        /// Set this to stop the key from reaching anything else - other controls, the vanilla
        /// hotkeys, the game itself. Leave it alone for keys you do not use, otherwise the
        /// player cannot open their inventory while one of our dialogs is focused.
        /// </summary>
        public bool Handled { get; set; }

        public KeyEventArgs(KeyEvent vsKeyEvent)
        {
            KeyCode = vsKeyEvent.KeyCode;
            KeyCode2 = vsKeyEvent.KeyCode2;
            KeyChar = vsKeyEvent.KeyChar;
            CtrlPressed = vsKeyEvent.CtrlPressed;
            ShiftPressed = vsKeyEvent.ShiftPressed;
            AltPressed = vsKeyEvent.AltPressed;
            CommandPressed = vsKeyEvent.CommandPressed;
        }

        /// <summary>Synthesises an event, for tests and for driving a dialog from code.</summary>
        public KeyEventArgs(
            GlKeys key,
            bool shift = false,
            bool ctrl = false,
            bool alt = false,
            char keyChar = '\0')
        {
            KeyCode = (int)key;
            KeyChar = keyChar;
            ShiftPressed = shift;
            CtrlPressed = ctrl;
            AltPressed = alt;
        }
    }
}
