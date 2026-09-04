using System;

namespace IS2Mod.ControlTypes
{
    /// <summary>
    /// An axis aligned rectangle in dialog local device pixels. Used for clip regions, where a
    /// pair of PointD would leave it ambiguous whether the second one is a size or a corner.
    /// </summary>
    public readonly struct LayoutRect
    {
        public double X { get; }
        public double Y { get; }
        public double Width { get; }
        public double Height { get; }

        public double Right => X + Width;
        public double Bottom => Y + Height;

        /// <summary>True when the rectangle has no area, i.e. nothing inside it is visible.</summary>
        public bool IsEmpty => Width <= 0 || Height <= 0;

        public LayoutRect(double x, double y, double width, double height)
        {
            X = x;
            Y = y;
            Width = width;
            Height = height;
        }

        /// <summary>
        /// The overlap of two rectangles. Empty when they do not touch - which is the answer for
        /// a control that has been scrolled completely out of its viewport.
        /// </summary>
        public LayoutRect Intersect(LayoutRect other)
        {
            double left = Math.Max(X, other.X);
            double top = Math.Max(Y, other.Y);
            double right = Math.Min(Right, other.Right);
            double bottom = Math.Min(Bottom, other.Bottom);

            return new LayoutRect(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top));
        }

        public bool Contains(double x, double y)
        {
            return x >= X && x <= Right && y >= Y && y <= Bottom;
        }

        public override string ToString()
        {
            return $"{X:0.##}/{Y:0.##} {Width:0.##}x{Height:0.##}";
        }
    }
}
