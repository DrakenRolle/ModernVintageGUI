using Cairo;

namespace IS2Mod.Interfaces
{
    /// <summary>
    /// A container that can show more content than fits and scroll through it.
    ///
    /// The scrollbars are not controls. They are drawn by the container itself, in the strip it
    /// reserves along its own edge, and they are not part of its Children - so they cannot be
    /// hit by the layout, cannot end up in the tab order, and cannot be dragged out of place.
    /// That is the whole reason this is an interface on the container rather than a
    /// ScrollbarControl: a scrollbar has no meaning without the thing it scrolls.
    ///
    /// Implementing this alone changes nothing. It unlocks the two switches, and a bar only
    /// appears once one of them is on *and* the content on that axis is actually larger than the
    /// viewport.
    /// </summary>
    public interface IScrollable
    {
        /// <summary>Allow scrolling up and down, and show a bar on the right when needed.</summary>
        bool EnableVerticalScrollbar { get; set; }

        /// <summary>Allow scrolling left and right, and show a bar at the bottom when needed.</summary>
        bool EnableHorizontalScrollbar { get; set; }

        /// <summary>
        /// How far the content is currently shifted, in device pixels, always positive or zero.
        /// X of 30 means the content is 30 pixels to the left of where it would sit unscrolled.
        /// </summary>
        PointD ScrollOffset { get; }

        /// <summary>
        /// The full size of the content, in device pixels - what it would need if nothing were
        /// cut. Measured by the layout, not by the caller.
        /// </summary>
        PointD ContentSize { get; }

        /// <summary>
        /// The visible area, in device pixels: the content box minus whatever the visible
        /// scrollbars reserve.
        /// </summary>
        PointD ViewportSize { get; }

        /// <summary>
        /// The furthest the content can be shifted on each axis, i.e. content minus viewport,
        /// never below zero. Both zero means everything fits and no bar is shown.
        /// </summary>
        PointD MaxScrollOffset { get; }

        /// <summary>
        /// Scrolls to an absolute offset, clamped to <see cref="MaxScrollOffset"/>. Returns true
        /// when the offset actually changed, so a caller can tell a consumed wheel tick from one
        /// that hit the end and should be passed on.
        /// </summary>
        bool ScrollTo(double offsetX, double offsetY);

        /// <summary>Shifts the current offset by a delta. Same return as <see cref="ScrollTo"/>.</summary>
        bool ScrollBy(double deltaX, double deltaY);
    }
}
