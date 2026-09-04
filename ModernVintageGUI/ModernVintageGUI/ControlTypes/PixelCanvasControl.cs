using Cairo;
using IS2Mod.ControlTypes;
using IS2Mod.ControlTypes.Events;
using IS2Mod.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace ModernVintageGUI.ControlTypes
{
    /// <summary>Which pixel changed, to what, and whether a player did it.</summary>
    public class PixelPaintedEventArgs : EventArgs
    {
        public PixelPaintedEventArgs(int x, int y, ElementColor color, bool byPlayer)
        {
            X = x;
            Y = y;
            Color = color;
            ByPlayer = byPlayer;
        }

        /// <summary>The pixel, in canvas coordinates: 0,0 is the top left one.</summary>
        public int X { get; }

        public int Y { get; }

        /// <summary>What it is now.</summary>
        public ElementColor Color { get; }

        /// <summary>
        /// True when the player painted it with the mouse, false when code set it.
        ///
        /// A canvas that is shared over the network needs the difference: what the player did
        /// here has to be sent, and what arrived from the server must not be sent straight back.
        /// </summary>
        public bool ByPlayer { get; }
    }

    /// <summary>
    /// A grid of coloured pixels the player can paint in, in the spirit of r/place.
    ///
    /// The canvas is a grid of <see cref="Columns"/> x <see cref="Rows"/> pixels, each drawn
    /// <see cref="UnscaledPixelSize"/> author units across - so a pixel grows and shrinks with
    /// the GUI scale like everything else, down to a floor of one screen pixel, because a pixel
    /// smaller than that is not a pixel any more.
    ///
    /// Pixels go in one at a time, as an array, or stamped from an image or an icon scaled down
    /// to the size you give. What the player paints comes back out through
    /// <see cref="PixelPainted"/>.
    ///
    /// The colours live in an array of premultiplied ARGB and are blitted as one image rather
    /// than drawn as one rectangle per pixel. That is what makes a large canvas affordable: a
    /// 200x200 canvas is forty thousand rectangles per redraw the other way, and it redraws
    /// every time anything in the dialog changes.
    /// </summary>
    public class PixelCanvasControl : RectangleControl, IDisposable
    {
        #region Defaults
        /// <summary>How wide a pixel is by default, in author units.</summary>
        public const double DefaultPixelSize = 8.0;

        /// <summary>Below this the grid lines are not drawn - they would be the whole picture.</summary>
        private const double MinPixelSizeForGrid = 4.0;
        #endregion

        #region State
        /// <summary>One premultiplied ARGB value per pixel, row major.</summary>
        private int[] _argb;

        private int _columns;
        private int _rows;

        /// <summary>The canvas as a Cairo image, rebuilt from the array when it has changed.</summary>
        private ImageSurface? _backing;
        private bool _backingIsStale = true;

        private bool _painting;
        private int _lastPaintedX;
        private int _lastPaintedY;
        private bool _isDisposed;
        #endregion

        public PixelCanvasControl(
            int columns = 16,
            int rows = 16,
            double unscaledPixelSize = DefaultPixelSize,
            string _Name = "",
            double _Margin = 5)
            : base(borderWidth: 0, _Name: _Name, _Orientation: Orientation.None, _Margin: _Margin, _Padding: 0)
        {
            _columns = Math.Max(1, columns);
            _rows = Math.Max(1, rows);
            UnscaledPixelSize = Math.Max(0.1, unscaledPixelSize);

            _argb = new int[_columns * _rows];

            MouseDown += OnMouseDownHere;
            MouseMove += OnMouseMoveHere;
            MouseUp += OnMouseUpHere;
        }

        #region Shape
        public int Columns => _columns;

        public int Rows => _rows;

        /// <summary>How wide one canvas pixel is, in author units.</summary>
        public double UnscaledPixelSize { get; set; }

        /// <summary>
        /// How wide one canvas pixel actually is, in screen pixels.
        ///
        /// Whole pixels, and never less than one: a fractional size would have Cairo smear one
        /// canvas pixel across two screen ones, and a size below one would drop pixels
        /// altogether at small GUI scales.
        /// </summary>
        public double PixelSize => Math.Max(1.0, Math.Floor(UnscaledPixelSize * LayoutScale));

        /// <summary>The whole canvas in screen pixels, which is what the control measures to.</summary>
        public double CanvasWidth => _columns * PixelSize;

        public double CanvasHeight => _rows * PixelSize;

        /// <summary>
        /// Changes the size of the canvas. Everything that still fits keeps its colour, so
        /// growing a canvas does not wipe it; what falls outside is gone.
        /// </summary>
        public void Resize(int columns, int rows)
        {
            columns = Math.Max(1, columns);
            rows = Math.Max(1, rows);

            if (columns == _columns && rows == _rows)
                return;

            var kept = new int[columns * rows];

            for (int y = 0; y < rows; y++)
            {
                for (int x = 0; x < columns; x++)
                {
                    kept[y * columns + x] = x < _columns && y < _rows ? _argb[y * _columns + x] : 0;
                }
            }

            _argb = kept;
            _columns = columns;
            _rows = rows;

            // A highlight names pixels that may not exist any more.
            _highlight = null;

            DiscardBacking();

            // The control measures to the canvas, so this is a layout change and not just a
            // repaint.
            RecomposeToMain();
        }
        #endregion

        #region Look
        /// <summary>
        /// Lines between the pixels, drawn only while a pixel is big enough to have room for
        /// them. Off by default - a picture is a picture.
        /// </summary>
        public bool ShowGrid { get; set; }

        public ElementColor GridColor { get; set; } = new ElementColor(0, 0, 0, 60);
        #endregion

        #region Highlighting an area
        /// <summary>The pixels currently outlined, or null. Set it with <see cref="SetHighlight"/>.</summary>
        private HashSet<Vec2i>? _highlight;

        /// <summary>
        /// The colour of the outline. Changing it repaints straight away, so it can follow
        /// whatever the dialog is doing - a hover, a selection, a warning.
        /// </summary>
        public ElementColor HighlightColor
        {
            get => _highlightColor;
            set
            {
                _highlightColor = value;

                if (_highlight != null)
                {
                    Dialog?.Refresh();
                }
            }
        }

        private ElementColor _highlightColor = new ElementColor(255, 255, 255, 230);

        /// <summary>How thick the outline is, in author units.</summary>
        public double UnscaledHighlightWidth { get; set; } = 1.5;

        /// <summary>Whether anything is outlined at the moment.</summary>
        public bool HasHighlight => _highlight != null && _highlight.Count > 0;

        /// <summary>The outlined pixels, or an empty array.</summary>
        public Vec2i[] HighlightedArea => _highlight?.ToArray() ?? Array.Empty<Vec2i>();

        /// <summary>
        /// Outlines a set of pixels. They have to hang together edge to edge, and the outline is
        /// drawn along the outside of the whole set - never between two pixels of it.
        /// </summary>
        /// <param name="colorSensitive">
        /// Also require every pixel in the set to be the same colour. Off by default: an area is
        /// an area whatever it is painted in.
        /// </param>
        /// <returns>
        /// false when the pixels are not all connected, are not all the same colour under
        /// <paramref name="colorSensitive"/>, or there are none - and then nothing changes,
        /// rather than a broken outline being drawn.
        /// </returns>
        public bool SetHighlight(IEnumerable<Vec2i> pixels, bool colorSensitive = false)
        {
            if (pixels == null)
                return false;

            var set = new HashSet<Vec2i>();

            foreach (Vec2i pixel in pixels)
            {
                if (pixel != null && Contains(pixel.X, pixel.Y))
                {
                    set.Add(new Vec2i(pixel.X, pixel.Y));
                }
            }

            if (!AreConnected(set))
                return false;

            if (colorSensitive && !AreOneColour(set))
                return false;

            // Outlining what is already outlined is not a change, and saying so matters: the
            // usual caller is a mouse move, which arrives many times a second while the cursor
            // sits on the same area, and every one of them would otherwise redraw the dialog.
            if (_highlight != null && _highlight.SetEquals(set))
                return true;

            _highlight = set;
            Dialog?.Refresh();

            return true;
        }

        /// <summary>
        /// Outlines the area the pixel at x,y belongs to - what the mouse is over, most of the
        /// time. Same thing as <see cref="GetArea"/> followed by <see cref="SetHighlight"/>.
        /// </summary>
        public bool HighlightAreaAt(int x, int y, bool colorSensitive = true)
        {
            return SetHighlight(GetArea(x, y, colorSensitive));
        }

        public void ClearHighlight()
        {
            if (_highlight == null)
                return;

            _highlight = null;
            Dialog?.Refresh();
        }

        /// <summary>
        /// The area a pixel belongs to: everything reachable from it by steps between
        /// neighbours that share what it is.
        ///
        /// With <paramref name="colorSensitive"/> on - the usual case - that is the same colour,
        /// so pointing at one pixel of a red line gives back the whole line. With it off it is
        /// anything painted at all, so pointing at a drawing on an empty canvas gives back the
        /// drawing.
        ///
        /// Steps are up, down, left and right. Two pixels that meet only at a corner are two
        /// areas, which is what makes a diagonal line a row of single pixels rather than one
        /// area - and is the same rule the outline is drawn by, so the two always agree.
        /// </summary>
        public Vec2i[] GetArea(int x, int y, bool colorSensitive = true)
        {
            if (!Contains(x, y))
                return Array.Empty<Vec2i>();

            int start = _argb[y * _columns + x];

            var found = new HashSet<Vec2i>();
            var queue = new Queue<Vec2i>();
            var first = new Vec2i(x, y);

            found.Add(first);
            queue.Enqueue(first);

            while (queue.Count > 0)
            {
                Vec2i current = queue.Dequeue();

                foreach (Vec2i step in Steps)
                {
                    var next = new Vec2i(current.X + step.X, current.Y + step.Y);

                    if (!Contains(next.X, next.Y) || found.Contains(next))
                        continue;

                    if (!Matches(start, _argb[next.Y * _columns + next.X], colorSensitive))
                        continue;

                    found.Add(next);
                    queue.Enqueue(next);
                }
            }

            return found.ToArray();
        }

        /// <summary>
        /// Whether every one of these pixels can be reached from every other by steps between
        /// neighbours. A single pixel is an area; none is not.
        /// </summary>
        public static bool AreConnected(IEnumerable<Vec2i> pixels)
        {
            var set = new HashSet<Vec2i>();

            foreach (Vec2i pixel in pixels)
            {
                if (pixel != null)
                {
                    set.Add(new Vec2i(pixel.X, pixel.Y));
                }
            }

            if (set.Count == 0)
                return false;

            Vec2i start = set.First();

            var reached = new HashSet<Vec2i> { start };
            var queue = new Queue<Vec2i>();
            queue.Enqueue(start);

            while (queue.Count > 0)
            {
                Vec2i current = queue.Dequeue();

                foreach (Vec2i step in Steps)
                {
                    var next = new Vec2i(current.X + step.X, current.Y + step.Y);

                    if (!set.Contains(next) || !reached.Add(next))
                        continue;

                    queue.Enqueue(next);
                }
            }

            return reached.Count == set.Count;
        }

        /// <summary>Up, down, left and right. Corners are deliberately not in here.</summary>
        private static readonly Vec2i[] Steps =
        {
            new Vec2i(0, -1), new Vec2i(1, 0), new Vec2i(0, 1), new Vec2i(-1, 0)
        };

        private bool AreOneColour(HashSet<Vec2i> pixels)
        {
            int? colour = null;

            foreach (Vec2i pixel in pixels)
            {
                int value = _argb[pixel.Y * _columns + pixel.X];

                if (colour == null)
                {
                    colour = value;
                }
                else if (colour != value)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Whether two pixels count as the same thing for an area: the same colour when that
        /// matters, and otherwise both painted or both empty.
        /// </summary>
        private static bool Matches(int a, int b, bool colorSensitive)
        {
            if (colorSensitive)
                return a == b;

            return ((a >> 24) & 0xFF) > 0 == ((b >> 24) & 0xFF) > 0;
        }
        #endregion

        #region Painting by the player
        /// <summary>
        /// Whether the player can paint on this canvas. Off by default, so a canvas that is only
        /// showing something cannot be scribbled on.
        /// </summary>
        public bool DrawMode { get; set; }

        /// <summary>What the player paints with.</summary>
        public ElementColor DrawColor { get; set; } = ElementColor.White;

        /// <summary>
        /// Which button paints. The right one by default, which leaves the left free for
        /// whatever the dialog around this wants it for - picking a colour, most likely.
        /// </summary>
        public EnumMouseButton PaintButton { get; set; } = EnumMouseButton.Right;

        /// <summary>Raised for every pixel that changed, whoever changed it.</summary>
        public event EventHandler<PixelPaintedEventArgs>? PixelPainted;
        #endregion

        #region Reading and writing pixels
        public ElementColor GetPixel(int x, int y)
        {
            return Contains(x, y) ? FromArgb(_argb[y * _columns + x]) : ElementColor.Transparent;
        }

        /// <summary>Sets one pixel. Out of range coordinates are ignored rather than thrown at.</summary>
        public void SetPixel(int x, int y, ElementColor color)
        {
            if (Set(x, y, color, byPlayer: false))
            {
                Invalidate();
            }
        }

        /// <summary>
        /// Sets a rectangle of pixels with its top left corner at x,y. The block is indexed
        /// [row, column], the way a picture reads.
        /// </summary>
        public void SetPixels(int x, int y, ElementColor[,] block)
        {
            if (block == null)
                return;

            bool changed = false;

            for (int row = 0; row < block.GetLength(0); row++)
            {
                for (int column = 0; column < block.GetLength(1); column++)
                {
                    changed |= Set(x + column, y + row, block[row, column], byPlayer: false);
                }
            }

            if (changed)
            {
                Invalidate();
            }
        }

        /// <summary>
        /// Replaces the whole canvas from one row major array of <see cref="Columns"/> x
        /// <see cref="Rows"/> colours - the shape <see cref="ToArray"/> gives back, so a canvas
        /// can be sent over the network and put back together on the other side.
        /// </summary>
        public void SetPixels(ElementColor[] pixels)
        {
            if (pixels == null)
                return;

            int count = Math.Min(pixels.Length, _argb.Length);
            bool changed = false;

            for (int i = 0; i < count; i++)
            {
                changed |= Set(i % _columns, i / _columns, pixels[i], byPlayer: false);
            }

            if (changed)
            {
                Invalidate();
            }
        }

        /// <summary>The whole canvas, row major.</summary>
        public ElementColor[] ToArray()
        {
            var pixels = new ElementColor[_argb.Length];

            for (int i = 0; i < _argb.Length; i++)
            {
                pixels[i] = FromArgb(_argb[i]);
            }

            return pixels;
        }

        public void Fill(ElementColor color)
        {
            bool changed = false;

            for (int y = 0; y < _rows; y++)
            {
                for (int x = 0; x < _columns; x++)
                {
                    changed |= Set(x, y, color, byPlayer: false);
                }
            }

            if (changed)
            {
                Invalidate();
            }
        }

        /// <summary>Empties the canvas back to fully transparent.</summary>
        public void Clear()
        {
            Fill(ElementColor.Transparent);
        }

        /// <summary>Whether these coordinates are on the canvas.</summary>
        public bool Contains(int x, int y)
        {
            return x >= 0 && y >= 0 && x < _columns && y < _rows;
        }
        #endregion

        #region Getting the picture out
        /// <summary>
        /// The canvas as an image, one image pixel per canvas pixel unless
        /// <paramref name="scale"/> says otherwise.
        ///
        /// **The caller owns it and has to dispose it.** It is a fresh surface rather than the
        /// one this control draws from, so keeping it, saving it or handing it to
        /// <c>capi.Gui.LoadCairoTexture</c> cannot pull the canvas out from under itself.
        ///
        /// Scaling up is whole pixels and nearest neighbour, so a canvas exported at 8 comes out
        /// as the same picture with fat square pixels and not as a blurred one.
        /// </summary>
        public ImageSurface ToImageSurface(int scale = 1)
        {
            scale = Math.Max(1, scale);

            var image = new ImageSurface(Format.Argb32, _columns * scale, _rows * scale);

            EnsureBacking();

            if (_backing == null)
                return image;

            using (var ctx = new Context(image))
            {
                ctx.Antialias = Antialias.None;
                ctx.Scale(scale, scale);

                using var pattern = new SurfacePattern(_backing) { Filter = Filter.Nearest };
                ctx.SetSource(pattern);
                ctx.Rectangle(0, 0, _columns, _rows);
                ctx.Fill();
            }

            image.Flush();
            return image;
        }

        /// <summary>
        /// Writes the canvas to a PNG file, at <paramref name="scale"/> image pixels per canvas
        /// pixel. Transparent pixels stay transparent.
        /// </summary>
        public void SavePng(string path, int scale = 1)
        {
            using ImageSurface image = ToImageSurface(scale);
            image.WriteToPng(path);
        }

        /// <summary>
        /// The raw pixels, row major, as premultiplied ARGB - the form Cairo and the game's
        /// texture loader both want. <see cref="ToArray"/> gives the same picture as colours.
        /// </summary>
        public int[] ToArgb()
        {
            return (int[])_argb.Clone();
        }
        #endregion

        #region Screen positions
        /// <summary>
        /// The pixel under a point on the screen - what a mouse event carries.
        ///
        /// Mouse coordinates are screen coordinates while the layout is dialog local, and the
        /// canvas may also be scrolled, so both have to be taken off before the division.
        /// </summary>
        public bool TryGetPixelAt(int screenX, int screenY, out int x, out int y)
        {
            x = 0;
            y = 0;

            PointD dialogPosition = Dialog?.Position ?? new PointD(0, 0);
            LayoutRect box = ContentBox();

            double localX = screenX - dialogPosition.X - box.X + ScrollOffset.X;
            double localY = screenY - dialogPosition.Y - box.Y + ScrollOffset.Y;

            if (localX < 0 || localY < 0)
                return false;

            double pixel = PixelSize;

            x = (int)(localX / pixel);
            y = (int)(localY / pixel);

            return Contains(x, y);
        }

        /// <summary>Sets the pixel under a point on the screen, if there is one there.</summary>
        public bool SetPixelAtScreen(int screenX, int screenY, ElementColor color)
        {
            if (!TryGetPixelAt(screenX, screenY, out int x, out int y))
                return false;

            SetPixel(x, y, color);
            return true;
        }
        #endregion

        #region Images and icons
        /// <summary>
        /// Stamps an image onto the canvas, scaled down to <paramref name="width"/> x
        /// <paramref name="height"/> canvas pixels.
        ///
        /// The scaling is Cairo's, so a picture reduced to twenty pixels across looks like
        /// someone reduced it and not like someone threw most of it away. Transparent parts of
        /// the image leave what was underneath.
        /// </summary>
        /// <returns>false when there is no client yet, or the image could not be loaded.</returns>
        public bool DrawImage(AssetLocation asset, int x, int y, int width, int height)
        {
            ICoreClientAPI? api = Dialog?.Api;

            if (api == null || asset == null || width <= 0 || height <= 0)
                return false;

            try
            {
                using ImageSurface source = GuiElement.getImageSurfaceFromAsset(api, asset);

                return Stamp(x, y, width, height, ctx =>
                {
                    ctx.Scale(width / (double)source.Width, height / (double)source.Height);

                    using var pattern = new SurfacePattern(source) { Filter = Filter.Good };
                    ctx.SetSource(pattern);
                    ctx.Paint();
                });
            }
            catch (Exception e)
            {
                api.Logger.Warning("[ModernVintageGUI] Could not stamp the image '{0}' onto a canvas: {1}", asset, e);
                return false;
            }
        }

        /// <summary>
        /// Stamps one of the game's icons onto the canvas, at that many canvas pixels across.
        /// The names are the ones in <see cref="GuiIcons"/>.
        /// </summary>
        public bool DrawIcon(string iconName, int x, int y, int width, int height, ElementColor color)
        {
            ICoreClientAPI? api = Dialog?.Api;

            if (api == null || string.IsNullOrEmpty(iconName) || width <= 0 || height <= 0)
                return false;

            double[] rgba = { color.RNormalized, color.GNormalized, color.BNormalized, color.ANormalized };

            try
            {
                return Stamp(x, y, width, height, ctx => api.Gui.Icons.DrawIcon(ctx, iconName, 0, 0, width, height, rgba));
            }
            catch (Exception e)
            {
                // Icons are drawn by whoever registered them, which can be another mod - see
                // ImageControl for the one in the game that throws. Losing the stamp is a hole
                // in a picture; letting it through takes the client down.
                api.Logger.Warning("[ModernVintageGUI] The GUI icon '{0}' threw while being stamped onto a canvas: {1}", iconName, e);
                return false;
            }
        }

        /// <summary>
        /// Draws something into a surface the size of the target area and composites the result
        /// onto the canvas, one pixel at a time.
        /// </summary>
        private bool Stamp(int x, int y, int width, int height, Action<Context> draw)
        {
            using var scratch = new ImageSurface(Format.Argb32, width, height);

            using (var ctx = new Context(scratch))
            {
                draw(ctx);
            }

            scratch.Flush();

            if (scratch.DataPtr == IntPtr.Zero)
                return false;

            var row = new int[width];
            bool changed = false;

            for (int line = 0; line < height; line++)
            {
                Marshal.Copy(scratch.DataPtr + line * scratch.Stride, row, 0, width);

                for (int column = 0; column < width; column++)
                {
                    int target = (y + line) * _columns + (x + column);

                    if (!Contains(x + column, y + line))
                        continue;

                    int blended = Over(row[column], _argb[target]);

                    if (blended == _argb[target])
                        continue;

                    _argb[target] = blended;
                    changed = true;

                    PixelPainted?.Invoke(this, new PixelPaintedEventArgs(x + column, y + line, FromArgb(blended), byPlayer: false));
                }
            }

            if (changed)
            {
                Invalidate();
            }

            return true;
        }
        #endregion

        #region Interaction
        private void OnMouseDownHere(object? sender, MouseEventArgs e)
        {
            if (!DrawMode || e.Button != PaintButton)
                return;

            if (!TryGetPixelAt(e.X, e.Y, out int x, out int y))
                return;

            _painting = true;
            _lastPaintedX = x;
            _lastPaintedY = y;

            // Without the capture the rest of the stroke goes to whatever the cursor wanders
            // over, and a stroke that leaves the canvas and comes back would be two strokes.
            Dialog?.CaptureMouse(this);

            Paint(x, y);

            e.Handled = true;
        }

        private void OnMouseMoveHere(object? sender, MouseEventArgs e)
        {
            if (!_painting)
                return;

            if (!TryGetPixelAt(e.X, e.Y, out int x, out int y))
                return;

            // Straight from the last pixel to this one. A mouse moving quickly reports a handful
            // of positions a second, so painting only where it was seen leaves a dotted line
            // with holes the size of the gaps between reports.
            PaintLine(_lastPaintedX, _lastPaintedY, x, y);

            _lastPaintedX = x;
            _lastPaintedY = y;
        }

        private void OnMouseUpHere(object? sender, MouseEventArgs e)
        {
            _painting = false;
        }

        /// <summary>Bresenham, so every pixel the line passes through is painted exactly once.</summary>
        private void PaintLine(int fromX, int fromY, int toX, int toY)
        {
            int dx = Math.Abs(toX - fromX);
            int dy = -Math.Abs(toY - fromY);
            int stepX = fromX < toX ? 1 : -1;
            int stepY = fromY < toY ? 1 : -1;
            int error = dx + dy;

            while (true)
            {
                Paint(fromX, fromY);

                if (fromX == toX && fromY == toY)
                    return;

                int doubled = error * 2;

                if (doubled >= dy)
                {
                    error += dy;
                    fromX += stepX;
                }

                if (doubled <= dx)
                {
                    error += dx;
                    fromY += stepY;
                }
            }
        }

        private void Paint(int x, int y)
        {
            // Painting the colour that is already there changes nothing, and skipping it is what
            // keeps a stroke from redrawing the dialog once per mouse report.
            if (Set(x, y, DrawColor, byPlayer: true))
            {
                Invalidate();
            }
        }
        #endregion

        #region Layout and drawing
        public override PointD CalculateSize()
        {
            foreach (UIControl child in Children)
            {
                child.CalculateSize();
            }

            // The whole canvas, including whatever is scrolled out of sight - that is what the
            // scrolling container compares against its viewport to decide about bars.
            MeasuredContentSize = new PointD(CanvasWidth, CanvasHeight);

            PointD measured = ClampToMaxSize(IsAutoSize
                ? new PointD(CanvasWidth + ScaledPadding * 2, CanvasHeight + ScaledPadding * 2)
                : ScaledExplicitSize);

            CalculatedSize = measured;
            SetLayoutSize(measured);

            return measured;
        }

        public override void GenerateRenderData(ImageSurface surface, Context ctx)
        {
            base.GenerateRenderData(surface, ctx);

            LayoutRect box = ContentBox();

            if (box.IsEmpty)
                return;

            EnsureBacking();

            if (_backing == null)
                return;

            double pixel = PixelSize;

            ctx.Save();

            // Clipped here rather than left to the container, so a canvas larger than its box is
            // cut at the box whether or not anyone switched clipping on.
            ctx.Rectangle(box.X, box.Y, box.Width, box.Height);
            ctx.Clip();

            ctx.Antialias = Antialias.None;
            ctx.Translate(box.X - ScrollOffset.X, box.Y - ScrollOffset.Y);
            ctx.Scale(pixel, pixel);

            using (var pattern = new SurfacePattern(_backing) { Filter = Filter.Nearest })
            {
                ctx.SetSource(pattern);
                ctx.Rectangle(0, 0, _columns, _rows);
                ctx.Fill();
            }

            ctx.Restore();

            if (ShowGrid && pixel >= MinPixelSizeForGrid)
            {
                DrawGrid(ctx, box, pixel);
            }

            if (_highlight != null && _highlight.Count > 0)
            {
                DrawHighlight(ctx, box, pixel);
            }
        }

        /// <summary>
        /// Outlines the highlighted area: for every pixel in it, the sides that face out of it.
        ///
        /// Sides between two pixels of the area are not drawn, which is what makes an area of
        /// twenty pixels look like one shape and not like twenty squares - and it falls out of
        /// the rule rather than needing a pass to remove the inner lines.
        ///
        /// The line sits just inside the edge, and its ends are the fiddly part: at a corner of
        /// the area, where two drawn sides meet, both stop at the inset so they meet exactly; at
        /// a side that continues into the next pixel of the area, the line runs all the way to
        /// the pixel boundary so it joins the next one seamlessly. Getting that wrong leaves
        /// either notches at the corners or stubs sticking into the area.
        /// </summary>
        private void DrawHighlight(Context ctx, LayoutRect box, double pixel)
        {
            double width = Math.Max(1.0, Math.Round(UnscaledHighlightWidth * LayoutScale));

            // Never so thick that it swallows the pixel it is outlining.
            width = Math.Min(width, Math.Max(1.0, Math.Floor(pixel / 3.0)));

            double inset = width / 2.0;

            double originX = box.X - ScrollOffset.X;
            double originY = box.Y - ScrollOffset.Y;

            ctx.Save();

            ctx.Rectangle(box.X, box.Y, box.Width, box.Height);
            ctx.Clip();

            ctx.Antialias = Antialias.None;
            ctx.LineWidth = width;
            ctx.LineCap = LineCap.Butt;
            ctx.SetSourceRGBA(
                HighlightColor.RNormalized,
                HighlightColor.GNormalized,
                HighlightColor.BNormalized,
                HighlightColor.ANormalized);

            foreach (Vec2i cell in _highlight!)
            {
                double left = originX + cell.X * pixel;
                double top = originY + cell.Y * pixel;
                double right = left + pixel;
                double bottom = top + pixel;

                bool north = Inside(cell.X, cell.Y - 1);
                bool east = Inside(cell.X + 1, cell.Y);
                bool south = Inside(cell.X, cell.Y + 1);
                bool west = Inside(cell.X - 1, cell.Y);

                if (!north)
                {
                    ctx.MoveTo(left + (west ? 0 : inset), top + inset);
                    ctx.LineTo(right - (east ? 0 : inset), top + inset);
                }

                if (!south)
                {
                    ctx.MoveTo(left + (west ? 0 : inset), bottom - inset);
                    ctx.LineTo(right - (east ? 0 : inset), bottom - inset);
                }

                if (!west)
                {
                    ctx.MoveTo(left + inset, top + (north ? 0 : inset));
                    ctx.LineTo(left + inset, bottom - (south ? 0 : inset));
                }

                if (!east)
                {
                    ctx.MoveTo(right - inset, top + (north ? 0 : inset));
                    ctx.LineTo(right - inset, bottom - (south ? 0 : inset));
                }
            }

            ctx.Stroke();
            ctx.Restore();
        }

        private bool Inside(int x, int y)
        {
            return _highlight != null && _highlight.Contains(new Vec2i(x, y));
        }

        private void DrawGrid(Context ctx, LayoutRect box, double pixel)
        {
            ctx.Save();

            ctx.Rectangle(box.X, box.Y, box.Width, box.Height);
            ctx.Clip();

            ctx.Antialias = Antialias.None;
            ctx.LineWidth = 1;
            ctx.SetSourceRGBA(GridColor.RNormalized, GridColor.GNormalized, GridColor.BNormalized, GridColor.ANormalized);

            double originX = box.X - ScrollOffset.X;
            double originY = box.Y - ScrollOffset.Y;

            for (int column = 0; column <= _columns; column++)
            {
                double x = Math.Floor(originX + column * pixel) + 0.5;
                ctx.MoveTo(x, originY);
                ctx.LineTo(x, originY + CanvasHeight);
            }

            for (int row = 0; row <= _rows; row++)
            {
                double y = Math.Floor(originY + row * pixel) + 0.5;
                ctx.MoveTo(originX, y);
                ctx.LineTo(originX + CanvasWidth, y);
            }

            ctx.Stroke();
            ctx.Restore();
        }
        #endregion

        #region The pixel buffer
        /// <summary>
        /// Writes one pixel into the buffer and reports it. Returns whether anything changed, so
        /// a caller that writes many can redraw once instead of once per pixel.
        /// </summary>
        private bool Set(int x, int y, ElementColor color, bool byPlayer)
        {
            if (!Contains(x, y))
                return false;

            int index = y * _columns + x;
            int value = ToArgb(color);

            if (_argb[index] == value)
                return false;

            _argb[index] = value;

            PixelPainted?.Invoke(this, new PixelPaintedEventArgs(x, y, color, byPlayer));
            return true;
        }

        private void Invalidate()
        {
            _backingIsStale = true;

            // The pixels changed, not the layout - so the surface is redrawn and nothing is
            // measured again.
            Dialog?.Refresh();
        }

        /// <summary>
        /// Brings the Cairo image up to date with the array, and only then. Copying a canvas of
        /// forty thousand pixels is nothing next to doing it on every redraw of the dialog.
        /// </summary>
        private void EnsureBacking()
        {
            if (_backing != null && !_backingIsStale)
                return;

            _backing ??= new ImageSurface(Format.Argb32, _columns, _rows);

            if (_backing.DataPtr == IntPtr.Zero)
                return;

            int stride = _backing.Stride;

            for (int y = 0; y < _rows; y++)
            {
                Marshal.Copy(_argb, y * _columns, _backing.DataPtr + y * stride, _columns);
            }

            _backing.MarkDirty();
            _backingIsStale = false;
        }

        private void DiscardBacking()
        {
            _backing?.Dispose();
            _backing = null;
            _backingIsStale = true;

            Dialog?.Refresh();
        }

        /// <summary>
        /// A colour as Cairo wants it: one native endian int per pixel, alpha in the top byte,
        /// and the three channels already multiplied by it.
        ///
        /// The premultiplication is not decoration. Cairo reads ARGB32 that way, and a half
        /// transparent red written without it comes out as a bright edge wherever the canvas is
        /// blended.
        /// </summary>
        private static int ToArgb(ElementColor color)
        {
            int a = color.A;

            int r = color.R * a / 255;
            int g = color.G * a / 255;
            int b = color.B * a / 255;

            return (a << 24) | (r << 16) | (g << 8) | b;
        }

        private static ElementColor FromArgb(int value)
        {
            int a = (value >> 24) & 0xFF;

            if (a == 0)
                return ElementColor.Transparent;

            int r = ((value >> 16) & 0xFF) * 255 / a;
            int g = ((value >> 8) & 0xFF) * 255 / a;
            int b = (value & 0xFF) * 255 / a;

            return new ElementColor(
                (byte)Math.Min(255, r),
                (byte)Math.Min(255, g),
                (byte)Math.Min(255, b),
                (byte)a);
        }

        /// <summary>Source over destination, both premultiplied.</summary>
        private static int Over(int source, int destination)
        {
            int sourceAlpha = (source >> 24) & 0xFF;

            if (sourceAlpha == 255)
                return source;

            if (sourceAlpha == 0)
                return destination;

            int inverse = 255 - sourceAlpha;

            int a = sourceAlpha + ((destination >> 24) & 0xFF) * inverse / 255;
            int r = ((source >> 16) & 0xFF) + ((destination >> 16) & 0xFF) * inverse / 255;
            int g = ((source >> 8) & 0xFF) + ((destination >> 8) & 0xFF) * inverse / 255;
            int b = (source & 0xFF) + (destination & 0xFF) * inverse / 255;

            return (Math.Min(255, a) << 24) | (Math.Min(255, r) << 16) | (Math.Min(255, g) << 8) | Math.Min(255, b);
        }
        #endregion

        public void Dispose()
        {
            if (_isDisposed)
                return;

            _isDisposed = true;

            _backing?.Dispose();
            _backing = null;
        }
    }
}
