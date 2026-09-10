using Cairo;
using IS2Mod.ControlTypes;
using IS2Mod.Enums;
using ModernVintageGUI.ControlTypes;
using System;
using Vintagestory.API.Client;

namespace ModernVintageGUI.Samples
{
    /// <summary>
    /// Complexity 2: a settings form.
    ///
    /// Everything a mod's config screen usually has - switches, a choice, a name, a level with
    /// buttons either side of it - and the two things a form has to get right: the controls read
    /// their state from one object and write it back, and Save is disabled until something
    /// changed. The second one is the interesting part in this framework, because assigning
    /// <c>IsEnabled</c> is all it takes to grey the button out on an open dialog.
    /// </summary>
    public static class SettingsSample
    {
        public const string Id = "settings";

        /// <summary>The state the form edits. A mod would have its config class here.</summary>
        private sealed class Settings
        {
            public bool ShowTooltips = true;
            public bool PlaySounds = true;
            public bool LargeText;
            public string Difficulty = "Normal";
            public string PlayerName = "";
            public double Volume = 0.6;
        }

        public static void Build(UIControl parent, ICoreClientAPI? capi)
        {
            parent.InsideOrientation = Orientation.Top;

            var settings = new Settings();
            bool dirty = false;

            ButtonControl save = UI.Button("Save", null, GuiIcons.Import).Enabled(false);
            save.Name = "saveButton";

            void Touched()
            {
                dirty = true;
                save.IsEnabled = true;
            }

            ProgressBarControl volume = UI.Progress(settings.Volume, Percent(settings.Volume));
            volume.Name = "volume";
            volume.Size = new PointD(150, 26);
            volume.IsAutoSize = false;

            void SetVolume(double value)
            {
                settings.Volume = Math.Clamp(value, 0, 1);
                volume.Value = settings.Volume;
                volume.Text = Percent(settings.Volume);
                Touched();
            }

            // Kept in a variable so the starting choice can be set after the handler is wired
            // without the form counting that as a change.
            DropdownControl difficulty = UI.Dropdown(
                pick => { settings.Difficulty = pick; Touched(); },
                "Peaceful", "Normal", "Hard");
            difficulty.Name = "difficulty";

            save.Clicked += (sender, e) =>
            {
                dirty = false;
                save.IsEnabled = false;

                capi?.ShowChatMessage(
                    $"Settings saved: tooltips={settings.ShowTooltips}, sounds={settings.PlaySounds}, " +
                    $"largeText={settings.LargeText}, difficulty={settings.Difficulty}, " +
                    $"name='{settings.PlayerName}', volume={Percent(settings.Volume)}");
            };

            // --- Display
            CheckboxControl tooltips = UI.Checkbox("Show tooltips", settings.ShowTooltips, on => { settings.ShowTooltips = on; Touched(); });
            tooltips.Name = "tooltips";

            CheckboxControl largeText = UI.Checkbox("Large text", settings.LargeText, on => { settings.LargeText = on; Touched(); });
            largeText.Name = "largeText";

            // --- Sound
            CheckboxControl sounds = UI.Checkbox("Play sounds", settings.PlaySounds, on => { settings.PlaySounds = on; Touched(); });
            sounds.Name = "sounds";

            TextLabelControl volumeLabel = UI.Label("Volume");
            volumeLabel.Size = new PointD(70, 26);
            volumeLabel.IsAutoSize = false;

            // Glyph buttons meant to stay small: the caption shrinks to the box rather than the
            // box growing to the button font.
            ButtonControl volumeDown = UI.Button("-", () => SetVolume(settings.Volume - 0.1));
            volumeDown.Name = "volumeDown";
            volumeDown.Size = new PointD(32, 26);
            volumeDown.IsAutoSize = false;
            volumeDown.TextAutoSize = true;

            ButtonControl volumeUp = UI.Button("+", () => SetVolume(settings.Volume + 0.1));
            volumeUp.Name = "volumeUp";
            volumeUp.Size = new PointD(32, 26);
            volumeUp.IsAutoSize = false;
            volumeUp.TextAutoSize = true;

            // --- Game
            TextLabelControl difficultyLabel = UI.Label("Difficulty");
            difficultyLabel.Size = new PointD(90, 30);
            difficultyLabel.IsAutoSize = false;

            TextLabelControl nameLabel = UI.Label("Your name");
            nameLabel.Size = new PointD(90, 30);
            nameLabel.IsAutoSize = false;

            TextInputControl playerName = UI.TextBox("Type a name and press Enter", text => { settings.PlayerName = text; Touched(); });
            playerName.Name = "playerName";

            // --- Cancel and Save, at the right.
            ButtonControl cancel = UI.Button("Cancel", () => capi?.ShowChatMessage(dirty ? "Settings: changes dropped" : "Settings: nothing to drop"));
            cancel.Name = "cancelButton";

            parent.Add(UI.Column(
                UI.Title("Settings"),

                UI.Group(
                    UI.Heading("Display"),
                    tooltips,
                    largeText),

                UI.Group(
                    UI.Heading("Sound"),
                    sounds,
                    UI.Row(volumeLabel, volumeDown, volume, volumeUp)),

                UI.Group(
                    UI.Heading("Game"),
                    UI.Row(difficultyLabel, difficulty),
                    UI.Row(nameLabel, playerName)),

                UI.Row(cancel, save).Aligned(Orientation.Right)));

            // The dropdown started on its first entry; the form starts on Normal. Picking it
            // runs the handler above, which marks the form dirty - undone right after, so the
            // screen opens with Save greyed out until the player changes something.
            difficulty.SelectByValue(settings.Difficulty);

            dirty = false;
            save.IsEnabled = false;
        }

        private static string Percent(double value) => (int)Math.Round(value * 100) + "%";
    }
}
