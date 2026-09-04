using Cairo;
using System;
using Vintagestory.API.Client;

namespace IS2Mod.ControlTypes
{
    /// <summary>
    /// Draws a scrollbar exactly the way the vanilla GUI does, so a scrolling container of ours
    /// is indistinguishable from one of the game's own.
    ///
    /// Every value here comes from GuiElementScrollbar and the emboss helper in GuiElement.
    /// Vanilla composes the track onto the dialog surface and the handle onto a texture of its
    /// own; we draw both onto the shared surface, which is the same picture because the handle
    /// texture is opaque over the track anyway.
    ///
    /// Static, so the layout harness can render a bar without a client.
    /// </summary>
    public static class ScrollbarStyle
    {
        /// <summary>GuiElementScrollbar.DefaultScrollbarWidth, in author units.</summary>
        public const double UnscaledWidth = 20.0;

        /// <summary>GuiElementScrollbar.DeafultScrollbarPadding, in author units.</summary>
        public const double UnscaledPadding = 2.0;

        /// <summary>Vanilla never lets the handle get shorter than this, in device pixels.</summary>
        public const double MinimumHandleLength = 10.0;

        /// <summary>
        /// One wheel tick moves the content by this many author units - GuiElement.scaled(102.0)
        /// in GuiElementScrollbar.OnMouseWheel.
        /// </summary>
        public const double UnscaledWheelStep = 102.0;

        /// <summary>
        /// The sunken groove the handle runs in: a flat dark fill plus an inverted emboss, which
        /// is what makes it read as a channel rather than a raised strip.
        /// </summary>
        public static void DrawTrack(Context ctx, LayoutRect track, double scale)
        {
            ctx.SetSourceRGBA(0.0, 0.0, 0.0, 0.2);
            GuiElement.RoundRectangle(ctx, track.X, track.Y, track.Width, track.Height, GuiStyle.ElementBGRadius);
            ctx.Fill();

            EmbossRoundRectangle(ctx, track.X, track.Y, track.Width, track.Height,
                GuiStyle.ElementBGRadius, depth: 2, inverse: true);
        }

        /// <summary>
        /// The handle. Vanilla fills it with the highlight colour and then fills the same path
        /// again with 40% black - the second fill is composited on top, so the result is the
        /// highlight darkened, not replaced.
        /// </summary>
        public static void DrawHandle(Context ctx, LayoutRect handle)
        {
            GuiElement.RoundRectangle(ctx, handle.X, handle.Y, handle.Width, handle.Height, 1.0);
            ctx.SetSourceRGBA(GuiStyle.DialogHighlightColor);
            ctx.FillPreserve();

            ctx.SetSourceRGBA(0.0, 0.0, 0.0, 0.4);
            ctx.Fill();

            EmbossRoundRectangle(ctx, handle.X, handle.Y, handle.Width, handle.Height,
                radius: 1, depth: 2, inverse: false);
        }

        /// <summary>
        /// How long the handle is for a given track, mirroring
        /// GuiElementScrollbar.SetNewTotalHeight: the visible fraction of the content, never
        /// shorter than <see cref="MinimumHandleLength"/> and never longer than the track.
        /// </summary>
        public static double HandleLength(double trackLength, double viewportLength, double contentLength)
        {
            if (contentLength <= 0)
                return trackLength;

            double fraction = Math.Clamp(viewportLength / contentLength, 0.0, 1.0);

            return Math.Min(trackLength, Math.Max(MinimumHandleLength, fraction * trackLength));
        }

        /// <summary>
        /// Where the handle sits for a given scroll offset. The handle travels the leftover
        /// track while the content travels its own overflow, so the two are proportional.
        /// </summary>
        public static double HandlePosition(
            double trackLength, double handleLength, double scrollOffset, double maxScrollOffset)
        {
            if (maxScrollOffset <= 0)
                return 0;

            double travel = Math.Max(0, trackLength - handleLength);

            return travel * Math.Clamp(scrollOffset / maxScrollOffset, 0.0, 1.0);
        }

        /// <summary>
        /// The inverse: the scroll offset that puts the handle at a given position. Used while
        /// dragging the handle.
        /// </summary>
        public static double ScrollOffsetForHandlePosition(
            double trackLength, double handleLength, double handlePosition, double maxScrollOffset)
        {
            double travel = Math.Max(0, trackLength - handleLength);

            if (travel <= 0)
                return 0;

            return maxScrollOffset * Math.Clamp(handlePosition / travel, 0.0, 1.0);
        }

        /// <summary>
        /// GuiElement.EmbossRoundRectangle with the arguments GuiElement.EmbossRoundRectangleElement
        /// passes: intensity 0.7, lightDarkBalance 0.8, alphaOffset 0.25.
        ///
        /// Reimplemented rather than called because the vanilla one is a protected instance
        /// method on GuiElement, which needs a client API and an ElementBounds - neither of
        /// which exists here or in the harness.
        /// </summary>
        private static void EmbossRoundRectangle(
            Context ctx, double x, double y, double width, double height,
            double radius, int depth, bool inverse)
        {
            const float Intensity = 0.7f;
            const float AlphaOffset = 0.25f;

            double degree = Math.PI / 180.0;
            float lightDarkBalance = 0.8f;

            int lightChannel = 255;
            int darkChannel = 0;

            if (inverse)
            {
                lightChannel = 0;
                darkChannel = 255;
                lightDarkBalance = 2f - lightDarkBalance;
            }

            Antialias previous = ctx.Antialias;
            ctx.Antialias = Antialias.Best;

            int step = 0;
            int remaining = depth;

            while (remaining-- > 0)
            {
                float strength = Intensity * (depth - step) / depth;

                // Top left half of the outline.
                ctx.NewPath();
                ctx.Arc(x + radius, y + height - radius, radius, 135.0 * degree, 180.0 * degree);
                ctx.Arc(x + radius, y + radius, radius, 180.0 * degree, 270.0 * degree);
                ctx.Arc(x + width - radius, y + radius, radius, -90.0 * degree, -45.0 * degree);
                ctx.SetSourceRGBA(lightChannel, lightChannel, lightChannel,
                    Math.Min(1f, lightDarkBalance * strength) - AlphaOffset);
                ctx.LineWidth = 1.0;
                ctx.Stroke();

                // Bottom right half.
                ctx.NewPath();
                ctx.Arc(x + width - radius, y + radius, radius, -45.0 * degree, 0.0 * degree);
                ctx.Arc(x + width - radius, y + height - radius, radius, 0.0 * degree, 90.0 * degree);
                ctx.Arc(x + radius, y + height - radius, radius, 90.0 * degree, 135.0 * degree);
                ctx.SetSourceRGBA(darkChannel, darkChannel, darkChannel,
                    Math.Min(1f, (2f - lightDarkBalance) * strength) - AlphaOffset);
                ctx.LineWidth = 1.0;
                ctx.Stroke();

                step++;
                x++;
                y++;
                width -= 2.0;
                height -= 2.0;
            }

            ctx.Antialias = previous;
        }
    }
}
