using HarmonyLib;
using IS2Mod.Input;
using System.Reflection;
using Vintagestory.API.Client;
using Vintagestory.Client.NoObf;

namespace IS2Mod.Patches
{
    /// <summary>
    /// GuiManager.OnMouseMove() takes back the hovered slot on every single mouse movement,
    /// which is what kept the item tooltip from ever appearing over one of our slots:
    ///
    /// <code>
    /// didHoverSlotEventTrigger = false;                 // ...whatever was announced is forgotten
    /// foreach (GuiDialog d in game.LoadedGuis) { ... }  // ...only vanilla windows get the move
    /// OnMouseMoveOver(null);                            // ...nobody hovered a slot -> leave it
    /// </code>
    ///
    /// The order is what makes it fatal. IClientEventAPI.MouseMove is triggered by ClientMain
    /// *before* it forwards to its client systems, so a slot of ours announces itself first and
    /// the manager wipes the flag a moment later - and since no window of ours is a GuiDialog,
    /// nothing sets it again. The result: OnMouseMoveOver fires TriggerOnMouseLeaveSlot for the
    /// slot that was just entered. Hovering never showed a tooltip at all, while clicking a slot
    /// did - a click is followed by no movement, so nothing came along to take it back.
    ///
    /// The fix is to tell the manager the truth it cannot see: while the cursor sits on one of
    /// our item slots, a hover event *did* happen this move. Everything else about the method is
    /// left alone, including the bookkeeping of which dialog is moused over.
    ///
    /// Making our dialogs vanilla GuiDialogs would be the other way out, and that is the whole
    /// framework thrown away for one flag.
    /// </summary>
    [HarmonyPatch]
    public static class GuiManagerHoverSlotPatch
    {
        private const string MethodName = "OnMouseMoveOver";
        private const string FieldName = "didHoverSlotEventTrigger";

        /// <summary>
        /// A private method and a private field, both of which a future version may rename. Not
        /// finding them turns the patch off and costs a tooltip; patching blind would throw
        /// while the mod is loading and cost the mod.
        /// </summary>
        public static bool Prepare()
        {
            return Target() != null && AccessTools.Field(typeof(GuiManager), FieldName) != null;
        }

        public static MethodBase? TargetMethod()
        {
            return Target();
        }

        private static MethodInfo? Target()
        {
            return AccessTools.Method(typeof(GuiManager), MethodName, new[] { typeof(GuiDialog) });
        }

        [HarmonyPrefix]
        public static void Prefix(ref bool ___didHoverSlotEventTrigger)
        {
            if (UIManager.Current?.IsItemSlotHovered == true)
            {
                ___didHoverSlotEventTrigger = true;
            }
        }
    }
}
