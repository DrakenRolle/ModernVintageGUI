using Cairo;
using IS2Mod.ControlTypes;
using IS2Mod.Enums;
using ModernVintageGUI.ControlTypes;
using Vintagestory.API.Client;

namespace ModernVintageGUI.Samples
{
    /// <summary>
    /// Complexity 1: a question with two answers.
    ///
    /// The tree is one expression, which is what the <see cref="UI"/> helpers are for; the two
    /// buttons are made first because they carry a name, and a name is a property. They sit at
    /// the right because the row is aligned right across its column, and the paragraph wraps at
    /// a width instead of running the dialog wide.
    /// </summary>
    public static class ConfirmSample
    {
        public const string Id = "confirm";

        public static void Build(UIControl parent, ICoreClientAPI? capi)
        {
            parent.InsideOrientation = Orientation.Top;

            ButtonControl keep = UI.Button("Keep editing", () => capi?.ShowChatMessage("Confirm: keep editing"));
            keep.Name = "keepButton";

            ButtonControl discard = UI.Button("Discard", () => capi?.ShowChatMessage("Confirm: discarded"), GuiIcons.Eraser);
            discard.Name = "discardButton";

            TextLabelControl text = UI.Paragraph(
                "The recipe was edited and not saved. Closing now throws the edits away; " +
                "there is no undo for this.",
                320);
            text.Size = new PointD(320, 60);
            text.IsAutoSize = false;

            parent.Add(UI.Column(
                UI.Title("Discard the changes?"),
                text,
                UI.Spacer(1, 8),
                UI.Row(keep, discard).Aligned(Orientation.Right)));
        }
    }
}
