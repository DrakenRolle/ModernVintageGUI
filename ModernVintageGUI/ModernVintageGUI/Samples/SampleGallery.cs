using Cairo;
using IS2Mod.ControlTypes;
using IS2Mod.ControlTypes.Custom;
using IS2Mod.Enums;
using ModernVintageGUI.ControlTypes;
using System;
using System.Collections.Generic;
using Vintagestory.API.Client;

namespace ModernVintageGUI.Samples
{
    /// <summary>
    /// One example screen: what it is called, what it shows, and how to build it into a parent.
    ///
    /// The builder takes a plain <see cref="UIControl"/> rather than a dialog for the same
    /// reason <see cref="ControlShowcase"/> does: the layout harness has no dialog and no game,
    /// and the same tree has to lay out there so the picture and the checks cover it.
    /// </summary>
    public sealed class SampleWindow
    {
        /// <summary>The short name a command or a hotkey refers to the sample by.</summary>
        public string Id { get; }

        /// <summary>The caption of the window.</summary>
        public string Title { get; }

        /// <summary>A sentence on what the sample exercises.</summary>
        public string Description { get; }

        /// <summary>One to five, from a confirm box to a dashboard.</summary>
        public int Complexity { get; }

        /// <summary>Fills a parent with the sample. The client API is null in the harness.</summary>
        public Action<UIControl, ICoreClientAPI?> Build { get; }

        public SampleWindow(string id, string title, string description, int complexity, Action<UIControl, ICoreClientAPI?> build)
        {
            Id = id;
            Title = title;
            Description = description;
            Complexity = complexity;
            Build = build;
        }
    }

    /// <summary>
    /// The example windows, from a two button confirm box to a dashboard of nested split panels,
    /// and the gallery that opens them.
    ///
    /// They are test UIs in the plain sense: each one is a different shape of screen a mod
    /// might actually want, built with the same controls a mod would use, so a change to the
    /// framework can be judged against five screens rather than one showcase of everything. In
    /// the game the gallery is on a hotkey; in the layout harness every sample is a scenario.
    /// </summary>
    public static class SampleGallery
    {
        /// <summary>The prefix every sample dialog's name starts with.</summary>
        public const string DialogNamePrefix = "mvguiSample_";

        /// <summary>The name of the gallery dialog itself.</summary>
        public const string GalleryDialogName = "mvguiSampleGallery";

        /// <summary>Every sample, simplest first.</summary>
        public static readonly IReadOnlyList<SampleWindow> All = new[]
        {
            new SampleWindow(
                ConfirmSample.Id,
                "Confirm",
                "A question and two buttons - the smallest screen there is.",
                complexity: 1,
                ConfirmSample.Build),

            new SampleWindow(
                SettingsSample.Id,
                "Settings",
                "A form: checkboxes, a dropdown, a text field, a value with buttons, Save and Cancel.",
                complexity: 2,
                SettingsSample.Build),

            new SampleWindow(
                ExplorerSample.Id,
                "Explorer",
                "Tree, list and details in a three way split panel, with a log below that " +
                "the whole thing is split against - drag the bars.",
                complexity: 3,
                ExplorerSample.Build),

            new SampleWindow(
                WorkshopSample.Id,
                "Workshop",
                "A crafting station: input slots, a recipe picker with a 3D preview, progress, " +
                "and tabs for recipes and a log.",
                complexity: 4,
                WorkshopSample.Build),

            new SampleWindow(
                DashboardSample.Id,
                "Dashboard",
                "Nested split panels both ways, live stats, a painted map, machine tree, tabs " +
                "and toggles that hide and disable parts of the screen while it is open.",
                complexity: 5,
                DashboardSample.Build)
        };

        /// <summary>The sample with this id, or null. Case does not matter.</summary>
        public static SampleWindow? Find(string? id)
        {
            if (string.IsNullOrWhiteSpace(id))
                return null;

            foreach (SampleWindow sample in All)
            {
                if (string.Equals(sample.Id, id, StringComparison.OrdinalIgnoreCase))
                    return sample;
            }

            return null;
        }

        /// <summary>
        /// Fills a parent with a button per sample, plus a line on each. <paramref name="open"/>
        /// is what a button does; null in the harness, where the buttons are only laid out.
        /// </summary>
        public static void BuildGallery(UIControl parent, Action<SampleWindow>? open)
        {
            parent.InsideOrientation = Orientation.Top;
            parent.Padding = 10;

            parent.Add(UI.Title("Sample windows"));
            parent.Add(UI.Label("Five screens of rising complexity. Each opens as its own dialog.", 14));

            foreach (SampleWindow sample in All)
            {
                SampleWindow captured = sample;

                ButtonControl openButton = UI.Button(
                    captured.Complexity + "  " + captured.Title,
                    open == null ? null : () => open(captured));
                openButton.Name = "open_" + captured.Id;
                openButton.Size = new PointD(150, 34);
                openButton.IsAutoSize = false;

                TextLabelControl description = UI.Paragraph(captured.Description, 330, 14);
                description.Margin = 6;

                parent.Add(UI.Row(openButton, description));
            }
        }

        /// <summary>
        /// A dialog with a title bar and the sample under it, ready to be shown. Each sample
        /// gets its own dialog, so several can be open at once and compared.
        /// </summary>
        public static CustomDialogElement CreateDialog(ICoreClientAPI capi, SampleWindow sample)
        {
            var dialog = new CustomDialogElement(capi, DialogNamePrefix + sample.Id, sample.Title)
            {
                // The bar has to reach the edges, so the dialog itself has no padding and the
                // content below the bar brings its own.
                Padding = 0
            };

            dialog.Add(new TitleBarControl(sample.Title) { Name = "titleBar" });

            RectangleControl content = UI.Panel(10);
            content.Name = "content";
            dialog.Add(content);

            sample.Build(content, capi);

            return dialog;
        }

        /// <summary>
        /// The gallery as a dialog. Opening a sample from it is <paramref name="open"/>'s job,
        /// because the dialogs it opens have to be owned by whoever disposes them.
        /// </summary>
        public static CustomDialogElement CreateGalleryDialog(ICoreClientAPI capi, Action<SampleWindow> open)
        {
            var dialog = new CustomDialogElement(capi, GalleryDialogName, "Sample windows")
            {
                Padding = 0
            };

            dialog.Add(new TitleBarControl("Sample windows") { Name = "titleBar" });

            RectangleControl content = UI.Column();
            content.Name = "content";
            dialog.Add(content);

            BuildGallery(content, open);

            return dialog;
        }
    }
}
