using Cairo;
using IS2Mod.Enums;
using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace IS2Mod.ControlTypes
{
    /// <summary>
    /// A single inventory slot, drawn exactly like the one the game draws.
    ///
    /// Every number in here comes from GuiElementItemSlotGridBase.ComposeElements and
    /// GuiElementPassiveItemSlot, so a grid of these is indistinguishable from a vanilla one.
    ///
    /// The slot is drawn in two passes, the same split vanilla makes. The background is Cairo
    /// and lands in the dialog surface; the item stack cannot be, because the game renders it
    /// out of the item atlas with its own shader, so it is drawn per frame in
    /// <see cref="GenerateInteractiveRenderData"/>.
    /// </summary>
    public class ItemSlotControl : UIControl, Interfaces.IItemTooltipSource
    {
        #region Vanilla styling
        /// <summary>GuiElementPassiveItemSlot.unscaledSlotSize.</summary>
        public const double UnscaledSlotSize = 48.0;

        /// <summary>GuiElementItemSlotGridBase.unscaledSlotPadding - the gap between two slots.</summary>
        public const double UnscaledSlotPadding = 3.0;

        /// <summary>Vanilla strokes the slot frame with scaled(4.5).</summary>
        private const double UnscaledFrameWidth = 4.5;

        /// <summary>And blurs it twice with scaled(4.0).</summary>
        private const double UnscaledBlurRange = 4.0;

        /// <summary>
        /// GuiElementPassiveItemSlot.unscaledItemSize - the size the stack is rendered at, which
        /// is a good deal smaller than the slot it sits in. Getting this wrong is immediately
        /// visible: the items look bloated and touch the frame.
        /// </summary>
        public const double UnscaledItemSize = 25.6;

        /// <summary>
        /// How far outside the slot the selection ring is *drawn*, in author units - the offset
        /// of its path, not how far its ink reaches.
        /// </summary>
        public const double UnscaledHighlightOverhang = 2.0;

        /// <summary>How thick that ring is stroked.</summary>
        public const double UnscaledHighlightLineWidth = 3.0;

        /// <summary>
        /// How far the ink of the selection ring actually reaches outside the slot: the offset
        /// of the path plus half the stroke, because Cairo centres a stroke on its path.
        ///
        /// A container that clips its slots has to keep this much room around the lattice. The
        /// half stroke is the part that is easy to forget, and forgetting it is visible at once:
        /// the ring of every outermost slot loses 1.5 units, so a hover highlight in the top row
        /// comes out with a flat top - see <see cref="InventoryGridControl"/>.
        /// </summary>
        public const double UnscaledHighlightReach =
            UnscaledHighlightOverhang + UnscaledHighlightLineWidth / 2.0;
        #endregion

        #region Properties
        /// <summary>
        /// The inventory slot shown here, or null for an empty decorative slot. Assigning it
        /// needs no redraw of the surface: the stack is drawn fresh every frame anyway, and the
        /// background does not depend on it.
        /// </summary>
        public ItemSlot? Slot { get; set; }

        /// <summary>Draw the vanilla active-slot ring around this slot.</summary>
        public bool IsHighlighted { get; set; }

        /// <summary>Index of this slot inside its grid. Handed to the grid events.</summary>
        public int SlotIndex { get; set; }

        /// <inheritdoc/>
        public ItemSlot? TooltipSlot => Slot;
        #endregion

        /// <summary>
        /// One background surface per pixel size, shared by every slot on screen.
        ///
        /// This is not an optimisation for its own sake, it is what makes the picture correct.
        /// Vanilla composes the slot onto a surface of its own and blits that texture once per
        /// slot, and the blur is why that matters: SurfaceTransformBlur works on the surface
        /// buffer and knows nothing about Cairo, so blurring a slot drawn straight onto the
        /// shared dialog surface would pull in whatever a neighbour three units away had
        /// already drawn. Drawing it in isolation and painting the result reproduces the
        /// original exactly - and since every slot looks the same, one surface serves them all.
        /// </summary>
        private static readonly Dictionary<int, ImageSurface> BackgroundCache =
            new Dictionary<int, ImageSurface>();

        public ItemSlotControl(string _Name = "", int _SlotIndex = 0)
            : base(_Name, new PointD(UnscaledSlotSize, UnscaledSlotSize),
                   Orientation.None, _Margin: 0, _Padding: 0)
        {
            SlotIndex = _SlotIndex;
            IsAutoSize = false;

            // A slot is something the player operates, so it belongs in the tab order.
            IsFocusable = true;

            // Vanilla lights the slot under the cursor. Doing it here rather than leaving it to
            // the grid means a lone slot outside a grid behaves the same way.
            Enter += (sender, e) => { SetHighlighted(true); NotifyHover(entered: true); };
            Exit += (sender, e) => { SetHighlighted(false); NotifyHover(entered: false); };
            GotFocus += (sender, e) => SetHighlighted(true);
            LostFocus += (sender, e) => SetHighlighted(false);
        }

        private void NotifyHover(bool entered)
        {
            ItemTooltip.Announce(Dialog?.Api, Slot, entered);
        }

        private void SetHighlighted(bool highlighted)
        {
            if (IsHighlighted == highlighted)
                return;

            IsHighlighted = highlighted;
            Dialog?.Refresh();
        }

        /// <summary>
        /// A slot is one piece. It has no children today, but if a subclass adds an overlay it
        /// must not start swallowing the clicks meant for the slot.
        /// </summary>
        protected override UIControl? HitTestRecursive(UIControl control, double localX, double localY)
        {
            return control.ContainsLocalPoint(localX, localY) ? control : null;
        }

        public override PointD CalculateSize()
        {
            // Always exactly one slot, whatever any children might say.
            foreach (UIControl child in Children)
            {
                child.CalculateSize();
            }

            PointD measured = ScaledExplicitSize;

            CalculatedSize = measured;
            SetLayoutSize(measured);

            return measured;
        }

        public override void GenerateRenderData(ImageSurface surface, Context ctx)
        {
            int size = (int)Math.Round(UnscaledSlotSize * LayoutScale);
            if (size <= 0)
                return;

            ImageSurface background = GetBackground(size);

            ctx.Save();
            ctx.SetSource(background, Position.X, Position.Y);
            ctx.Rectangle(Position.X, Position.Y, size, size);
            ctx.Fill();
            ctx.Restore();

            if (IsHighlighted)
            {
                DrawHighlight(ctx, size);
            }

            base.GenerateRenderData(surface, ctx);
        }

        /// <summary>
        /// The slot background, straight out of GuiElementItemSlotGridBase: a filled rounded
        /// rectangle in the back colour, a thick stroke in the front colour, blurred twice so
        /// that stroke turns into the soft inner shadow, and finally a hard dark outline.
        /// </summary>
        private static ImageSurface GetBackground(int size)
        {
            if (BackgroundCache.TryGetValue(size, out ImageSurface? cached))
                return cached;

            double scale = size / UnscaledSlotSize;

            var surface = new ImageSurface(Format.Argb32, size, size);
            using (var ctx = new Context(surface))
            {
                ctx.Antialias = Antialias.Best;

                ctx.SetSourceRGBA(GuiStyle.DialogSlotBackColor);
                GuiElement.RoundRectangle(ctx, 0, 0, size, size, GuiStyle.ElementBGRadius);
                ctx.Fill();

                ctx.SetSourceRGBA(GuiStyle.DialogSlotFrontColor);
                GuiElement.RoundRectangle(ctx, 0, 0, size, size, GuiStyle.ElementBGRadius);
                ctx.LineWidth = UnscaledFrameWidth * scale;
                ctx.Stroke();

                // The blur reads the pixel buffer, so the drawing above has to be committed to
                // it first.
                surface.Flush();

                // Twice, exactly as vanilla does. One pass does not turn the stroke into the
                // soft gradient a slot is recognised by.
                surface.BlurFull(UnscaledBlurRange * scale);
                surface.BlurFull(UnscaledBlurRange * scale);

                GuiElement.RoundRectangle(ctx, 0, 0, size, size, 1.0);
                ctx.LineWidth = UnscaledFrameWidth * scale;
                ctx.SetSourceRGBA(0.0, 0.0, 0.0, 0.8);
                ctx.Stroke();

                surface.Flush();
            }

            BackgroundCache[size] = surface;
            return surface;
        }

        /// <summary>
        /// The active slot ring.
        ///
        /// Vanilla composes it on a surface four units larger than the slot and strokes it three
        /// times with two blurs in between, so it glows outwards past the slot edge - that
        /// overhang is what makes it read as a selection and not as a border. The oversize is
        /// reproduced here; the blurs are not, because on the shared dialog surface they would
        /// bleed straight into the neighbouring slots three units away.
        /// </summary>
        private void DrawHighlight(Context ctx, int size)
        {
            double overhang = UnscaledHighlightOverhang * LayoutScale;

            ctx.Save();
            ctx.SetSourceRGBA(GuiStyle.ActiveSlotColor);

            GuiElement.RoundRectangle(
                ctx,
                Position.X - overhang,
                Position.Y - overhang,
                size + overhang * 2,
                size + overhang * 2,
                GuiStyle.ElementBGRadius);

            ctx.LineWidth = UnscaledHighlightLineWidth * LayoutScale;
            ctx.Stroke();
            ctx.Restore();
        }

        /// <summary>
        /// The item stack. Nothing here can go into the dialog surface: the game animates
        /// stacks and draws them from the item atlas rather than as a bitmap.
        /// </summary>
        public override void GenerateInteractiveRenderData(ICoreClientAPI api, float deltaTime)
        {
            base.GenerateInteractiveRenderData(api, deltaTime);

            if (Slot?.Itemstack == null)
                return;

            PointD screen = GetScreenPosition();

            // Centre of the slot, and the vanilla render size - the same two numbers
            // GuiElementItemSlotGridBase uses: slot origin plus half a slot, at scaled(25.6).
            double centre = UnscaledSlotSize / 2.0 * LayoutScale;
            double size = UnscaledItemSize * LayoutScale;

            // In front of the dialog surface the renderer drew a moment ago, and behind the stack
            // on the cursor. Relative to the surface rather than a fixed number: our dialogs sit
            // at a much greater depth than vanilla's, so a hard coded 90 would put the item
            // behind our own background and it would be invisible.
            float z = (Dialog?.SurfaceRenderZ ?? 0) + Custom.CustomDialogElement.SlotItemZOffset;

            api.Render.RenderItemstackToGui(
                Slot,
                screen.X + centre,
                screen.Y + centre,
                z,
                (float)size,
                ColorUtil.WhiteArgb,
                deltaTime);
        }
    }
}
