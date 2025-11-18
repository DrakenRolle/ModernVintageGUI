using Cairo;
using IS2Mod.Enums;
using System;
using System.Linq;

namespace IS2Mod.ControlTypes
{
    public enum RectangleBorderStyle
    {
        Top,
        Bottom,
        Left,
        Right
    }

    public class ElementColor
    {
        public byte R { get; set; }
        public byte G { get; set; }
        public byte B { get; set; }
        public byte A { get; set; }

        public ElementColor(byte r, byte g, byte b, byte a)
        {
            R = r;
            G = g;
            B = b;
            A = a;
        }
        public ElementColor(double r, double g, double b, double a)
        {
            R = (byte)(r * 255);
            G = (byte)(g * 255);
            B = (byte)(b * 255);
            A = (byte)(a * 255);
        }
        public ElementColor(double[] colors)
        {
            double r = colors.Length > 0 ? colors[0] : 1.0;
            double g = colors.Length > 1 ? colors[1] : 1.0;
            double b = colors.Length > 2 ? colors[2] : 1.0;
            double a = colors.Length > 3 ? colors[3] : 1.0;

            R = (byte)(r * 255);
            G = (byte)(g * 255);
            B = (byte)(b * 255);
            A = (byte)(a * 255);
        }

        // Color conversion helpers
        public double RNormalized => R / 255.0;
        public double GNormalized => G / 255.0;
        public double BNormalized => B / 255.0;
        public double ANormalized => A / 255.0;

        // Common colors
        public static ElementColor Transparent => new ElementColor(255, 255, 255, 0);
        public static ElementColor White => new ElementColor(255, 255, 255, 255);
        public static ElementColor Black => new ElementColor(0, 0, 0, 255);
    }

    public class RectangleControl : UIControl
    {
        #region Properties
        public int BorderWidth { get; set; }
        public int RoundedCorners { get; set; }
        public ElementColor BorderColor { get; set; }
        public ElementColor BackgroundColor { get; set; }
        public SurfacePattern Pattern { get; set; }
        public RectangleBorderStyle[] HiddenBorders { get; set; }

        // NEW: Blur properties for Gaussian blur effect
        public double BlurRange { get; set; }
        public int BlurEdgeWidth { get; set; }
        #endregion

        #region Constructors
        public RectangleControl(
            int borderWidth = 1,
            int roundedCorners = 0,
            ElementColor borderColor = null,
            ElementColor backgroundColor = null,
            SurfacePattern pattern = null,
            RectangleBorderStyle[] hiddenBorders = null,
            double blurRange = 0,
            int blurEdgeWidth = 0,
            string _Name = "",
            PointD? _Size = null,
            Orientation _Orientation = Orientation.Top,
            double _Margin = 0,
            double _Padding = 0,
            int _Index = 0)
            : base(_Name, _Size, _Orientation, _Margin, _Padding, _Index)
        {
            BorderWidth = borderWidth;
            RoundedCorners = roundedCorners;
            BorderColor = borderColor ?? ElementColor.Transparent;
            BackgroundColor = backgroundColor ?? ElementColor.Transparent;
            Pattern = pattern;
            HiddenBorders = hiddenBorders ?? Array.Empty<RectangleBorderStyle>();
            BlurRange = blurRange;
            BlurEdgeWidth = blurEdgeWidth;
        }

        public RectangleControl() : base()
        {
            BorderWidth = 1;
            RoundedCorners = 0;
            BorderColor = ElementColor.Transparent;
            BackgroundColor = ElementColor.Transparent;
            HiddenBorders = Array.Empty<RectangleBorderStyle>();
            BlurRange = 0;
            BlurEdgeWidth = 0;
            Padding = BorderWidth;
        }
        #endregion

        #region Size Calculation
        public override PointD CalculateSize()
        {
            return base.CalculateSize();
        }
        #endregion

        #region Rendering
        public override void GenerateRenderData(ImageSurface surface, Context ctx)
        {
            // Render borders first
            if (RoundedCorners == 0)
            {
                RenderSquareBorders(ctx);
            }
            else
            {
                RenderRoundedBorders(ctx);
            }

            RenderBackground(ctx);

            // NEW: Apply Gaussian blur to borders if enabled
            if (BlurRange > 0 && BlurEdgeWidth > 0)
            {
                ApplyBlurToBorders(surface);
            }

            // Render children
            base.GenerateRenderData(surface, ctx);
        }

        // NEW: Apply Gaussian blur to border edges using SurfaceTransformBlur.BlurPartial
        private void ApplyBlurToBorders(ImageSurface surface)
        {
            int x = (int)Position.X;
            int y = (int)Position.Y;
            int width = (int)Size.X;
            int height = (int)Size.Y;

            // Ensure coordinates are within surface bounds
            int surfaceWidth = surface.Width;
            int surfaceHeight = surface.Height;

            if (x < 0 || y < 0 || x + width > surfaceWidth || y + height > surfaceHeight)
            {
                // Clamp to surface bounds
                x = Math.Max(0, x);
                y = Math.Max(0, y);
                width = Math.Min(width, surfaceWidth - x);
                height = Math.Min(height, surfaceHeight - y);
            }

            // Apply blur only to the border edges
            if (width > 0 && height > 0)
            {
                try
                {
                    SurfaceTransformBlur.BlurPartial(
                        surface,
                        BlurRange,
                        BlurEdgeWidth,
                        x,
                        y,
                        x + width,
                        y + height
                    );
                }
                catch (Exception ex)
                {
                    // Handle any blur errors gracefully
                    System.Diagnostics.Debug.WriteLine($"Blur failed: {ex.Message}");
                }
            }
        }

        private void RenderSquareBorders(Context ctx)
        {
            ctx.LineWidth = BorderWidth;

            // Top border
            if (!HiddenBorders.Contains(RectangleBorderStyle.Top))
            {
                RenderBorderLine(ctx,
                    Position.X, Position.Y,
                    Position.X + Size.X, Position.Y);
            }

            // Right border
            if (!HiddenBorders.Contains(RectangleBorderStyle.Right))
            {
                RenderBorderLine(ctx,
                    Position.X + Size.X, Position.Y,
                    Position.X + Size.X, Position.Y + Size.Y);
            }

            // Bottom border
            if (!HiddenBorders.Contains(RectangleBorderStyle.Bottom))
            {
                RenderBorderLine(ctx,
                    Position.X + Size.X, Position.Y + Size.Y,
                    Position.X, Position.Y + Size.Y);
            }

            // Left border
            if (!HiddenBorders.Contains(RectangleBorderStyle.Left))
            {
                RenderBorderLine(ctx,
                    Position.X, Position.Y + Size.Y,
                    Position.X, Position.Y);
            }
        }

        private void RenderBorderLine(Context ctx, double x1, double y1, double x2, double y2)
        {
            ctx.SetSourceRGBA(
                BorderColor.RNormalized,
                BorderColor.GNormalized,
                BorderColor.BNormalized,
                BorderColor.ANormalized);

            ctx.NewPath();
            ctx.MoveTo(x1, y1);
            ctx.LineTo(x2, y2);
            ctx.Stroke();
        }

        private void RenderRoundedBorders(Context ctx)
        {
            ctx.SetSourceRGBA(
                BorderColor.RNormalized,
                BorderColor.GNormalized,
                BorderColor.BNormalized,
                BorderColor.ANormalized);
            ctx.LineWidth = BorderWidth;

            // If all borders are visible, use the simple path
            if (HiddenBorders.Length == 0)
            {
                CreateRoundedPath(ctx);
                ctx.Stroke();
            }
            else
            {
                // Render each side individually with rounded corners
                RenderRoundedBordersSelectively(ctx);
            }
        }

        private void RenderRoundedBordersSelectively(Context ctx)
        {
            double radians = Math.PI / 180.0;
            ctx.Antialias = Antialias.Best;

            bool hasTop = !HiddenBorders.Contains(RectangleBorderStyle.Top);
            bool hasRight = !HiddenBorders.Contains(RectangleBorderStyle.Right);
            bool hasBottom = !HiddenBorders.Contains(RectangleBorderStyle.Bottom);
            bool hasLeft = !HiddenBorders.Contains(RectangleBorderStyle.Left);

            // Top border with corners
            if (hasTop)
            {
                ctx.NewPath();

                // Top-left corner
                if (hasLeft)
                {
                    ctx.Arc(
                        Position.X + RoundedCorners,
                        Position.Y + RoundedCorners,
                        RoundedCorners,
                        180.0 * radians,
                        270.0 * radians);
                }
                else
                {
                    ctx.MoveTo(Position.X, Position.Y);
                }

                // Top line
                ctx.LineTo(Position.X + Size.X - RoundedCorners, Position.Y);

                // Top-right corner
                if (hasRight)
                {
                    ctx.Arc(
                        Position.X + Size.X - RoundedCorners,
                        Position.Y + RoundedCorners,
                        RoundedCorners,
                        -90.0 * radians,
                        0.0 * radians);
                }
                else
                {
                    ctx.LineTo(Position.X + Size.X, Position.Y);
                }

                ctx.Stroke();
            }

            // Right border with corners
            if (hasRight)
            {
                ctx.NewPath();

                // Top-right corner (if top is not drawn)
                if (!hasTop)
                {
                    ctx.Arc(
                        Position.X + Size.X - RoundedCorners,
                        Position.Y + RoundedCorners,
                        RoundedCorners,
                        -90.0 * radians,
                        0.0 * radians);
                }
                else
                {
                    ctx.MoveTo(Position.X + Size.X, Position.Y + RoundedCorners);
                }

                // Right line
                ctx.LineTo(Position.X + Size.X, Position.Y + Size.Y - RoundedCorners);

                // Bottom-right corner
                if (hasBottom)
                {
                    ctx.Arc(
                        Position.X + Size.X - RoundedCorners,
                        Position.Y + Size.Y - RoundedCorners,
                        RoundedCorners,
                        0.0 * radians,
                        90.0 * radians);
                }
                else
                {
                    ctx.LineTo(Position.X + Size.X, Position.Y + Size.Y);
                }

                ctx.Stroke();
            }

            // Bottom border with corners
            if (hasBottom)
            {
                ctx.NewPath();

                // Bottom-right corner (if right is not drawn)
                if (!hasRight)
                {
                    ctx.Arc(
                        Position.X + Size.X - RoundedCorners,
                        Position.Y + Size.Y - RoundedCorners,
                        RoundedCorners,
                        0.0 * radians,
                        90.0 * radians);
                }
                else
                {
                    ctx.MoveTo(Position.X + Size.X - RoundedCorners, Position.Y + Size.Y);
                }

                // Bottom line
                ctx.LineTo(Position.X + RoundedCorners, Position.Y + Size.Y);

                // Bottom-left corner
                if (hasLeft)
                {
                    ctx.Arc(
                        Position.X + RoundedCorners,
                        Position.Y + Size.Y - RoundedCorners,
                        RoundedCorners,
                        90.0 * radians,
                        180.0 * radians);
                }
                else
                {
                    ctx.LineTo(Position.X, Position.Y + Size.Y);
                }

                ctx.Stroke();
            }

            // Left border with corners
            if (hasLeft)
            {
                ctx.NewPath();

                // Bottom-left corner (if bottom is not drawn)
                if (!hasBottom)
                {
                    ctx.Arc(
                        Position.X + RoundedCorners,
                        Position.Y + Size.Y - RoundedCorners,
                        RoundedCorners,
                        90.0 * radians,
                        180.0 * radians);
                }
                else
                {
                    ctx.MoveTo(Position.X, Position.Y + Size.Y - RoundedCorners);
                }

                // Left line
                ctx.LineTo(Position.X, Position.Y + RoundedCorners);

                // Top-left corner
                if (hasTop)
                {
                    ctx.Arc(
                        Position.X + RoundedCorners,
                        Position.Y + RoundedCorners,
                        RoundedCorners,
                        180.0 * radians,
                        270.0 * radians);
                }
                else
                {
                    ctx.LineTo(Position.X, Position.Y);
                }

                ctx.Stroke();
            }
        }

        private void RenderBackground(Context ctx)
        {
            if (BackgroundColor == null)
                return;

            ctx.SetSourceRGBA(
                BackgroundColor.RNormalized,
                BackgroundColor.GNormalized,
                BackgroundColor.BNormalized,
                BackgroundColor.ANormalized);

            if (RoundedCorners == 0)
            {
                ctx.Rectangle(Position.X, Position.Y, Size.X, Size.Y);
            }
            else
            {
                CreateRoundedPath(ctx);
            }

            ctx.Fill();
        }

        private void CreateRoundedPath(Context ctx)
        {
            double radians = Math.PI / 180.0;
            ctx.Antialias = Antialias.Best;
            ctx.NewPath();

            // Top-right corner
            ctx.Arc(
                Position.X + Size.X - RoundedCorners,
                Position.Y + RoundedCorners,
                RoundedCorners,
                -90.0 * radians,
                0.0 * radians);

            // Bottom-right corner
            ctx.Arc(
                Position.X + Size.X - RoundedCorners,
                Position.Y + Size.Y - RoundedCorners,
                RoundedCorners,
                0.0 * radians,
                90.0 * radians);

            // Bottom-left corner
            ctx.Arc(
                Position.X + RoundedCorners,
                Position.Y + Size.Y - RoundedCorners,
                RoundedCorners,
                90.0 * radians,
                180.0 * radians);

            // Top-left corner
            ctx.Arc(
                Position.X + RoundedCorners,
                Position.Y + RoundedCorners,
                RoundedCorners,
                180.0 * radians,
                270.0 * radians);

            ctx.ClosePath();
        }
        #endregion
    }
}