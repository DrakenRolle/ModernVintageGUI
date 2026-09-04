using Vintagestory.API.Common;

namespace IS2Mod.Interfaces
{
    /// <summary>
    /// A control that stands for an item stack and wants the game's own item tooltip on hover -
    /// an inventory slot, an entry of an item dropdown, a recipe output preview.
    ///
    /// Two things hang off this, and both are why it is an interface rather than a check for
    /// ItemSlotControl:
    ///
    /// 1. The control announces the slot with
    ///    <see cref="IS2Mod.ControlTypes.ItemTooltip.Announce"/> when the cursor arrives and
    ///    leaves. That is what fills HudMouseTools, which is what draws the tooltip.
    /// 2. <see cref="IS2Mod.Input.UIManager.IsItemSlotHovered"/> reports it, and the
    ///    <see cref="IS2Mod.Patches.GuiManagerHoverSlotPatch"/> needs that report: the game takes
    ///    the hovered slot back on every mouse movement unless one of its own windows claims it,
    ///    and none of ours is one of its windows.
    ///
    /// A control that skips this looks right and shows no tooltip, which is the bug that keeps
    /// coming back - hence the contract.
    /// </summary>
    public interface IItemTooltipSource
    {
        /// <summary>
        /// The slot to describe, or null when there is nothing to describe. May be a
        /// <see cref="DummySlot"/> - the tooltip only reads the stack out of it.
        /// </summary>
        ItemSlot? TooltipSlot { get; }
    }
}
