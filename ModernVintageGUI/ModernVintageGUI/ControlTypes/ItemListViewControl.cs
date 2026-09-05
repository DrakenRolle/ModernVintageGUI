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

        /// <summary>
        /// Put every variant of the picked thing into its details, as a list of its own.
        ///
        /// This is what a list of *kinds* wants. One row says "rock", and what the player is
        /// actually after is which rock - so opening the row opens the granite, the andesite and
        /// the chalk, each with the game's own icon and tooltip, in the same kind of list they
        /// came from. It is the same control nested one level deep, which is also why it stops
        /// there: the nested list has this switched off, so a variant opens its description and
        /// not a third list.
        ///
        /// On by default. Off for a list that already holds the variants themselves, where every
        /// row would open a copy of its own siblings.
        /// </summary>
        public bool ShowVariants { get; set; } = true;

        /// <summary>How tall the nested variant list is, in author units.</summary>
        public double UnscaledVariantListHeight { get; set; } = 150.0;

        /// <summary>The caption over the nested list. Null leaves it off.</summary>
        public string? VariantsHeading { get; set; } = "Variants";

        /// <summary>
        /// Raised when a row of the nested variant list is picked. The outer list's own
        /// <see cref="ListViewControl.SelectionChanged"/> stays on the kind that was opened, so
        /// a caller can tell "they are looking at rock" from "they picked granite".
        /// </summary>
        public event EventHandler<ListViewSelectionEventArgs>? VariantSelected;
        #endregion

        private readonly List<ItemStack> _stacks = new List<ItemStack>();

        /// <summary>
        /// The nested list, built once and refilled per row rather than made again per click -
        /// it is a control with a subtree, and the panel it sits in is rebuilt on every click.
        /// </summary>
        private ItemListViewControl? _variantList;
        private RectangleControl? _variantPanel;

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

            // What the caller put on the row always wins over anything worked out here, so a
            // row that already carries its own content keeps it.
            UIControl? content = item.DetailContent ?? BuildVariantList(stack);

            if (stack == null || !DescribeItemsAutomatically)
            {
                view.Show(item.Text, item.Description, item.Details, stack, item.IconName, content);
                return;
            }

            string? description = item.Description ?? DescribeStack(stack);

            var entries = new List<DetailEntry>(item.Details);

            if (entries.Count == 0)
            {
                entries.AddRange(FactsAbout(stack));
            }

            view.Show(item.Text, description, entries, stack, item.IconName, content);
        }

        /// <summary>
        /// The nested list of every variant of <paramref name="stack"/>, or null when there is
        /// nothing to nest.
        ///
        /// Null rather than an empty list in three cases, and each of them is a case where a
        /// frame with nothing in it would be worse than no frame: variants are switched off,
        /// there is no client to ask, or the thing has no variants beyond itself.
        /// </summary>
        private UIControl? BuildVariantList(ItemStack? stack)
        {
            if (!ShowVariants || stack?.Collectible?.Code == null)
                return null;

            ICoreClientAPI? capi = Dialog?.Api;

            if (capi == null)
                return null;

            List<ItemStack> variants = ItemTypeSelectorControl.CollectVariants(capi, stack.Collectible.Code);

            if (variants.Count <= 1)
                return null;

            EnsureVariantList();

            _variantList!.SetStacks(variants);

            // The variant the row was opened from, marked in the nested list - so a player who
            // opened "rock-granite" sees which of the rocks they came from.
            _variantList.SelectByCode(stack.Collectible.Code);

            return _variantPanel;
        }

        /// <summary>
        /// Builds the nested list and the panel that holds it, once.
        ///
        /// The heading sits in the panel rather than in the list, because a list draws its own
        /// frame and a caption inside that frame would read as a row.
        /// </summary>
        private void EnsureVariantList()
        {
            if (_variantList != null)
                return;

            _variantPanel = new RectangleControl(_Name: Name + "_variantPanel")
            {
                InsideOrientation = IS2Mod.Enums.Orientation.Top
            };

            if (!string.IsNullOrEmpty(VariantsHeading))
            {
                _variantPanel.Children.Add(new TextLabelControl(
                    text: VariantsHeading,
                    fontName: GuiStyle.StandardFontName,
                    fontSize: 16,
                    textColor: new ElementColor(1.0, 1.0, 1.0, 0.55),
                    orientation: TextOrientation.MiddleLeft,
                    padding: 0,
                    _Name: Name + "_variantsHeading",
                    _Margin: 2));
            }

            _variantList = new ItemListViewControl(Name + "_variants", _Margin: 0)
            {
                // The one that stops it: a variant opens its description, not another list of
                // the siblings it is already standing among.
                ShowVariants = false,

                Size = new PointD(ListViewControl.UnscaledDefaultWidth, UnscaledVariantListHeight),
                IsAutoSize = false
            };

            _variantList.SelectionChanged += (sender, e) => VariantSelected?.Invoke(this, e);

            _variantPanel.Children.Add(_variantList);
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
