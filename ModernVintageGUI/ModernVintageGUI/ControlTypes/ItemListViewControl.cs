using Cairo;
using IS2Mod.ControlTypes;
using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace ModernVintageGUI.ControlTypes
{
    /// <summary>
    /// A <see cref="ListViewControl"/> of item stacks: the handbook's row style, the game's item
    /// tooltip on every row, and a detail view that describes the picked item with the game's
    /// own words rather than with a caption somebody typed twice.
    ///
    /// It is a list view and not a type picker. The difference is what the player is doing:
    /// <see cref="ItemTypeSelectorControl"/> is a square that opens a list, takes one pick and
    /// closes again, while this is a list that stands on the dialog and is browsed - a recipe
    /// book, a shop, a search result, a chest index.
    ///
    /// <code>
    /// var list = new ItemListViewControl();
    /// list.SetStacks(capi.World.Items.Select(item => new ItemStack(item)));
    /// </code>
    /// </summary>
    public class ItemListViewControl : ListViewControl
    {
        #region Properties
        /// <summary>The stacks on offer, in list order. Replace them with <see cref="SetStacks"/>.</summary>
        public IReadOnlyList<ItemStack> Stacks => _stacks;

        /// <summary>The picked stack, or null when nothing is picked.</summary>
        public ItemStack? SelectedStack => SelectedItem?.Stack;

        /// <summary>The code of the picked item - what a mod usually stores and reloads.</summary>
        public AssetLocation? SelectedCode => SelectedStack?.Collectible?.Code;

        /// <summary>
        /// Let the detail view describe an item out of the game itself - the same text the
        /// tooltip and the handbook show - for a row the caller gave no description of.
        ///
        /// On by default, and worth turning off for a list where the item is a stand-in for
        /// something else: a shop listing wants a price and a stock count, not the durability of
        /// the pickaxe it happens to be selling.
        /// </summary>
        public bool DescribeItemsAutomatically { get; set; } = true;
        #endregion

        private readonly List<ItemStack> _stacks = new List<ItemStack>();

        public ItemListViewControl(string _Name = "", double _Margin = 5)
            : base(_Name, _Margin)
        {
            // Forced rather than left to Auto: a list of items is an item list even while it is
            // still empty, and a list whose rows change height as soon as the first stack
            // arrives is a list that jumps under the player.
            RowStyle = DropdownRowStyle.ItemList;
        }

        #region Stacks
        /// <summary>
        /// Sets the stacks on offer. The selection is kept when the same row survives and
        /// cleared otherwise, exactly as in <see cref="ListViewControl.SetItems"/>.
        /// </summary>
        public void SetStacks(IEnumerable<ItemStack>? stacks)
        {
            _stacks.Clear();

            var rows = new List<ListViewItem>();

            if (stacks != null)
            {
                foreach (ItemStack stack in stacks)
                {
                    // A stack without a collectible is a placeholder the registry keeps for an
                    // unknown asset: no name, and no model to draw.
                    if (stack?.Collectible == null)
                        continue;

                    _stacks.Add(stack);
                    rows.Add(new ListViewItem(stack, value: stack.Collectible.Code?.ToString()));
                }
            }

            SetItems(rows);
        }

        /// <summary>The same from collectibles, which is how a caller usually has them.</summary>
        public void SetCollectibles(IEnumerable<CollectibleObject>? collectibles)
        {
            var stacks = new List<ItemStack>();

            if (collectibles != null)
            {
                foreach (CollectibleObject collectible in collectibles)
                {
                    if (collectible?.Code != null)
                    {
                        stacks.Add(new ItemStack(collectible));
                    }
                }
            }

            SetStacks(stacks);
        }

        /// <summary>Picks the row holding this code, if it is on offer.</summary>
        public bool SelectByCode(AssetLocation? code)
        {
            if (code == null)
                return false;

            for (int i = 0; i < Items.Count; i++)
            {
                if (code.Equals(Items[i].Stack?.Collectible?.Code))
                {
                    Select(i);
                    return true;
                }
            }

            return false;
        }
        #endregion

        #region Details
        /// <summary>
        /// Describes the picked item out of the game, unless the caller already described it.
        ///
        /// What the caller put on the row always wins. A list of items is very often a list of
        /// something else that happens to be shown as an item, and a panel that overrode the
        /// mod's own text with the durability of a pickaxe would be worse than no panel.
        /// </summary>
        protected override void FillDetails(DetailViewControl view, ListViewItem item)
        {
            ItemStack? stack = item.Stack;

            if (stack == null || !DescribeItemsAutomatically)
            {
                base.FillDetails(view, item);
                return;
            }

            string? description = item.Description ?? DescribeStack(stack);

            var entries = new List<DetailEntry>(item.Details);

            if (entries.Count == 0)
            {
                entries.AddRange(FactsAbout(stack));
            }

            view.Show(item.Text, description, entries, stack, item.IconName);
        }

        /// <summary>
        /// The game's own description of a stack - the block of text under the name in a
        /// tooltip.
        ///
        /// Null without a client: the text is assembled by the collectible out of the world, and
        /// the layout harness has neither. That is the normal case for a picture rather than a
        /// failure, so it is a null and not a throw.
        /// </summary>
        private string? DescribeStack(ItemStack stack)
        {
            ICoreClientAPI? capi = Dialog?.Api;

            if (capi?.World == null)
                return null;

            string text = stack.GetDescription(capi.World, new DummySlot(stack));

            return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
        }

        /// <summary>
        /// The handful of facts that are true of every collectible, as labelled lines. Anything
        /// beyond this is the mod's business - it knows what its list is about, and this does
        /// not.
        /// </summary>
        private static IEnumerable<DetailEntry> FactsAbout(ItemStack stack)
        {
            CollectibleObject collectible = stack.Collectible;

            if (collectible.Code != null)
            {
                yield return new DetailEntry("Code", collectible.Code.ToString());
            }

            yield return new DetailEntry("Kind", stack.Class == EnumItemClass.Block ? "Block" : "Item");

            if (collectible.MaxStackSize > 0)
            {
                yield return new DetailEntry("Max stack", collectible.MaxStackSize.ToString());
            }

            if (collectible.Durability > 0)
            {
                yield return new DetailEntry("Durability", collectible.Durability.ToString());
            }
        }
        #endregion
    }
}
