using HarmonyLib;
using IS2Mod.Input;
using Vintagestory.Client.NoObf;

namespace IS2Mod.Patches
{
    /// <summary>
    /// ClientMain.UpdateFreeMouse() runs once per rendered frame (from MainRenderLoop) and
    /// recomputes MouseGrabbed purely from the number of *vanilla* GuiDialogs that are open:
    ///
    ///     MouseGrabbed = IsFocused &amp;&amp; ... &amp;&amp; ((DialogsOpened == 0 || ...) ^ togglemousecontrol)
    ///
    /// A custom dialog is not a GuiDialog, so DialogsOpened stays 0 and the game re-grabs the
    /// cursor on the very next frame - which is why setting MouseGrabbed = false in Show() had
    /// no lasting effect. While the mouse is grabbed the platform also warps the cursor back to
    /// the window center on every move, so the dialog would only ever see center coordinates.
    ///
    /// There is no API hook in that code path, hence the patch.
    ///
    /// This has to be a Prefix that *replaces* the method, not a Postfix that corrects it
    /// afterwards. Both MouseGrabbed setters have side effects that fire on every change of
    /// value, so letting the original assign true and then flipping it back to false would run
    /// them twice per frame:
    ///
    ///   - ClientPlatformWindows.MouseGrabbed warps the OS cursor to the window center whenever
    ///     the CursorState changes. Two changes per frame pin the cursor to the middle of the
    ///     screen and it rubber-bands back on every movement.
    ///   - ClientMain.MouseGrabbed calls player.inventoryMgr.DropMouseSlotItems(fullStack: true)
    ///     on every false -> true transition while no vanilla dialog is open, so the item held
    ///     on the mouse cursor would be dropped continuously.
    /// </summary>
    [HarmonyPatch(typeof(ClientMain), nameof(ClientMain.UpdateFreeMouse))]
    public static class ClientMainUpdateFreeMousePatch
    {
        /// <returns>
        /// false to skip the original method, which is what keeps the cursor free.
        /// </returns>
        [HarmonyPrefix]
        public static bool Prefix(ClientMain __instance)
        {
            UIManager? manager = UIManager.Current;
            if (manager == null || !manager.RequiresUngrabbedMouse)
            {
                return true; // no custom dialog open - let the game do its thing
            }

            // Guarded, so the setter (and with it the cursor warp) only runs on the actual
            // transition when the dialog opens, not once per frame.
            if (__instance.MouseGrabbed)
            {
                __instance.MouseGrabbed = false;
            }

            // The original sets this to (!MouseGrabbed && no dialog prefers a free mouse), i.e.
            // true for us. It is what lets the player still mine and place blocks with an
            // ungrabbed cursor, so it has to be cleared or the world reacts through the dialog.
            __instance.mouseWorldInteractAnyway = false;

            return false;
        }
    }
}
