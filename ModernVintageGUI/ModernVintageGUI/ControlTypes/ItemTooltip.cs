using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace IS2Mod.ControlTypes
{
    /// <summary>
    /// Tells the game which item slot the cursor is on, the way GuiElementItemSlotGridBase does
    /// it from its OnMouseMove.
    ///
    /// It is not decoration. HudMouseTools fills its tooltip from OnMouseEnterSlot, the item
    /// info panel of the survival inventory is drawn from it, and a mod listening on
    /// OnMouseEnterSlot reacts to it. A control that stays silent here looks right and behaves
    /// like a picture of an inventory.
    ///
    /// Shared by every <see cref="IS2Mod.Interfaces.IItemTooltipSource"/>, because getting it
    /// half right - announcing the arrival but not the departure - leaves a tooltip standing
    /// over a slot the cursor has long left.
    /// </summary>
    public static class ItemTooltip
    {
        public static void Announce(ICoreClientAPI? api, ItemSlot? slot, bool entered)
        {
            if (api == null || slot == null)
                return;

            if (entered)
            {
                api.Input.TriggerOnMouseEnterSlot(slot);
            }
            else
            {
                api.Input.TriggerOnMouseLeaveSlot(slot);
            }
        }
    }
}
