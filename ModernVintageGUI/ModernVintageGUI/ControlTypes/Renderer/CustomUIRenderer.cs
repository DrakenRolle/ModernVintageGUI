using IS2Mod.ControlTypes.Custom;
using Vintagestory.API.Client;

namespace IS2Mod.ControlTypes.Renderer
{
    /// <summary>
    /// Renders a custom dialog element to the screen during the Ortho rendering stage.
    /// </summary>
    public class CustomUIRenderer : IRenderer
    {
        #region Properties
        /// <summary>
        /// Controls render order. Lower values render earlier.
        /// </summary>
        public double RenderOrder => 0.5;

        /// <summary>
        /// Render distance. 999 means always render regardless of distance.
        /// </summary>
        public int RenderRange => 999;
        #endregion

        #region Private Fields
        private readonly CustomDialogElement _dialog;
        private readonly ICoreClientAPI _api;
        #endregion

        #region Constructor
        public CustomUIRenderer(ICoreClientAPI capi, CustomDialogElement dialogElement)
        {
            _api = capi;
            _dialog = dialogElement;
        }
        #endregion

        #region Rendering
        public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
        {
            // Only render if dialog is visible and has a valid texture
            if (!ShouldRender())
                return;

            RenderDialogTexture();
        }

        private bool ShouldRender()
        {
            return _dialog.IsVisible &&
                   _dialog.StaticElementsTexture != null &&
                   _dialog.StaticElementsTexture.TextureId != 0;
        }

        private void RenderDialogTexture()
        {
            _api.Render.RenderTexture(
                _dialog.StaticElementsTexture.TextureId,
                _dialog.Position.X,
                _dialog.Position.Y,
                _dialog.Size.X,
                _dialog.Size.Y
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
