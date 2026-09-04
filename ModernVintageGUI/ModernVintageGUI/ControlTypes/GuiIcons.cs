using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace IS2Mod.ControlTypes
{
    /// <summary>
    /// Icons by name - the game's own, and any a mod adds.
    ///
    /// Adding one is a line, because the game already has the machinery for it:
    /// <c>IconUtil.CustomIcons</c> is a public dictionary of name to renderer, and
    /// <c>IconUtil.SvgIconSource</c> turns an SVG asset into such a renderer. So a mod ships an
    /// SVG and registers it, and from then on the name works everywhere a name works - a button,
    /// an <see cref="ModernVintageGUI.ControlTypes.ImageControl"/>, a dropdown entry:
    ///
    /// <code>
    /// GuiIcons.Register(capi, "gear", new AssetLocation("mymod:textures/icons/gear.svg"));
    /// button.IconName = "gear";
    /// </code>
    ///
    /// The names below are shortcuts for the ones the game ships, nothing more. They are not a
    /// list of what is allowed: <see cref="Exists"/> asks the running game, so an icon added by
    /// a later game version or by another mod counts as real without anything here changing.
    /// </summary>
    public static class GuiIcons
    {
        #region The game's own, as shortcuts
        public const string Airbrush = "airbrush";
        public const string Apple = "apple";
        public const string Basket = "basket";
        public const string Belt = "belt";
        public const string Boots = "boots";
        public const string Bracers = "bracers";
        public const string Brush = "brush";
        public const string Cape = "cape";
        public const string Cursor = "cursor";
        public const string Dice = "dice";
        public const string Eraser = "eraser";
        public const string Erode = "erode";
        public const string FloodFill = "floodfill";
        public const string Gloves = "gloves";
        public const string GrowShrink = "growshrink";
        public const string Handheld = "handheld";
        public const string Hat = "hat";
        public const string Health = "health";
        public const string Import = "import";
        public const string Lake = "lake";
        public const string Left = "left";
        public const string Line = "line";
        public const string Mask = "mask";
        public const string Medal = "medal";
        public const string MenuIcon = "menuicon";
        public const string Necklace = "necklace";
        public const string None = "none";
        public const string Offhand = "offhand";
        public const string Pullover = "pullover";
        public const string RaiseLower = "raiselower";
        public const string Redo = "redo";
        public const string Repeat = "repeat";
        public const string Right = "right";
        public const string Ring = "ring";
        public const string Select = "select";
        public const string Shirt = "shirt";
        public const string Tree = "tree";
        public const string Trousers = "trousers";
        public const string Undo = "undo";
        #endregion

        #region Registering
        /// <summary>
        /// Registers an SVG from the mod's assets under a name. Overwrites a name that is
        /// already taken, which is what lets a mod replace one of the game's icons on purpose.
        /// </summary>
        public static void Register(ICoreClientAPI capi, string name, AssetLocation svg)
        {
            if (capi == null || string.IsNullOrEmpty(name) || svg == null)
                return;

            capi.Gui.Icons.CustomIcons[name] = capi.Gui.Icons.SvgIconSource(svg);
        }

        /// <summary>The same for an icon drawn by hand rather than loaded from an asset.</summary>
        public static void Register(ICoreClientAPI capi, string name, IconRendererDelegate renderer)
        {
            if (capi == null || string.IsNullOrEmpty(name) || renderer == null)
                return;

            capi.Gui.Icons.CustomIcons[name] = renderer;
        }

        /// <summary>Whether a name has been registered by this mod or any other.</summary>
        public static bool IsCustom(ICoreClientAPI? capi, string? name)
        {
            return name != null && capi?.Gui.Icons.CustomIcons.ContainsKey(name) == true;
        }
        #endregion

        #region Asking the game what exists
        /// <summary>
        /// The built in names, read out of the running game rather than written down here.
        ///
        /// IconUtil keeps one <c>Draw&lt;name&gt;_svg</c> method per icon and decides in a switch
        /// which to call, so there is no list to ask for - but the methods themselves are a list,
        /// and reading them cannot go stale the way a copy would. Should a future version rename
        /// them, this comes back empty and nothing here treats a name as wrong; the only thing
        /// lost is the warning about a typo.
        /// </summary>
        public static IReadOnlyCollection<string> BuiltIn => _builtIn ??= DiscoverBuiltIn();

        private static IReadOnlyCollection<string>? _builtIn;

        private static IReadOnlyCollection<string> DiscoverBuiltIn()
        {
            try
            {
                const string prefix = "Draw";
                const string suffix = "_svg";

                return typeof(IconUtil)
                    .GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                    .Select(method => method.Name)
                    .Where(name => name.StartsWith(prefix, StringComparison.Ordinal)
                                && name.EndsWith(suffix, StringComparison.Ordinal))
                    .Select(name => name.Substring(prefix.Length, name.Length - prefix.Length - suffix.Length))
                    .ToHashSet(StringComparer.Ordinal);
            }
            catch
            {
                // Reflection is a courtesy here, not a dependency - see the remark above.
                return Array.Empty<string>();
            }
        }

        /// <summary>
        /// Every name that will draw something right now: the game's own plus everything
        /// registered. Handy for a dropdown of icons, or for checking a configured name.
        /// </summary>
        public static IEnumerable<string> Available(ICoreClientAPI? capi)
        {
            IEnumerable<string> names = BuiltIn;

            if (capi != null)
            {
                names = names.Concat(capi.Gui.Icons.CustomIcons.Keys);
            }

            return names.Distinct(StringComparer.Ordinal).OrderBy(name => name, StringComparer.Ordinal);
        }

        /// <summary>
        /// Whether this name is likely to draw. Used only to decide whether a missing icon is
        /// worth a warning - it never stops one from being drawn, because being wrong about
        /// that would be worse than the typo it is meant to catch.
        /// </summary>
        public static bool Exists(ICoreClientAPI? capi, string? name)
        {
            if (string.IsNullOrEmpty(name))
                return false;

            if (IsCustom(capi, name))
                return true;

            IReadOnlyCollection<string> builtIn = BuiltIn;

            // Nothing discovered means the check itself is unavailable, not that the name is bad.
            return builtIn.Count == 0 || builtIn.Contains(name);
        }
        #endregion
    }
}
