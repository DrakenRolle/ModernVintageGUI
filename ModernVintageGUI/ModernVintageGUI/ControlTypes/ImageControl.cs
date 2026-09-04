using Cairo;
using IS2Mod.ControlTypes;
using IS2Mod.Enums;
using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace ModernVintageGUI.ControlTypes
{
    /// <summary>How an image is fitted into a box that is not its own shape.</summary>
    public enum ImageFit
    {
        /// <summary>Whole image, aspect kept, letterboxed in the box.</summary>
        Contain,

        /// <summary>Fills the box, aspect kept, the overflow cut off.</summary>
        Cover,

        /// <summary>Fills the box exactly, aspect ignored.</summary>
        Stretch,

        /// <summary>Drawn at its own size, centred.</summary>
        None
    }

    /// <summary>
    /// A picture from the mod's assets, or one of the game's GUI icons.
    ///
    /// Both go into the Cairo surface rather than being drawn per frame, because a picture does
    /// not change between frames - the per frame pass is for things that cannot be a bitmap, and
    /// a bitmap is exactly what this is.
    ///
    /// <code>
    /// new ImageControl(new AssetLocation("mymod:textures/gui/logo.png"))
    /// new ImageControl { IconName = "wrench" }
    /// </code>
    /// </summary>
    public class ImageControl : UIControl
    {
        /// <summary>
        /// Loaded images, by asset. Loading one means decoding a PNG and copying it into a Cairo
        /// surface, which is far too much to do again on every redraw - and a dialog redraws
        /// whenever anything in it is hovered.
        /// </summary>
        private static readonly Dictionary<string, ImageSurface> Cache = new Dictionary<string, ImageSurface>();

        private AssetLocation? _asset;
        private string? _iconName;

        #region Properties
        /// <summary>The picture to show. Null to show nothing, or to use <see cref="IconName"/>.</summary>
        public AssetLocation? Asset
        {
            get => _asset;
            set
            {
                _asset = value;
                RecomposeToMain();
            }
        }

        /// <summary>One of the game's own GUI icons, drawn in <see cref="IconColor"/>.</summary>
        public string? IconName
        {
            get => _iconName;
            set
            {
                _iconName = value;
                RecomposeToMain();
            }
        }

        /// <summary>The colour a named icon is drawn in. Ignored for an asset.</summary>
        public ElementColor IconColor { get; set; } = new ElementColor(GuiStyle.DialogDefaultTextColor);

        /// <summary>How the picture is fitted into the control's box.</summary>
        public ImageFit Fit { get; set; } = ImageFit.Contain;

        /// <summary>Drawn over the picture, 0 to 1. For a disabled look, or a fade.</summary>
        public double Opacity { get; set; } = 1.0;
        #endregion

        public ImageControl(AssetLocation? asset = null, string _Name = "", PointD? _Size = null, double _Margin = 0)
            : base(_Name, _Size ?? new PointD(32, 32), Orientation.None, _Margin, _Padding: 0)
        {
            _asset = asset;
            IsAutoSize = false;
        }

        #region Layout
        public override PointD CalculateSize()
        {
            foreach (UIControl child in Children)
            {
                child.CalculateSize();
            }

            PointD measured = ClampToMaxSize(ScaledExplicitSize);

            CalculatedSize = measured;
            SetLayoutSize(measured);

            return measured;
        }
        #endregion

        #region Rendering
        public override void GenerateRenderData(ImageSurface surface, Context ctx)
        {
            if (Size.X <= 0 || Size.Y <= 0)
                return;

            ICoreClientAPI? api = Dialog?.Api;

            if (api != null && _iconName != null)
            {
                DrawIcon(api, ctx);
            }
            else if (api != null && _asset != null)
            {
                try
                {
                    DrawAsset(api, ctx);
                }
                catch (Exception e)
                {
                    // Same reasoning as for icons: a picture that cannot be drawn must cost a
                    // picture, not the frame.
                    api.Logger.Warning("[ModernVintageGUI] Could not draw the image '{0}': {1}", _asset, e);
                    _asset = null;
                }
            }

            base.GenerateRenderData(surface, ctx);
        }

        /// <summary>Names already complained about, so the log does not fill up per redraw.</summary>
        private static readonly HashSet<string> WarnedIcons = new HashSet<string>();

        /// <summary>Icons that threw once. Never drawn again - see <see cref="DrawIcon"/>.</summary>
        private static readonly HashSet<string> BrokenIcons = new HashSet<string>();

        private void DrawIcon(ICoreClientAPI api, Context ctx)
        {
            if (BrokenIcons.Contains(_iconName!))
                return;

            try
            {
                DrawIconUnguarded(api, ctx);
            }
            catch (Exception e)
            {
                // An icon is drawn by whoever registered it, and that can be another mod: the
                // waypoint icons of the map, for one, throw when their SVG is not loaded because
                // the map is not open. This runs inside the game's render loop, so letting it
                // through does not lose an icon - it takes the client down with it.
                BrokenIcons.Add(_iconName!);

                api.Logger.Warning(
                    "[ModernVintageGUI] The GUI icon '{0}' threw while being drawn and will be " +
                    "skipped from now on: {1}",
                    _iconName, e);
            }
        }

        private void DrawIconUnguarded(ICoreClientAPI api, Context ctx)
        {
            // An unknown name draws nothing and reports nothing - the icon is simply absent and
            // there is no error anywhere to find. Saying so once is the difference between a
            // typo that takes a minute and one that takes an evening.
            if (!GuiIcons.Exists(api, _iconName) && WarnedIcons.Add(_iconName!))
            {
                api.Logger.Warning(
                    "[ModernVintageGUI] There is no GUI icon called '{0}', so nothing will be drawn. " +
                    "The names the game knows are in GuiIcons.Available(api).",
                    _iconName);
            }

            double[] color = { IconColor.R / 255.0, IconColor.G / 255.0, IconColor.B / 255.0, IconColor.A / 255.0 * Opacity };

            api.Gui.Icons.DrawIcon(ctx, _iconName, Position.X, Position.Y, Size.X, Size.Y, color);
        }

        private void DrawAsset(ICoreClientAPI api, Context ctx)
        {
            ImageSurface? image = Load(api, _asset!);

            if (image == null)
                return;

            double sourceWidth = image.Width;
            double sourceHeight = image.Height;

            if (sourceWidth <= 0 || sourceHeight <= 0)
                return;

            (double scaleX, double scaleY) = ScaleFor(sourceWidth, sourceHeight);

            double drawWidth = sourceWidth * scaleX;
            double drawHeight = sourceHeight * scaleY;

            double x = Position.X + (Size.X - drawWidth) / 2.0;
            double y = Position.Y + (Size.Y - drawHeight) / 2.0;

            ctx.Save();

            // Clipped to our own box, or a Cover fit would paint over the neighbours.
            ctx.Rectangle(Position.X, Position.Y, Size.X, Size.Y);
            ctx.Clip();

            // The pattern is placed by the transform rather than by SetSource coordinates,
            // because that is the only way to scale it.
            ctx.Translate(x, y);
            ctx.Scale(scaleX, scaleY);
            ctx.SetSource(image, 0, 0);

            if (Opacity >= 1.0)
            {
                ctx.Paint();
            }
            else
            {
                ctx.PaintWithAlpha(Math.Clamp(Opacity, 0, 1));
            }

            ctx.Restore();
        }

        private (double, double) ScaleFor(double sourceWidth, double sourceHeight)
        {
            double x = Size.X / sourceWidth;
            double y = Size.Y / sourceHeight;

            switch (Fit)
            {
                case ImageFit.Stretch:
                    return (x, y);

                case ImageFit.Cover:
                    double cover = Math.Max(x, y);
                    return (cover, cover);

                case ImageFit.None:
                    return (1, 1);

                default:
                    double contain = Math.Min(x, y);
                    return (contain, contain);
            }
        }

        private static ImageSurface? Load(ICoreClientAPI api, AssetLocation asset)
        {
            string key = asset.ToString();

            if (Cache.TryGetValue(key, out ImageSurface? cached))
                return cached;

            try
            {
                ImageSurface loaded = GuiElement.getImageSurfaceFromAsset(api, asset);
                Cache[key] = loaded;
                return loaded;
            }
            catch (Exception e)
            {
                // A missing texture should leave a hole in the dialog, not take the dialog down
                // with it. Cached as a miss so the log does not fill up once per redraw.
                api.Logger.Warning("[ModernVintageGUI] Could not load the image '{0}': {1}", asset, e);
                Cache[key] = null!;
                return null;
            }
        }
        #endregion
    }
}
