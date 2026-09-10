using Cairo;
using IS2Mod.ControlTypes;
using IS2Mod.Enums;
using Vintagestory.API.Client;

namespace ModernVintageGUI.Samples
{
    /// <summary>
    /// Complexity 1: a question with two answers.
    ///
    /// The buttons sit at the right because their row is aligned right across the column, and
    /// the text is a fixed size label that wraps, so the dialog does not run as wide as the
    /// sentence.
    /// </summary>
    public static class ConfirmSample
    {
        public const string Id = "confirm";

        public static void Build(UIControl parent, ICoreClientAPI? capi)
        {
            parent.InsideOrientation = Orientation.Top;

            var column = new RectangleControl(_Name: "confirm", _Margin: 0, _Padding: 0)
            {
                InsideOrientation = Orientation.Top
            };

            var title = new TextLabelControl(
                text: "Discard the changes?",
                fontName: GuiStyle.StandardFontName,
                fontSize: 22,
                fontWeight: FontWeight.Bold,
                textColor: new ElementColor(GuiStyle.DialogDefaultTextColor),
                orientation: TextOrientation.MiddleLeft)
            {
                Margin = 4
            };

            var text = new TextLabelControl(
                text: "The recipe was edited and not saved. Closing now throws the edits away; " +
                      "there is no undo for this.",
                fontName: GuiStyle.StandardFontName,
                fontSize: 16,
                textColor: new ElementColor(GuiStyle.DialogDefaultTextColor),
                orientation: TextOrientation.TopLeft,
                wordWrap: true,
                lineHeight: 20)
            {
                Size = new PointD(320, 60),
                IsAutoSize = false,
                Margin = 2
            };

            var gap = new RectangleControl(borderWidth: 0, _Name: "gap", _Margin: 0, _Padding: 0)
            {
                Size = new PointD(1, 8),
                IsAutoSize = false
            };

            var buttons = new RectangleControl(_Name: "buttons", _Margin: 0, _Padding: 0)
            {
                InsideOrientation = Orientation.Left,
                Orientation = Orientation.Right
            };

            var keep = new ButtonControl(_Name: "keepButton")
            {
                Text = "Keep editing"
            };

            keep.Clicked += (sender, e) => capi?.ShowChatMessage("Confirm: keep editing");

            var discard = new ButtonControl(_Name: "discardButton")
            {
                Text = "Discard",
                IconName = GuiIcons.Eraser
            };

            discard.Clicked += (sender, e) => capi?.ShowChatMessage("Confirm: discarded");

            buttons.Children.Add(keep);
            buttons.Children.Add(discard);

            column.Children.Add(title);
            column.Children.Add(text);
            column.Children.Add(gap);
            column.Children.Add(buttons);

            parent.Children.Add(column);
        }
    }
}
