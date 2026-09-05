using Cairo;
using System;
using System.Runtime.InteropServices;
using Vintagestory.API.Client;

namespace IS2Mod.ControlTypes
{
    /// <summary>
    /// Blurring a rectangle of the dialog surface without paying for the rest of it.
    ///
    /// <c>SurfaceTransformBlur.BlurPartial</c> takes a rectangle, but its cost follows the
    /// *surface* rather than that rectangle: the same 160x40 button costs 0.26 ms on a 200x200
    /// surface and 13.3 ms on a 2400x2400 one. Every blurred control therefore paid for the size
    /// of the whole dialog, on every redraw - and a redraw happens whenever anything at all is
    /// hovered. On the showcase that was more than half of the drawing, and at GUI scale 2 it was
    /// most of a 58 ms frame.
    ///
    /// So the region is copied onto a surface of its own size, blurred there, and copied back.
    /// Same routine, same arguments, same pixels in - and the cost stops following the dialog:
    /// that 2400x2400 case drops to 0.37 ms.
    ///
    /// The one visible difference is at the edge of a *clipped* control: the blur writes a row or
    /// two past the rectangle it was given, which on the dialog surface smeared past the clip -
    /// the thing the callers intersect their rectangle by hand to avoid in the first place. Here
    /// that spill lands in the scratch and is dropped, so a button cut off by a scrolling list
    /// now ends exactly at the cut.
    /// </summary>
    public static class SurfaceBlur
    {
        /// <summary>
        /// Blurs the rectangle <paramref name="x"/>/<paramref name="y"/> of
        /// <paramref name="surface"/>, in place.
        /// </summary>
        /// <param name="blurRange">Sigma, in device pixels - already scaled by the caller.</param>
        /// <param name="blurEdgeWidth">How many box passes the blur makes.</param>
        public static void BlurRegion(
            ImageSurface surface,
            int x,
            int y,
            int width,
            int height,
            double blurRange,
            int blurEdgeWidth)
        {
            if (surface == null || width <= 0 || height <= 0)
                return;

            // The blur reads past the rectangle it is told to write, so the scratch has to carry
            // the neighbourhood with it - without it a rectangle that cuts through something
            // comes out different at its edges. The band is as wide as the blur can reach: every
            // one of its passes spreads by about its own width.
            //
            // Where the band would leave the surface it is simply cut, which is what the blur
            // meets at the edge of the surface anyway.
            int margin = (int)Math.Ceiling(blurRange * Math.Max(1, blurEdgeWidth)) + 4;

            int left = Math.Max(0, x - margin);
            int top = Math.Max(0, y - margin);
            int right = Math.Min(surface.Width, x + width + margin);
            int bottom = Math.Min(surface.Height, y + height + margin);

            int scratchWidth = right - left;
            int scratchHeight = bottom - top;

            if (scratchWidth <= 0 || scratchHeight <= 0)
                return;

            using (var scratch = new ImageSurface(Format.Argb32, scratchWidth, scratchHeight))
            {
                if (surface.DataPtr == IntPtr.Zero || scratch.DataPtr == IntPtr.Zero)
                    return;

                // The pixels are moved as pixels, not drawn.
                //
                // Painting the region across with Cairo would put a pattern and its filter
                // between the two surfaces, and that does not give back exactly what went in -
                // it showed up as a band of slightly wrong pixels along the cut edge of a
                // clipped button. A copy of the bytes is what the blur itself does, and it is
                // both exact and faster.
                CopyRegion(surface, left, top, scratch, 0, 0, scratchWidth, scratchHeight);

                scratch.MarkDirty();

                // The same rectangle as before, in the scratch's coordinates.
                int innerX = x - left;
                int innerY = y - top;

                SurfaceTransformBlur.BlurPartial(
                    scratch, blurRange, blurEdgeWidth,
                    innerX, innerY, innerX + width, innerY + height);

                // Only the rectangle goes back, not the band around it - that was carried along
                // to be read, not to be changed.
                CopyRegion(scratch, innerX, innerY, surface, x, y, width, height);

                // Written into the buffer behind Cairo's back, exactly as the blur used to be.
                surface.MarkDirty();
            }
        }

        /// <summary>
        /// The same, for a caller that has the rectangle as doubles and has already worked out
        /// what it is allowed to touch. Everything is truncated to whole pixels and clamped to
        /// the surface.
        /// </summary>
        public static void BlurRegion(
            ImageSurface surface,
            double left,
            double top,
            double right,
            double bottom,
            double blurRange,
            int blurEdgeWidth)
        {
            int x = Math.Max(0, (int)left);
            int y = Math.Max(0, (int)top);
            int width = Math.Min((int)(right - left), surface.Width - x);
            int height = Math.Min((int)(bottom - top), surface.Height - y);

            BlurRegion(surface, x, y, width, height, blurRange, blurEdgeWidth);
        }

        /// <summary>
        /// Copies a rectangle of pixels from one ARGB32 surface into another, row by row.
        ///
        /// Through a managed buffer rather than pointer to pointer, because the project does not
        /// build unsafe code - one row at a time keeps that buffer small and the two copies per
        /// row are still a memcpy each.
        /// </summary>
        private static void CopyRegion(
            ImageSurface source, int sourceX, int sourceY,
            ImageSurface destination, int destinationX, int destinationY,
            int width, int height)
        {
            // Cairo may still be holding drawing operations that have not reached the buffer.
            source.Flush();
            destination.Flush();

            int[] row = new int[width];

            int sourceStride = source.Stride;
            int destinationStride = destination.Stride;

            for (int line = 0; line < height; line++)
            {
                IntPtr from = source.DataPtr + (sourceY + line) * sourceStride + sourceX * 4;
                IntPtr to = destination.DataPtr + (destinationY + line) * destinationStride + destinationX * 4;

                Marshal.Copy(from, row, 0, width);
                Marshal.Copy(row, 0, to, width);
            }
        }
    }
}
