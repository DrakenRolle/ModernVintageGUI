using Cairo;
using System;
using Vintagestory.API.Client;

namespace IS2Mod.ControlTypes
{
    /// <summary>
    /// Cairo routines the game draws its own widgets with, as statics.
    ///
    /// GuiElement carries these as instance methods, which puts them out of reach of anything
    /// that is not a GuiElement - and nothing in this framework is one. They are ported here
    /// rather than approximated, because the numbers are exactly what makes a control read as
    /// part of the game rather than as something a mod drew next to it.
    /// </summary>
    public static class VanillaDraw
    {
        /// <summary>
        /// The bevel vanilla puts on buttons, dropdowns and inset boxes: a light edge along the
        /// top left and a dark one along the bottom right, one pixel per pass, fading out over
        /// <paramref name="depth"/> passes.
        ///
        /// <paramref name="inverse"/> swaps the two, which is what turns a raised button into a
        /// sunken well. The three constants are the ones
        /// GuiElement.EmbossRoundRectangleElement passes on.
        /// </summary>
        public static void EmbossRoundRectangle(
            Context ctx,
            double x,
            double y,
            double width,
            double height,
            bool inverse = false,
            int depth = 2,
            double radius = -1)
        {
            EmbossRoundRectangle(
                ctx, x, y, width, height,
                radius < 0 ? GuiStyle.ElementBGRadius : radius,
                depth,
                intensity: 0.7f,
                lightDarkBalance: 0.8f,
                inverse: inverse,
                alphaOffset: 0.25f);
        }

        /// <summary>The full routine, with the knobs vanilla keeps to itself.</summary>
        public static void EmbossRoundRectangle(
            Context ctx,
            double x,
            double y,
            double width,
            double height,
            double radius,
            int depth,
            float intensity,
            float lightDarkBalance,
            bool inverse,
            float alphaOffset)
        {
            const double ToRadians = Math.PI / 180.0;

            ctx.Antialias = Antialias.Best;

            // The two edge colours, swapped when the bevel goes inwards.
            double lightEdge = inverse ? 0.0 : 1.0;
            double darkEdge = inverse ? 1.0 : 0.0;

            if (inverse)
            {
                lightDarkBalance = 2f - lightDarkBalance;
            }

            int remaining = depth;
            int pass = 0;

            while (remaining-- > 0)
            {
                // Top left half of the outline: bottom left corner up and over to the top right.
                ctx.NewPath();
                ctx.Arc(x + radius, y + height - radius, radius, 135.0 * ToRadians, 180.0 * ToRadians);
                ctx.Arc(x + radius, y + radius, radius, 180.0 * ToRadians, 270.0 * ToRadians);
                ctx.Arc(x + width - radius, y + radius, radius, -90.0 * ToRadians, -45.0 * ToRadians);

                float fade = intensity * (depth - pass) / depth;
                double alpha = Math.Min(1f, lightDarkBalance * fade) - alphaOffset;

                ctx.SetSourceRGBA(lightEdge, lightEdge, lightEdge, alpha);
                ctx.LineWidth = 1.0;
                ctx.Stroke();

                // Bottom right half, mirrored.
                ctx.NewPath();
                ctx.Arc(x + width - radius, y + radius, radius, -45.0 * ToRadians, 0.0);
                ctx.Arc(x + width - radius, y + height - radius, radius, 0.0, 90.0 * ToRadians);
                ctx.Arc(x + radius, y + height - radius, radius, 90.0 * ToRadians, 135.0 * ToRadians);

                alpha = Math.Min(1f, (2f - lightDarkBalance) * fade) - alphaOffset;

                ctx.SetSourceRGBA(darkEdge, darkEdge, darkEdge, alpha);
                ctx.LineWidth = 1.0;
                ctx.Stroke();

                // Each pass is drawn one pixel further in, which is what makes the edge fade
                // rather than end in a hard line.
                pass++;
                x++;
                y++;
                width -= 2.0;
                height -= 2.0;
            }
        }
    }
}
