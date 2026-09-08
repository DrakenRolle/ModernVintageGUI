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

            ButtonControl save = UI.Button("Save", null, GuiIcons.Import).WithName("saveButton").Enabled(false);

            void Touched()
            {
                dirty = true;
                save.IsEnabled = true;
            }

            ProgressBarControl volume = UI.Progress(settings.Volume, Percent(settings.Volume)).WithName("volume");

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
                    "Peaceful", "Normal", "Hard")
                .WithName("difficulty");

            save.Clicked += (sender, e) =>
            {
                dirty = false;
                save.IsEnabled = false;

                capi?.ShowChatMessage(
                    $"Settings saved: tooltips={settings.ShowTooltips}, sounds={settings.PlaySounds}, " +
                    $"largeText={settings.LargeText}, difficulty={settings.Difficulty}, " +
                    $"name='{settings.PlayerName}', volume={Percent(settings.Volume)}");
            };

            parent.Add(UI.Column(
                UI.Title("Settings"),

                UI.Group(
                    UI.Heading("Display"),
                    UI.Checkbox("Show tooltips", settings.ShowTooltips, on => { settings.ShowTooltips = on; Touched(); })
                        .WithName("tooltips"),
                    UI.Checkbox("Large text", settings.LargeText, on => { settings.LargeText = on; Touched(); })
                        .WithName("largeText")),

                UI.Group(
                    UI.Heading("Sound"),
                    UI.Checkbox("Play sounds", settings.PlaySounds, on => { settings.PlaySounds = on; Touched(); })
                        .WithName("sounds"),
                    UI.Row(
                        UI.Label("Volume").WithSize(70, 26),
                        UI.Button("-", () => SetVolume(settings.Volume - 0.1)).WithSize(32, 26).WithName("volumeDown"),
                        volume.WithSize(150, 26),
                        UI.Button("+", () => SetVolume(settings.Volume + 0.1)).WithSize(32, 26).WithName("volumeUp"))),

                UI.Group(
                    UI.Heading("Game"),
                    UI.Row(
                        UI.Label("Difficulty").WithSize(90, 30),
                        difficulty),
                    UI.Row(
                        UI.Label("Your name").WithSize(90, 30),
                        UI.TextBox("Type a name and press Enter", text => { settings.PlayerName = text; Touched(); })
                            .WithName("playerName"))),

                UI.Row(
                        UI.Button("Cancel", () => capi?.ShowChatMessage(dirty ? "Settings: changes dropped" : "Settings: nothing to drop"))
                            .WithName("cancelButton"),
                        save)
                    .Aligned(Orientation.Right)));

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
