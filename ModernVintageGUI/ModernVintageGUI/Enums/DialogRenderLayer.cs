namespace IS2Mod.Enums
{
    /// <summary>
    /// Which band of the Ortho render stage a dialog draws in. Higher draws later, i.e. on top.
    ///
    /// For reference, vanilla registers its own GUI at 1.0 and the crosshair at 1.02, so both
    /// bands stay below that on purpose: vanilla dialogs are meant to cover ours, which matches
    /// the input rule in UIManager that yields to an open vanilla dialog.
    /// </summary>
    public enum DialogRenderLayer
    {
        /// <summary>Ordinary dialogs.</summary>
        Normal,

        /// <summary>
        /// Popups that have to cover ordinary dialogs - context menus, dropdowns, tooltips.
        /// </summary>
        Overlay
    }
}
