using IS2Mod.ControlTypes.Custom;
using IS2Mod.Enums;
using System;
using Vintagestory.API.Client;

namespace IS2Mod.ControlTypes.Renderer
{
    /// <summary>
    /// Renders a custom dialog element to the screen during the Ortho rendering stage.
    /// </summary>
    public class CustomUIRenderer : IRenderer
    {
        #region Render order
        // Vanilla registers its whole GUI - dialogs and HUDs - in one renderer at 1.0 and the
        // crosshair at 1.02. So there are two places we can sit: below it, where a vanilla dialog
        // covers us, and above it, where we cover vanilla. Which one a dialog uses depends on
        // whether it has focus, the same rule the game applies to its own windows.
        private const double UnfocusedNormalBase = 0.5;
        private const double UnfocusedOverlayBase = 0.6;
        private const double FocusedNormalBase = 1.1;
        private const double FocusedOverlayBase = 1.2;

        /// <summary>
        /// Spacing between two dialogs of the same layer. Small enough that a session will not
        /// run out of a band, large enough to stay well clear of double rounding.
        /// </summary>
        private const double LayerStep = 0.00001;

        /// <summary>Keeps a band from bleeding into the next one after very many dialogs.</summary>
        private const double LayerWidth = 0.09;

        private static int _normalCount;
        private static int _overlayCount;
        #endregion

        #region Depth
        // Render order alone does not decide what is on top. ClientMain.OrthoMode sets up the GUI
        // with a frustum of 0.4 to 20001 and moves the model to z = -19849, and ScreenManager sets
        // the depth function to Lequal with the depth test on. Larger z is therefore *nearer*, and
        // a fragment drawn later still loses if its depth is behind what is already there.
        //
        // Vanilla stacks its dialogs with GlTranslate(0, 0, ZSize) at ZSize = 150 each, plus the
        // z offsets its elements add on top. IRenderAPI.RenderTexture defaults to z = 50, so
        // without an explicit value we sit below almost all of the vanilla GUI no matter how late
        // we draw.

        /// <summary>The RenderTexture default - low enough that a vanilla dialog covers us.</summary>
        private const float UnfocusedZ = 50f;

        /// <summary>
        /// Clear of anything vanilla stacks up (a handful of dialogs plus element offsets stays
        /// well under 2000) and far from the near plane at about 19848.
        /// </summary>
        private const float FocusedZ = 10000f;

        /// <summary>
        /// Popups sit above the dialog they belong to, focused or not.
        ///
        /// The gap to <see cref="FocusedZ"/> has to clear everything that dialog draws in front
        /// of its own surface, and the tallest of those is the stack size of an item in a slot -
        /// InventoryItemRenderer puts that a hundred in front of the stack, so a menu only a
        /// hundred above the dialog would have the numbers of the grid behind it printed through
        /// it. A thousand leaves the question closed.
        /// </summary>
        private const float OverlayZ = 11000f;

        private float RenderZ()
        {
            if (_dialog.Layer == DialogRenderLayer.Overlay)
                return OverlayZ;

            return _dialog.IsFocused ? FocusedZ : UnfocusedZ;
        }

        /// <summary>
        /// Every dialog gets its own order within its band, increasing with creation.
        ///
        /// This matters because ClientEventManager sorts renderers ascending and inserts a new
        /// one *before* any existing entry with the same order - so equal orders would make the
        /// newest dialog render first, i.e. underneath the older ones, which is the opposite of
        /// how UIManager routes input (topmost = most recently registered).
        /// </summary>
        private static double NextOrder(DialogRenderLayer layer, bool aboveVanilla, int index)
        {
            double layerBase = layer == DialogRenderLayer.Overlay
                ? (aboveVanilla ? FocusedOverlayBase : UnfocusedOverlayBase)
                : (aboveVanilla ? FocusedNormalBase : UnfocusedNormalBase);

            return layerBase + Math.Min(index * LayerStep, LayerWidth);
        }

        /// <summary>One sequence number per dialog, shared by its two renderers.</summary>
        public static int NextSequence(DialogRenderLayer layer)
        {
            return layer == DialogRenderLayer.Overlay ? _overlayCount++ : _normalCount++;
        }
        #endregion

        #region Properties
        /// <summary>
        /// Controls render order. Lower values render earlier. Fixed at construction, because
        /// the game sorts the renderer list once when it is registered and never re-sorts it.
        /// </summary>
        public double RenderOrder { get; }

        /// <summary>
        /// Render distance. 999 means always render regardless of distance.
        /// </summary>
        public int RenderRange => 999;
        #endregion

        #region Private Fields
        private readonly CustomDialogElement _dialog;
        private readonly ICoreClientAPI _api;
        private readonly bool _aboveVanilla;
        #endregion

        #region Constructor
        public CustomUIRenderer(
            ICoreClientAPI capi,
            CustomDialogElement dialogElement,
            DialogRenderLayer layer,
            bool aboveVanilla,
            int sequence)
        {
            _api = capi;
            _dialog = dialogElement;
            _aboveVanilla = aboveVanilla;
            RenderOrder = NextOrder(layer, aboveVanilla, sequence);
        }
        #endregion

        #region Rendering
        public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
        {
            // A dialog has two renderers, one on each side of the vanilla GUI. Exactly one of
            // them draws, decided by focus - popups always draw on the upper side, because a
            // menu a vanilla window could cover would be useless.
            bool wantsAbove = _dialog.Layer == DialogRenderLayer.Overlay || _dialog.IsFocused;
            if (wantsAbove != _aboveVanilla)
                return;

            if (!_dialog.IsVisible)
                return;

            // Rebuild the surface if anything changed since the last frame, and build it for the
            // first time for a dialog that was just shown. Exactly one of the two renderers gets
            // past the check above per frame, so this runs once.
            //
            // Drawing and uploading from inside the render stage is what the game does itself -
            // GuiComposer.Render() checks recomposeOnRender and recomposes right there.
            Diagnostics.UIProfiler.Scope redraw = Diagnostics.UIProfiler.Begin();

            _dialog.EnsureRendered();

            Diagnostics.UIProfiler.End("frame  EnsureRendered (redraw when dirty)", redraw);

            LoadedTexture? texture = _dialog.StaticElementsTexture;

            if (texture == null || texture.TextureId == 0)
                return;

            float z = RenderZ();

            RenderDialogTexture(texture, z);

            // Tell the tree how deep the surface it has to draw in front of was put. Vanilla's
            // own numbers (90 for a stack in a slot, 450 for the one on the cursor) are relative
            // to its dialogs, which stack in steps of 150 from zero - ours sits at 10000 to clear
            // the whole vanilla GUI, so those numbers would land far behind our own background
            // and the item would simply not be visible.
            _dialog.SurfaceRenderZ = z;

            // Anything that cannot live in the Cairo surface - item stacks, above all - goes on
            // top of it, every frame. Same split the vanilla GUI makes.
            Diagnostics.UIProfiler.Scope interactive = Diagnostics.UIProfiler.Begin();

            _dialog.GenerateInteractiveRenderData(_api, deltaTime);

            Diagnostics.UIProfiler.End("frame  interactive pass (item stacks)", interactive);

            Diagnostics.UIProfiler.EndFrame();
        }

        private void RenderDialogTexture(LoadedTexture texture, float z)
        {
            _api.Render.RenderTexture(
                texture.TextureId,
                _dialog.Position.X,
                _dialog.Position.Y,
                _dialog.Size.X,
                _dialog.Size.Y,
                z
            );
        }
        #endregion

        #region Cleanup
        public void Dispose()
        {
            // Cleanup if needed
        }
        #endregion
    }
}
