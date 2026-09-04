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

    public class RectangleControl : UIControl, IS2Mod.Interfaces.IScrollable
    {
        #region Properties
        public int BorderWidth { get; set; }
        public int RoundedCorners { get; set; }
        public ElementColor BorderColor { get; set; }
        public ElementColor BackgroundColor { get; set; }
        public SurfacePattern? Pattern { get; set; }
        public RectangleBorderStyle[] HiddenBorders { get; set; }

        // NEW: Blur properties for Gaussian blur effect
        public double BlurRange { get; set; }
        public int BlurEdgeWidth { get; set; }
        #endregion

        #region Constructors
        public RectangleControl(
            int borderWidth = 1,
            int roundedCorners = 0,
            ElementColor? borderColor = null,
            ElementColor? backgroundColor = null,
            SurfacePattern? pattern = null,
            RectangleBorderStyle[]? hiddenBorders = null,
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

            SubscribeScrollInput();
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

            SubscribeScrollInput();
        }
        #endregion

        /// <summary>Border width in device pixels.</summary>
        private double ScaledBorderWidth => BorderWidth * LayoutScale;

        /// <summary>Corner radius in device pixels.</summary>
        private double ScaledRoundedCorners => RoundedCorners * LayoutScale;

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

            // Render children - clipped to the viewport when this container scrolls
            base.GenerateRenderData(surface, ctx);

            // The bars go on top of the content and outside its clip, which is why they are
            // drawn after the base call rather than as children.
            DrawScrollbars(ctx);
        }

        #region Scrolling
        private bool _enableVerticalScrollbar;
        private bool _enableHorizontalScrollbar;

        /// <summary>
        /// The scroll position in *unscaled author units*, like Size and Padding, and unlike the
        /// device pixel values the rest of the layout works in.
        ///
        /// This is what makes the GUI scale slider behave. The content grows with the scale, so
        /// an offset kept in device pixels would point at a different row after a scale change -
        /// the list would appear to jump. In author units the same offset keeps the same row at
        /// the top, which is what the player expects from a slider that only changes how big
        /// everything is.
        /// </summary>
        private PointD _scrollOffsetUnscaled;

        /// <summary>Which bar the player is currently dragging, if any.</summary>
        private enum DragTarget { None, Vertical, Horizontal }
        private DragTarget _dragging = DragTarget.None;

        /// <summary>Where inside the handle the drag started, so it does not jump on grab.</summary>
        private double _dragGrabOffset;

        /// <inheritdoc/>
        public bool EnableVerticalScrollbar
        {
            get => _enableVerticalScrollbar;
            set
            {
                if (!SetProperty(ref _enableVerticalScrollbar, value))
                    return;

                OnScrollEnabledChanged();
            }
        }

        /// <inheritdoc/>
        public bool EnableHorizontalScrollbar
        {
            get => _enableHorizontalScrollbar;
            set
            {
                if (!SetProperty(ref _enableHorizontalScrollbar, value))
                    return;

                OnScrollEnabledChanged();
            }
        }

        /// <inheritdoc/>
        public PointD ScrollOffset => new PointD(
            _scrollOffsetUnscaled.X * LayoutScale,
            _scrollOffsetUnscaled.Y * LayoutScale);

        /// <inheritdoc/>
        public PointD ContentSize => MeasuredContentSize;

        /// <inheritdoc/>
        public PointD ViewportSize
        {
            get
            {
                LayoutRect viewport = ResolveScroll().Viewport;
                return new PointD(viewport.Width, viewport.Height);
            }
        }

        /// <inheritdoc/>
        public PointD MaxScrollOffset => ResolveScroll().MaxOffset;

        /// <summary>True while either axis may scroll.</summary>
        private bool IsScrollable => _enableVerticalScrollbar || _enableHorizontalScrollbar;

        /// <summary>
        /// Scrolling without clipping would just draw the content over everything around it, so
        /// switching a bar on switches clipping on with it. Switching the last one off does not
        /// switch clipping back off - it may have been wanted on its own.
        /// </summary>
        private void OnScrollEnabledChanged()
        {
            if (IsScrollable)
                ClipsChildren = true;

            RecomposeToMain();
        }

        private ScrollLayout ResolveScroll()
        {
            return ScrollLayout.Resolve(
                PaddingBox(),
                MeasuredContentSize,
                _enableVerticalScrollbar,
                _enableHorizontalScrollbar,
                ScrollbarStyle.UnscaledWidth * LayoutScale);
        }

        /// <summary>
        /// Children live in the viewport, not in the whole padding box: the strip a bar occupies
        /// is not theirs to be stretched into or drawn in.
        /// </summary>
        public override LayoutRect ContentBox()
        {
            return IsScrollable ? ResolveScroll().Viewport : PaddingBox();
        }

        /// <summary>
        /// On a scrolling axis the children get the whole content to spread over, not just the
        /// visible strip - a row in a horizontally scrolling list has to be able to be wider
        /// than the window it is scrolled through. On an axis that does not scroll this is the
        /// viewport, exactly as for any other container.
        /// </summary>
        public override LayoutRect ArrangeBox()
        {
            if (!IsScrollable)
                return PaddingBox();

            ScrollLayout scroll = ResolveScroll();
            LayoutRect viewport = scroll.Viewport;

            double width = _enableHorizontalScrollbar
                ? Math.Max(viewport.Width, MeasuredContentSize.X)
                : viewport.Width;

            double height = _enableVerticalScrollbar
                ? Math.Max(viewport.Height, MeasuredContentSize.Y)
                : viewport.Height;

            return new LayoutRect(viewport.X, viewport.Y, width, height);
        }

        /// <inheritdoc/>
        public bool ScrollTo(double offsetX, double offsetY)
        {
            PointD clamped = ClampOffset(new PointD(offsetX, offsetY));
            PointD current = ScrollOffset;

            if (Math.Abs(clamped.X - current.X) < 0.001 && Math.Abs(clamped.Y - current.Y) < 0.001)
                return false;

            // Stored unscaled, taken in device pixels - the same split Size and ExplicitSize use.
            double scale = LayoutScale <= 0 ? 1 : LayoutScale;
            _scrollOffsetUnscaled = new PointD(clamped.X / scale, clamped.Y / scale);

            // Positions are baked into the layout, so moving the content is a layout change.
            // Cheap enough - the pass is a couple of walks over the tree - and it keeps
            // everything in one coordinate space, which the blur depends on.
            RecomposeToMain();
            return true;
        }

        /// <inheritdoc/>
        public bool ScrollBy(double deltaX, double deltaY)
        {
            PointD current = ScrollOffset;
            return ScrollTo(current.X + deltaX, current.Y + deltaY);
        }

        /// <summary>Holds a device pixel offset inside what the content actually allows.</summary>
        private PointD ClampOffset(PointD offset)
        {
            PointD max = ResolveScroll().MaxOffset;

            return new PointD(
                Math.Clamp(offset.X, 0, max.X),
                Math.Clamp(offset.Y, 0, max.Y));
        }

        public override void CalculateAllPositions()
        {
            base.CalculateAllPositions();
            ApplyScrollOffsetToChildren();
        }

        /// <summary>
        /// Shifts the laid out children by the current scroll offset, after clamping it.
        ///
        /// Separate from <see cref="CalculateAllPositions"/> so a container that places its
        /// children itself - <see cref="InventoryGridControl"/> puts them on a fixed lattice
        /// rather than stacking them - can position them first and then scroll them, in that
        /// order. Doing it the other way round would overwrite the shift.
        /// </summary>
        protected void ApplyScrollOffsetToChildren()
        {
            if (!IsScrollable)
                return;

            // Clamp first: the content may have shrunk since the last pass, or the GUI scale may
            // have changed the viewport, and an offset past the end would show empty space.
            // Clamping is idempotent, so doing it inside the layout pass is safe.
            PointD offset = ClampOffset(ScrollOffset);

            double scale = LayoutScale <= 0 ? 1 : LayoutScale;
            _scrollOffsetUnscaled = new PointD(offset.X / scale, offset.Y / scale);

            if (offset.X == 0 && offset.Y == 0)
                return;

            // Shift the laid out children rather than translating the drawing context. The
            // context transform is invisible to SurfaceTransformBlur, which writes into the
            // surface buffer by absolute coordinates - a translated blur would land in the wrong
            // place. It also keeps hit testing working without any inverse transform, because
            // everything stays in one space.
            //
            // base.CalculateAllPositions recomputes every position from the parent and the
            // previous sibling, so this is not cumulative across passes.
            foreach (UIControl child in Children)
            {
                TranslateSubtree(child, -offset.X, -offset.Y);
            }
        }

        private static void TranslateSubtree(UIControl control, double deltaX, double deltaY)
        {
            control.Position = new PointD(control.Position.X + deltaX, control.Position.Y + deltaY);

            foreach (UIControl child in control.Children)
            {
                TranslateSubtree(child, deltaX, deltaY);
            }
        }

        /// <summary>
        /// Wires up wheel and drag. Called from both constructors rather than from the property
        /// setters, because a container has to react to the wheel from the first frame - the
        /// switches may be set before it is ever laid out.
        /// </summary>
        private void SubscribeScrollInput()
        {
            MouseWheel += OnScrollWheel;
            MouseDown += OnScrollMouseDown;
            MouseMove += OnScrollMouseMove;
            MouseUp += OnScrollMouseUp;
        }

        private void OnScrollWheel(object? sender, Events.MouseWheelEventArgs e)
        {
            if (!IsScrollable || e.IsHandled)
                return;

            ScrollLayout scroll = ResolveScroll();

            // Vanilla moves the content by scaled(102) per tick, in GuiElementScrollbar.
            double step = ScrollbarStyle.UnscaledWheelStep * LayoutScale * e.deltaPrecise;

            // A vertical bar takes the wheel; a container that only scrolls sideways gets it
            // too, which is what a horizontal strip of slots wants.
            bool moved = scroll.VerticalBarVisible
                ? ScrollBy(0, -step)
                : scroll.HorizontalBarVisible && ScrollBy(-step, 0);

            // Only claim the tick when it actually moved something. At the end of the content
            // it passes on, so a list inside a list hands over instead of swallowing it.
            if (moved)
                e.SetHandled(true);
        }

        private void OnScrollMouseDown(object? sender, Events.MouseEventArgs e)
        {
            if (!IsScrollable)
                return;

            ScrollLayout scroll = ResolveScroll();

            // Event coordinates are on screen, the layout is dialog local.
            PointD dialogPosition = Dialog?.Position ?? new PointD(0, 0);
            double localX = e.X - dialogPosition.X;
            double localY = e.Y - dialogPosition.Y;

            if (scroll.VerticalBarVisible &&
                TryStartDrag(scroll.VerticalTrack(LayoutScale), vertical: true, localX, localY, scroll))
            {
                return;
            }

            if (scroll.HorizontalBarVisible)
            {
                TryStartDrag(scroll.HorizontalTrack(LayoutScale), vertical: false, localX, localY, scroll);
            }
        }

        private bool TryStartDrag(
            LayoutRect track, bool vertical, double localX, double localY, ScrollLayout scroll)
        {
            if (!track.Contains(localX, localY))
                return false;

            double trackLength = vertical ? track.Height : track.Width;
            double viewportLength = vertical ? scroll.Viewport.Height : scroll.Viewport.Width;
            double contentLength = vertical ? MeasuredContentSize.Y : MeasuredContentSize.X;
            double maxOffset = vertical ? scroll.MaxOffset.Y : scroll.MaxOffset.X;
            double currentOffset = vertical ? ScrollOffset.Y : ScrollOffset.X;

            double handleLength = ScrollbarStyle.HandleLength(trackLength, viewportLength, contentLength);
            double handleStart = ScrollbarStyle.HandlePosition(trackLength, handleLength, currentOffset, maxOffset);

            double pointer = (vertical ? localY - track.Y : localX - track.X);
            double insideHandle = pointer - handleStart;

            if (insideHandle >= 0 && insideHandle <= handleLength)
            {
                // Grabbed the handle itself - remember where, so it does not jump under the
                // cursor the way it would if we centred it on every press.
                _dragGrabOffset = insideHandle;
            }
            else
            {
                // Clicked the empty groove. Vanilla jumps the handle there and centres it.
                _dragGrabOffset = handleLength / 2;
                ApplyDragPosition(pointer - _dragGrabOffset, trackLength, handleLength, maxOffset, vertical);
            }

            _dragging = vertical ? DragTarget.Vertical : DragTarget.Horizontal;
            Dialog?.CaptureMouse(this);
            return true;
        }

        private void OnScrollMouseMove(object? sender, Events.MouseEventArgs e)
        {
            if (_dragging == DragTarget.None)
                return;

            bool vertical = _dragging == DragTarget.Vertical;
            ScrollLayout scroll = ResolveScroll();
            LayoutRect track = vertical ? scroll.VerticalTrack(LayoutScale) : scroll.HorizontalTrack(LayoutScale);

            PointD dialogPosition = Dialog?.Position ?? new PointD(0, 0);
            double pointer = vertical
                ? e.Y - dialogPosition.Y - track.Y
                : e.X - dialogPosition.X - track.X;

            double trackLength = vertical ? track.Height : track.Width;
            double viewportLength = vertical ? scroll.Viewport.Height : scroll.Viewport.Width;
            double contentLength = vertical ? MeasuredContentSize.Y : MeasuredContentSize.X;
            double maxOffset = vertical ? scroll.MaxOffset.Y : scroll.MaxOffset.X;

            double handleLength = ScrollbarStyle.HandleLength(trackLength, viewportLength, contentLength);

            ApplyDragPosition(pointer - _dragGrabOffset, trackLength, handleLength, maxOffset, vertical);
        }

        private void ApplyDragPosition(
            double handlePosition, double trackLength, double handleLength, double maxOffset, bool vertical)
        {
            double offset = ScrollbarStyle.ScrollOffsetForHandlePosition(
                trackLength, handleLength, handlePosition, maxOffset);

            if (vertical)
                ScrollTo(ScrollOffset.X, offset);
            else
                ScrollTo(offset, ScrollOffset.Y);
        }

        private void OnScrollMouseUp(object? sender, Events.MouseEventArgs e)
        {
            if (_dragging == DragTarget.None)
                return;

            _dragging = DragTarget.None;
            Dialog?.ReleaseMouseCapture();
        }

        private void DrawScrollbars(Context ctx)
        {
            if (!IsScrollable)
                return;

            ScrollLayout scroll = ResolveScroll();

            if (scroll.VerticalBarVisible)
            {
                LayoutRect track = scroll.VerticalTrack(LayoutScale);
                ScrollbarStyle.DrawTrack(ctx, track, LayoutScale);

                double length = ScrollbarStyle.HandleLength(
                    track.Height, scroll.Viewport.Height, MeasuredContentSize.Y);
                double position = ScrollbarStyle.HandlePosition(
                    track.Height, length, ScrollOffset.Y, scroll.MaxOffset.Y);

                ScrollbarStyle.DrawHandle(ctx,
                    new LayoutRect(track.X, track.Y + position, track.Width, length));
            }

            if (scroll.HorizontalBarVisible)
            {
                LayoutRect track = scroll.HorizontalTrack(LayoutScale);
                ScrollbarStyle.DrawTrack(ctx, track, LayoutScale);

                double length = ScrollbarStyle.HandleLength(
                    track.Width, scroll.Viewport.Width, MeasuredContentSize.X);
                double position = ScrollbarStyle.HandlePosition(
                    track.Width, length, ScrollOffset.X, scroll.MaxOffset.X);

                ScrollbarStyle.DrawHandle(ctx,
                    new LayoutRect(track.X + position, track.Y, length, track.Height));
            }
        }
        #endregion

        // NEW: Apply Gaussian blur to border edges using SurfaceTransformBlur.BlurPartial
        private void ApplyBlurToBorders(ImageSurface surface)
        {
            double left = Position.X;
            double top = Position.Y;
            double right = Position.X + Size.X;
            double bottom = Position.Y + Size.Y;

            // A Cairo clip lives on the context, and BlurPartial writes into the surface buffer
            // directly - so it does not see one. Inside a clipping container the blur would
            // smear right past the edge of the viewport, which is exactly what a scrolled row
            // would do at the top and bottom of the visible area. Intersect by hand.
            LayoutRect? clip = EffectiveClip();
            if (clip.HasValue)
            {
                if (clip.Value.IsEmpty)
                    return;

                left = Math.Max(left, clip.Value.X);
                top = Math.Max(top, clip.Value.Y);
                right = Math.Min(right, clip.Value.Right);
                bottom = Math.Min(bottom, clip.Value.Bottom);

                if (right <= left || bottom <= top)
                    return;
            }

            int x = (int)left;
            int y = (int)top;
            int width = (int)(right - left);
            int height = (int)(bottom - top);

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
                    // BlurPartial reads the pixel buffer, so the drawing above has to land in
                    // it first - Cairo may still be holding it in the context.
                    surface.Flush();

                    // Blur radius is a geometric dimension, so it scales - the same way vanilla
                    // uses GuiElement.scaled(9.0) for its dialog background blur.
                    SurfaceTransformBlur.BlurPartial(
                        surface,
                        BlurRange * LayoutScale,
                        (int)Math.Round(BlurEdgeWidth * LayoutScale),
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
            ctx.LineWidth = ScaledBorderWidth;

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
            ctx.LineWidth = ScaledBorderWidth;

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
                        Position.X + ScaledRoundedCorners,
                        Position.Y + ScaledRoundedCorners,
                        ScaledRoundedCorners,
                        180.0 * radians,
                        270.0 * radians);
                }
                else
                {
                    ctx.MoveTo(Position.X, Position.Y);
                }

                // Top line
                ctx.LineTo(Position.X + Size.X - ScaledRoundedCorners, Position.Y);

                // Top-right corner
                if (hasRight)
                {
                    ctx.Arc(
                        Position.X + Size.X - ScaledRoundedCorners,
                        Position.Y + ScaledRoundedCorners,
                        ScaledRoundedCorners,
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
                        Position.X + Size.X - ScaledRoundedCorners,
                        Position.Y + ScaledRoundedCorners,
                        ScaledRoundedCorners,
                        -90.0 * radians,
                        0.0 * radians);
                }
                else
                {
                    ctx.MoveTo(Position.X + Size.X, Position.Y + ScaledRoundedCorners);
                }

                // Right line
                ctx.LineTo(Position.X + Size.X, Position.Y + Size.Y - ScaledRoundedCorners);

                // Bottom-right corner
                if (hasBottom)
                {
                    ctx.Arc(
                        Position.X + Size.X - ScaledRoundedCorners,
                        Position.Y + Size.Y - ScaledRoundedCorners,
                        ScaledRoundedCorners,
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
                        Position.X + Size.X - ScaledRoundedCorners,
                        Position.Y + Size.Y - ScaledRoundedCorners,
                        ScaledRoundedCorners,
                        0.0 * radians,
                        90.0 * radians);
                }
                else
                {
                    ctx.MoveTo(Position.X + Size.X - ScaledRoundedCorners, Position.Y + Size.Y);
                }

                // Bottom line
                ctx.LineTo(Position.X + ScaledRoundedCorners, Position.Y + Size.Y);

                // Bottom-left corner
                if (hasLeft)
                {
                    ctx.Arc(
                        Position.X + ScaledRoundedCorners,
                        Position.Y + Size.Y - ScaledRoundedCorners,
                        ScaledRoundedCorners,
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
                        Position.X + ScaledRoundedCorners,
                        Position.Y + Size.Y - ScaledRoundedCorners,
                        ScaledRoundedCorners,
                        90.0 * radians,
                        180.0 * radians);
                }
                else
                {
                    ctx.MoveTo(Position.X, Position.Y + Size.Y - ScaledRoundedCorners);
                }

                // Left line
                ctx.LineTo(Position.X, Position.Y + ScaledRoundedCorners);

                // Top-left corner
                if (hasTop)
                {
                    ctx.Arc(
                        Position.X + ScaledRoundedCorners,
                        Position.Y + ScaledRoundedCorners,
                        ScaledRoundedCorners,
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
                Position.X + Size.X - ScaledRoundedCorners,
                Position.Y + ScaledRoundedCorners,
                ScaledRoundedCorners,
                -90.0 * radians,
                0.0 * radians);

            // Bottom-right corner
            ctx.Arc(
                Position.X + Size.X - ScaledRoundedCorners,
                Position.Y + Size.Y - ScaledRoundedCorners,
                ScaledRoundedCorners,
                0.0 * radians,
                90.0 * radians);

            // Bottom-left corner
            ctx.Arc(
                Position.X + ScaledRoundedCorners,
                Position.Y + Size.Y - ScaledRoundedCorners,
                ScaledRoundedCorners,
                90.0 * radians,
                180.0 * radians);

            // Top-left corner
            ctx.Arc(
                Position.X + ScaledRoundedCorners,
                Position.Y + ScaledRoundedCorners,
                ScaledRoundedCorners,
                180.0 * radians,
                270.0 * radians);

            ctx.ClosePath();
        }
        #endregion
    }
}