using HarmonyLib;
using IS2Mod.Input;
using Vintagestory.API.Client;
using Vintagestory.Client.NoObf;

namespace IS2Mod.Patches
{
    /// <summary>
    /// ClientMain.OnKeyPress() carries the characters the player actually typed - with the
    /// keyboard layout applied, so umlauts, accents and dead keys come out right - and it is the
    /// only place they exist.
    ///
    /// Unlike OnKeyDown and OnKeyUp it triggers nothing on IClientEventAPI: it walks straight
    /// into the client systems. So a text field in a framework that is not a vanilla GuiDialog
    /// cannot see a single character without this patch, which is why the framework had none
    /// until now.
    ///
    /// KeyDown is not a substitute. It carries a raw key code, so it cannot tell an "a" from an
    /// "A" and cannot produce an "ä" at all - a text field built on it works for ASCII and
    /// silently fails for half of Europe.
    ///
    /// A prefix rather than a postfix, and it returns false when one of our controls took the
    /// character: a key we consumed must not also reach the chat box or a vanilla hotkey.
    /// </summary>
    [HarmonyPatch(typeof(ClientMain), nameof(ClientMain.OnKeyPress))]
    public static class ClientMainKeyPressPatch
    {
        /// <returns>false to skip the original, which is what swallows the character.</returns>
        [HarmonyPrefix]
        public static bool Prefix(KeyEvent eventArgs)
        {
            UIManager? manager = UIManager.Current;

            if (manager == null || eventArgs == null)
            {
                return true;
            }

            return !manager.HandleKeyPress(eventArgs);
        }
    }
}
