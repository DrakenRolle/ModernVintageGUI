using Cairo;
using IS2Mod.ControlTypes;
using IS2Mod.Enums;
using ModernVintageGUI.ControlTypes;
using System;
using System.Collections.Generic;
using Vintagestory.API.Client;

namespace ModernVintageGUI
{
    /// <summary>
    /// The short way to write a dialog.
    ///
    /// Every control here can be built with its own constructor and object initializer, and
    /// nothing in this class does anything those cannot do. What it removes is the repetition:
    /// a label in the dialog font and colour was six named arguments, a fixed size was two
    /// assignments, and a row of three buttons was a container, three constructors and four
    /// <c>Children.Add</c> lines. With this the same row is one expression that reads like the
    /// tree it builds:
    ///
    /// <code>
    /// dialog.Add(UI.Column(
    ///     UI.Heading("Settings"),
    ///     UI.Checkbox("Show tooltips", true, on => config.Tooltips = on),
    ///     UI.Row(
    ///         UI.Button("Save", Save),
    ///         UI.Button("Cancel", dialog.Hide))));
    /// </code>
    ///
    /// The helpers return the concrete control, so anything they do not cover is set in an
    /// object initializer or afterwards - <c>UI.Button("Save", Save).WithSize(160, 40)</c> - and
    /// the fluent extensions in <see cref="UIControlExtensions"/> hand the control back so the
    /// chain does not end.
    /// </summary>
    public static class UI
    {
        /// <summary>The font size every helper here uses unless told otherwise.</summary>
        public const int DefaultFontSize = 16;

        /// <summary>The text colour of a dialog, as an <see cref="ElementColor"/>.</summary>
        public static ElementColor TextColor => new ElementColor(GuiStyle.DialogDefaultTextColor);

        #region Containers
        /// <summary>A container that stacks its children downwards.</summary>
        public static RectangleControl Column(params UIControl[] children)
        {
            return Stack(Orientation.Top, children);
        }

        /// <summary>A container that stacks its children sideways.</summary>
        public static RectangleControl Row(params UIControl[] children)
        {
            return Stack(Orientation.Left, children);
        }

        /// <summary>A container that puts its children on top of each other, all at its origin.</summary>
        public static RectangleControl Overlay(params UIControl[] children)
        {
            return Stack(Orientation.None, children);
        }

        /// <summary>A container stacking along <paramref name="direction"/>, with the children already in it.</summary>
        public static RectangleControl Stack(Orientation direction, params UIControl[] children)
        {
            var container = new RectangleControl(_Name: "", _Margin: 0, _Padding: 0)
            {
                InsideOrientation = direction
            };

            container.AddRange(children);
            return container;
        }

        /// <summary>
        /// A column with room around its content: the padded box a group of controls sits in.
        /// </summary>
        public static RectangleControl Panel(double padding, params UIControl[] children)
        {
            RectangleControl panel = Column(children);
            panel.Padding = padding;
            return panel;
        }

        /// <summary>
        /// A framed, tinted column - what a section of a dialog looks like when it wants to be
        /// read as one thing.
        /// </summary>
        public static RectangleControl Group(params UIControl[] children)
        {
            var group = new RectangleControl(
                borderWidth: 2,
                borderColor: new ElementColor(0.0, 0.0, 0.0, 0.4),
                backgroundColor: new ElementColor(0.0, 0.0, 0.0, 0.15),
                _Padding: 6,
                _Margin: 4)
            {
                InsideOrientation = Orientation.Top
            };

            group.AddRange(children);
            return group;
        }

        /// <summary>
        /// A fixed size window onto its children that scrolls vertically when they do not fit.
        /// For the other axis set <c>EnableHorizontalScrollbar</c> on the result and give it
        /// <c>InsideOrientation = Orientation.Left</c>.
        /// </summary>
        public static RectangleControl Scroll(double width, double height, params UIControl[] children)
        {
            var box = new RectangleControl(
                borderWidth: 2,
                borderColor: new ElementColor(0.0, 0.0, 0.0, 0.4),
                _Padding: 4)
            {
                InsideOrientation = Orientation.Top
            };

            box.WithSize(width, height);
            box.EnableVerticalScrollbar = true;

            box.AddRange(children);
            return box;
        }

        /// <summary>
        /// A split panel with one panel per argument and the argument already in it. Left for
        /// panels side by side, Top for stacked ones.
        /// </summary>
        public static SplitPanelControl Split(Orientation direction, params UIControl[] panels)
        {
            var split = new SplitPanelControl(panelCount: Math.Max(2, panels.Length), direction);

            for (int i = 0; i < panels.Length && i < split.PanelCount; i++)
            {
                if (panels[i] != null)
                {
                    split.Panels[i].Children.Add(panels[i]);
                }
            }

            return split;
        }

        /// <summary>Tabs, one per pair, with the content already on its page.</summary>
        public static TabsControl Tabs(params (string Caption, UIControl Content)[] pages)
        {
            var tabs = new TabsControl();

            foreach ((string caption, UIControl content) in pages)
            {
                tabs.AddTab(caption, content);
            }

            return tabs;
        }

        /// <summary>
        /// Empty space of a fixed size. The one way to leave a gap that is not a margin - between
        /// two groups, or to push a button to the far end of a row.
        /// </summary>
        public static RectangleControl Spacer(double width, double height)
        {
            return new RectangleControl(borderWidth: 0, _Margin: 0, _Padding: 0).WithSize(width, height);
        }
        #endregion

        #region Text
        /// <summary>A label in the dialog's font and text colour.</summary>
        public static TextLabelControl Label(string text, int fontSize = DefaultFontSize)
        {
            return new TextLabelControl(
                text: text,
                fontName: GuiStyle.StandardFontName,
                fontSize: fontSize,
                textColor: TextColor,
                orientation: TextOrientation.MiddleLeft)
            {
                Margin = 2
            };
        }

        /// <summary>
        /// A label that wraps at a width, for a sentence or two of explanation. The height comes
        /// from the text.
        /// </summary>
        public static TextLabelControl Paragraph(string text, double width, int fontSize = DefaultFontSize)
        {
            var label = new TextLabelControl(
                text: text,
                fontName: GuiStyle.StandardFontName,
                fontSize: fontSize,
                textColor: TextColor,
                orientation: TextOrientation.TopLeft,
                wordWrap: true,
                lineHeight: fontSize + 4)
            {
                Margin = 2,
                Orientation = Orientation.Left
            };

            // The width is the wrap width; the label stays auto sizing so the height is whatever
            // the wrapped text needs.
            label.Size = new PointD(width, 0);
            return label;
        }

        /// <summary>The small caption over a group of controls, the way the showcase labels its sections.</summary>
        public static TextLabelControl Heading(string text)
        {
            return new TextLabelControl(
                text: text,
                fontName: GuiStyle.StandardFontName,
                fontSize: (int)GuiStyle.SmallFontSize,
                textColor: TextColor,
                orientation: TextOrientation.MiddleLeft,
                _Name: "heading_" + text)
            {
                Margin = 4
            };
        }

        /// <summary>A larger label for the name of a screen or a section.</summary>
        public static TextLabelControl Title(string text)
        {
            return new TextLabelControl(
                text: text,
                fontName: GuiStyle.StandardFontName,
                fontSize: 22,
                fontWeight: FontWeight.Bold,
                textColor: TextColor,
                orientation: TextOrientation.MiddleLeft)
            {
                Margin = 4
            };
        }
        #endregion

        #region Controls
        /// <summary>A button with a caption and, optionally, what it does and an icon.</summary>
        public static ButtonControl Button(string text, Action? onClick = null, string? iconName = null)
        {
            var button = new ButtonControl
            {
                Text = text,
                IconName = iconName
            };

            if (onClick != null)
            {
                button.Clicked += (sender, e) => onClick();
            }

            return button;
        }

        /// <summary>A checkbox with a caption, its starting state and what to do when it flips.</summary>
        public static CheckboxControl Checkbox(string text, bool isChecked = false, Action<bool>? onChanged = null)
        {
            var checkbox = new CheckboxControl(text, isChecked);

            if (onChanged != null)
            {
                checkbox.CheckedChanged += (sender, on) => onChanged(on);
            }

            return checkbox;
        }

        /// <summary>A text field with a placeholder, and what to do when Enter is pressed in it.</summary>
        public static TextInputControl TextBox(string placeholder = "", Action<string>? onEnter = null)
        {
            var input = new TextInputControl
            {
                PlaceholderText = placeholder
            };

            if (onEnter != null)
            {
                input.EnterPressed += (sender, text) => onEnter(text);
            }

            return input;
        }

        /// <summary>A progress bar at a value between 0 and 1, with an optional caption over it.</summary>
        public static ProgressBarControl Progress(double value, string? text = null)
        {
            return new ProgressBarControl
            {
                Value = value,
                Text = text ?? ""
            };
        }

        /// <summary>
        /// A dropdown of plain captions. The value of each entry is its caption, and the first
        /// one is picked to begin with.
        /// </summary>
        public static DropdownControl Dropdown(params string[] items)
        {
            return Dropdown(null, items);
        }

        /// <summary>The same, telling <paramref name="onSelected"/> the caption that was picked.</summary>
        public static DropdownControl Dropdown(Action<string>? onSelected, params string[] items)
        {
            var dropdown = new DropdownControl();
            var entries = new List<DropdownItem>(items.Length);

            foreach (string item in items)
            {
                entries.Add(new DropdownItem(item, value: item));
            }

            dropdown.SetItems(entries);

            if (entries.Count > 0)
            {
                dropdown.Select(0);
            }

            if (onSelected != null)
            {
                dropdown.SelectionChanged += (sender, e) => onSelected(e.Item?.Text ?? "");
            }

            return dropdown;
        }

        /// <summary>One of the game's icons at a size, drawn in the dialog text colour.</summary>
        public static ImageControl Icon(string iconName, double size = 24)
        {
            return new ImageControl(_Size: new PointD(size, size))
            {
                IconName = iconName
            };
        }
        #endregion
    }

    /// <summary>
    /// Fluent helpers on every control. Each one returns what it was called on, typed as it was
    /// passed in, so a control can be configured and put into its parent in one expression.
    /// </summary>
    public static class UIControlExtensions
    {
        /// <summary>
        /// Adds a child and hands it back - so the child can be kept in a variable in the same
        /// line it is added, which <c>Children.Add</c> cannot do because it returns nothing.
        ///
        /// <code>
        /// var save = column.Add(UI.Button("Save"));
        /// </code>
        /// </summary>
        public static T Add<T>(this UIControl parent, T child) where T : UIControl
        {
            if (parent == null)
                throw new ArgumentNullException(nameof(parent));

            if (child != null)
            {
                parent.Children.Add(child);
            }

            return child!;
        }

        /// <summary>Adds several children in order and hands the parent back.</summary>
        public static TParent AddRange<TParent>(this TParent parent, params UIControl[] children) where TParent : UIControl
        {
            if (parent == null)
                throw new ArgumentNullException(nameof(parent));

            if (children == null)
                return parent;

            foreach (UIControl child in children)
            {
                if (child != null)
                {
                    parent.Children.Add(child);
                }
            }

            return parent;
        }

        /// <summary>The same, from any sequence.</summary>
        public static TParent AddRange<TParent>(this TParent parent, IEnumerable<UIControl> children) where TParent : UIControl
        {
            if (parent == null)
                throw new ArgumentNullException(nameof(parent));

            if (children == null)
                return parent;

            foreach (UIControl child in children)
            {
                if (child != null)
                {
                    parent.Children.Add(child);
                }
            }

            return parent;
        }

        /// <summary>
        /// A fixed size in author units. This is the pair <c>Size = ...; IsAutoSize = false;</c>
        /// that every fixed size control needs and that is easy to write half of.
        /// </summary>
        public static T WithSize<T>(this T control, double width, double height) where T : UIControl
        {
            control.Size = new PointD(width, height);
            control.IsAutoSize = false;
            return control;
        }

        /// <summary>Back to sizing from the content.</summary>
        public static T AutoSized<T>(this T control) where T : UIControl
        {
            control.IsAutoSize = true;
            return control;
        }

        public static T WithName<T>(this T control, string name) where T : UIControl
        {
            control.Name = name;
            return control;
        }

        public static T WithMargin<T>(this T control, double margin) where T : UIControl
        {
            control.Margin = margin;
            return control;
        }

        public static T WithPadding<T>(this T control, double padding) where T : UIControl
        {
            control.Padding = padding;
            return control;
        }

        /// <summary>Where the control sits across its parent's stacking direction.</summary>
        public static T Aligned<T>(this T control, Orientation orientation) where T : UIControl
        {
            control.Orientation = orientation;
            return control;
        }

        /// <summary>An upper limit for an auto sizing control, in author units. Zero means no limit on that axis.</summary>
        public static T WithMaxSize<T>(this T control, double width, double height) where T : UIControl
        {
            control.MaxSize = new PointD(width, height);
            return control;
        }

        public static T Enabled<T>(this T control, bool enabled) where T : UIControl
        {
            control.IsEnabled = enabled;
            return control;
        }

        public static T Visible<T>(this T control, bool visible) where T : UIControl
        {
            control.IsVisible = visible;
            return control;
        }

        /// <summary>Cut what the children draw at the edge of this control.</summary>
        public static T Clipping<T>(this T control, bool clips = true) where T : UIControl
        {
            control.ClipsChildren = clips;
            return control;
        }

        /// <summary>Subscribes to <see cref="UIControl.Clicked"/> without the event arguments.</summary>
        public static T OnClick<T>(this T control, Action handler) where T : UIControl
        {
            if (handler != null)
            {
                control.Clicked += (sender, e) => handler();
            }

            return control;
        }
    }
}
