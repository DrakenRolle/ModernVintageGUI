using Cairo;
using IS2Mod.ControlTypes;
using IS2Mod.Enums;
using ModernVintageGUI.ControlTypes;
using System;
using System.Collections.Generic;
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

            var save = new ButtonControl(_Name: "saveButton")
            {
                Text = "Save",
                IconName = GuiIcons.Import,
                IsEnabled = false
            };

            void Touched()
            {
                dirty = true;
                save.IsEnabled = true;
            }

            var volume = new ProgressBarControl(_Name: "volume")
            {
                Value = settings.Volume,
                Text = Percent(settings.Volume),
                Size = new PointD(150, 26),
                IsAutoSize = false
            };

            void SetVolume(double value)
            {
                settings.Volume = Math.Clamp(value, 0, 1);
                volume.Value = settings.Volume;
                volume.Text = Percent(settings.Volume);
                Touched();
            }

            // Kept in a variable so the starting choice can be set after the handler is wired
            // without the form counting that as a change.
            var difficulty = new DropdownControl(_Name: "difficulty");

            difficulty.SetItems(new List<DropdownItem>
            {
                new DropdownItem("Peaceful", value: "Peaceful"),
                new DropdownItem("Normal", value: "Normal"),
                new DropdownItem("Hard", value: "Hard")
            });

            difficulty.Select(0);
            difficulty.SelectionChanged += (sender, e) =>
            {
                settings.Difficulty = e.Item?.Text ?? "";
                Touched();
            };

            save.Clicked += (sender, e) =>
            {
                dirty = false;
                save.IsEnabled = false;

                capi?.ShowChatMessage(
                    $"Settings saved: tooltips={settings.ShowTooltips}, sounds={settings.PlaySounds}, " +
                    $"largeText={settings.LargeText}, difficulty={settings.Difficulty}, " +
                    $"name='{settings.PlayerName}', volume={Percent(settings.Volume)}");
            };

            var column = new RectangleControl(_Name: "settings", _Margin: 0, _Padding: 0)
            {
                InsideOrientation = Orientation.Top
            };

            column.Children.Add(new TextLabelControl(
                text: "Settings",
                fontName: GuiStyle.StandardFontName,
                fontSize: 22,
                fontWeight: FontWeight.Bold,
                textColor: new ElementColor(GuiStyle.DialogDefaultTextColor),
                orientation: TextOrientation.MiddleLeft)
            {
                Margin = 4
            });

            // --- Display
            RectangleControl display = Group("display");
            display.Children.Add(Heading("Display"));

            var tooltips = new CheckboxControl("Show tooltips", isChecked: settings.ShowTooltips, _Name: "tooltips");
            tooltips.CheckedChanged += (sender, on) => { settings.ShowTooltips = on; Touched(); };
            display.Children.Add(tooltips);

            var largeText = new CheckboxControl("Large text", isChecked: settings.LargeText, _Name: "largeText");
            largeText.CheckedChanged += (sender, on) => { settings.LargeText = on; Touched(); };
            display.Children.Add(largeText);

            column.Children.Add(display);

            // --- Sound
            RectangleControl sound = Group("sound");
            sound.Children.Add(Heading("Sound"));

            var sounds = new CheckboxControl("Play sounds", isChecked: settings.PlaySounds, _Name: "sounds");
            sounds.CheckedChanged += (sender, on) => { settings.PlaySounds = on; Touched(); };
            sound.Children.Add(sounds);

            var volumeRow = new RectangleControl(_Name: "volumeRow", _Margin: 0, _Padding: 0)
            {
                InsideOrientation = Orientation.Left
            };

            TextLabelControl volumeLabel = Label("Volume");
            volumeLabel.Size = new PointD(70, 26);
            volumeLabel.IsAutoSize = false;
            volumeRow.Children.Add(volumeLabel);

            var volumeDown = new ButtonControl(_Name: "volumeDown")
            {
                Text = "-",
                Size = new PointD(32, 26),
                IsAutoSize = false,

                // A glyph button meant to stay small: the caption shrinks to the box rather
                // than the box growing to the button font.
                TextAutoSize = true
            };

            volumeDown.Clicked += (sender, e) => SetVolume(settings.Volume - 0.1);
            volumeRow.Children.Add(volumeDown);

            volumeRow.Children.Add(volume);

            var volumeUp = new ButtonControl(_Name: "volumeUp")
            {
                Text = "+",
                Size = new PointD(32, 26),
                IsAutoSize = false,
                TextAutoSize = true
            };

            volumeUp.Clicked += (sender, e) => SetVolume(settings.Volume + 0.1);
            volumeRow.Children.Add(volumeUp);

            sound.Children.Add(volumeRow);
            column.Children.Add(sound);

            // --- Game
            RectangleControl game = Group("game");
            game.Children.Add(Heading("Game"));

            var difficultyRow = new RectangleControl(_Name: "difficultyRow", _Margin: 0, _Padding: 0)
            {
                InsideOrientation = Orientation.Left
            };

            TextLabelControl difficultyLabel = Label("Difficulty");
            difficultyLabel.Size = new PointD(90, 30);
            difficultyLabel.IsAutoSize = false;
            difficultyRow.Children.Add(difficultyLabel);
            difficultyRow.Children.Add(difficulty);
            game.Children.Add(difficultyRow);

            var nameRow = new RectangleControl(_Name: "nameRow", _Margin: 0, _Padding: 0)
            {
                InsideOrientation = Orientation.Left
            };

            TextLabelControl nameLabel = Label("Your name");
            nameLabel.Size = new PointD(90, 30);
            nameLabel.IsAutoSize = false;
            nameRow.Children.Add(nameLabel);

            var playerName = new TextInputControl(_Name: "playerName")
            {
                PlaceholderText = "Type a name and press Enter"
            };

            playerName.EnterPressed += (sender, text) => { settings.PlayerName = text; Touched(); };
            nameRow.Children.Add(playerName);
            game.Children.Add(nameRow);

            column.Children.Add(game);

            // --- Cancel and Save, at the right
            var buttons = new RectangleControl(_Name: "buttons", _Margin: 0, _Padding: 0)
            {
                InsideOrientation = Orientation.Left,
                Orientation = Orientation.Right
            };

            var cancel = new ButtonControl(_Name: "cancelButton")
            {
                Text = "Cancel"
            };

            cancel.Clicked += (sender, e) =>
                capi?.ShowChatMessage(dirty ? "Settings: changes dropped" : "Settings: nothing to drop");

            buttons.Children.Add(cancel);
            buttons.Children.Add(save);
            column.Children.Add(buttons);

            parent.Children.Add(column);

            // The dropdown started on its first entry; the form starts on Normal. Picking it
            // runs the handler above, which marks the form dirty - undone right after, so the
            // screen opens with Save greyed out until the player changes something.
            difficulty.SelectByValue(settings.Difficulty);

            dirty = false;
            save.IsEnabled = false;
        }

        private static string Percent(double value) => (int)Math.Round(value * 100) + "%";

        /// <summary>A framed, tinted column: one section of the form.</summary>
        private static RectangleControl Group(string name)
        {
            return new RectangleControl(
                borderWidth: 2,
                borderColor: new ElementColor(0.0, 0.0, 0.0, 0.4),
                backgroundColor: new ElementColor(0.0, 0.0, 0.0, 0.15),
                _Name: name,
                _Margin: 4,
                _Padding: 6)
            {
                InsideOrientation = Orientation.Top
            };
        }

        private static TextLabelControl Heading(string text)
        {
            return new TextLabelControl(
                text: text,
                fontName: GuiStyle.StandardFontName,
                fontSize: (int)GuiStyle.SmallFontSize,
                textColor: new ElementColor(GuiStyle.DialogDefaultTextColor),
                orientation: TextOrientation.MiddleLeft,
                _Name: "heading_" + text)
            {
                Margin = 4
            };
        }

        private static TextLabelControl Label(string text)
        {
            return new TextLabelControl(
                text: text,
                fontName: GuiStyle.StandardFontName,
                fontSize: 16,
                textColor: new ElementColor(GuiStyle.DialogDefaultTextColor),
                orientation: TextOrientation.MiddleLeft)
            {
                Margin = 2
            };
        }
    }
}
