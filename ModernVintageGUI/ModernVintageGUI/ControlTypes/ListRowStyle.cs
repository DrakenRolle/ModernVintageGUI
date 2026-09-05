using Cairo;
using System;
using Vintagestory.API.Client;

namespace IS2Mod.ControlTypes
{
    /// <summary>
    /// The three reasons a row of a list can be lit, strongest first.
    ///
    /// They are independent - a row can be selected, focused and hovered at once - which is why
    /// they are flags rather than an enum. Drawing them in order is what keeps the two states a
    /// player has to tell apart, "this is where the cursor is" and "this is what is picked",
    /// from collapsing into the same shade.
    /// </summary>
    [Flags]
    public enum ListRowState
    {
        None = 0,
        Selected = 1,
        Focused = 2,
        Hovered = 4
    }

    /// <summary>
    /// How one row of a list is painted: the band behind it, the line under it and the mark on
    /// its selected edge.
    ///
    /// It is shared rather than written per control on purpose. A dropdown list, a list view and
    /// a tree are the same thing to a player - a column of rows they run their eyes down - and
    /// three separate attempts at "what does a hovered row look like" is exactly how a framework
    /// ends up with three list controls that are recognisably not from the same place.
    ///
    /// Static and free of any client API, so the layout harness renders the real thing.
    /// </summary>
    public static class ListRowStyle
    {
        #region Numbers
        /// <summary>The bar on the leading edge of the selected row, in author units.</summary>
        public const double UnscaledAccentWidth = 3.0;

        /// <summary>The hairline between two rows, in author units.</summary>
        public const double UnscaledSeparatorHeight = 1.0;

        /// <summary>The row under the cursor. The strongest of the three, and deliberately so.</summary>
        public const double HoverAlpha = 0.45;

        /// <summary>The row the keyboard is on, when the cursor is somewhere else.</summary>
        public const double FocusAlpha = 0.32;

        /// <summary>The picked row, which stays lit while the cursor moves on.</summary>
        public const double SelectedAlpha = 0.22;

        /// <summary>
        /// The two shades the banding alternates between - a touch of light on the odd rows, a
        /// touch of shadow on the even ones.
        ///
        /// Both are weak on purpose. The banding is there to give the eye a line to follow
        /// across a wide row, not to be a pattern in its own right; a stripe strong enough to
        /// notice as a stripe fights with the hover and the selection, which are the two things
        /// that actually mean something.
        /// </summary>
        private const double EvenRowAlpha = 0.10;
        private const double OddRowAlpha = 0.045;
        #endregion

        /// <summary>
        /// Paints the band of one row: the alternating shade, whatever the row's state adds on
        /// top of it, the separator under it and the accent on its selected edge.
        ///
        /// <paramref name="band"/> is the whole strip the row owns, gaps included - not the box
        /// its caption sits in. In a list with room between the rows every row takes half that
        /// room on each side, so two bands meet exactly and the column reads as one surface
        /// rather than as a ladder of floating labels.
        /// </summary>
        /// <param name="rowIndex">Position in the list, which is what decides the banding.</param>
        /// <param name="striped">Off for a list short enough not to need the help.</param>
        /// <param name="separator">Off where the rows already have a frame of their own.</param>
        public static void DrawRow(
            Context ctx,
            LayoutRect band,
            int rowIndex,
            ListRowState state,
            double scale,
            bool striped = true,
            bool separator = true)
        {
            if (band.IsEmpty)
                return;

            ctx.Save();

            if (striped)
            {
                DrawBanding(ctx, band, rowIndex);
            }

            DrawStateFill(ctx, band, state);

            if (separator)
            {
                DrawSeparator(ctx, band, scale);
            }

            if ((state & ListRowState.Selected) != 0)
            {
                DrawAccent(ctx, band, scale);
            }

            ctx.Restore();
        }

        /// <summary>The alternating shade, and nothing else.</summary>
        public static void DrawBanding(Context ctx, LayoutRect band, int rowIndex)
        {
            bool even = (rowIndex & 1) == 0;

            ctx.SetSourceRGBA(
                even ? 0.0 : 1.0,
                even ? 0.0 : 1.0,
                even ? 0.0 : 1.0,
                even ? EvenRowAlpha : OddRowAlpha);

            ctx.Rectangle(band.X, band.Y, band.Width, band.Height);
            ctx.Fill();
        }

        /// <summary>
        /// The highlight the row's state calls for. Only the strongest one is drawn: stacking
        /// two translucent fills of the same colour makes a shade neither of them stands for,
        /// and the player would be reading a third state that does not exist.
        /// </summary>
        public static void DrawStateFill(Context ctx, LayoutRect band, ListRowState state)
        {
            double alpha = AlphaFor(state);

            if (alpha <= 0)
                return;

            double[] color = GuiStyle.DialogHighlightColor;

            ctx.SetSourceRGBA(color[0], color[1], color[2], alpha);
            ctx.Rectangle(band.X, band.Y, band.Width, band.Height);
            ctx.Fill();
        }

        /// <summary>Which of the three states wins, as an alpha.</summary>
        public static double AlphaFor(ListRowState state)
        {
            if ((state & ListRowState.Hovered) != 0)
                return HoverAlpha;

            if ((state & ListRowState.Focused) != 0)
                return FocusAlpha;

            if ((state & ListRowState.Selected) != 0)
                return SelectedAlpha;

            return 0.0;
        }

        /// <summary>
        /// The line between two rows: a dark hairline along the bottom of the band with a very
        /// faint light one under it. The pair is what makes it read as a groove in the panel
        /// rather than as a drawn-on border, which is the same trick the game's own inset
        /// elements use.
        /// </summary>
        public static void DrawSeparator(Context ctx, LayoutRect band, double scale)
        {
            double height = Math.Max(1.0, UnscaledSeparatorHeight * scale);
            double bottom = band.Bottom - height;

            ctx.SetSourceRGBA(0.0, 0.0, 0.0, 0.30);
            ctx.Rectangle(band.X, bottom, band.Width, height);
            ctx.Fill();

            ctx.SetSourceRGBA(1.0, 1.0, 1.0, 0.05);
            ctx.Rectangle(band.X, bottom - height, band.Width, height);
            ctx.Fill();
        }

        /// <summary>
        /// The bar on the leading edge of the selected row, in the colour the game marks its
        /// active slot with.
        ///
        /// This is what carries the selection once the cursor has moved on. A fill alone cannot:
        /// hover and selection are both a wash of the same highlight colour, and asking a player
        /// to tell 0.22 from 0.45 of it across a scrolling list is asking them to guess.
        /// </summary>
        public static void DrawAccent(Context ctx, LayoutRect band, double scale)
        {
            double width = Math.Max(1.0, UnscaledAccentWidth * scale);

            ctx.SetSourceRGBA(GuiStyle.ActiveSlotColor);
            ctx.Rectangle(band.X, band.Y, width, band.Height);
            ctx.Fill();
        }
    }
}
