using ModernVintageGUI.ControlTypes;
using System.Reflection;
using IS2Mod.ControlTypes;

namespace ModernVintageGUI.Designer.Markup
{
    /// <summary>One authorable property of a control, i.e. one attribute in the markup.</summary>
    public sealed class MarkupProperty
    {
        public required string Name { get; init; }
        public required Type Type { get; init; }
        public required PropertyInfo Info { get; init; }

        /// <summary>
        /// The value a freshly built control has. Attributes equal to it are left out when the
        /// document is written, so the markup stays as short as what a person would type.
        /// </summary>
        public object? DefaultValue { get; init; }

        /// <summary>"Layout" for the properties every control shares, "Control" for its own.</summary>
        public required string Group { get; init; }

        public string[] EnumNames =>
            Type.IsEnum ? Enum.GetNames(Type) : Array.Empty<string>();
    }

    /// <summary>One control type as the designer sees it: a tag name, a factory and a property list.</summary>
    public sealed class ControlDescriptor
    {
        public required string Tag { get; init; }
        public required Type ClrType { get; init; }
        public required string Category { get; init; }
        public required string Summary { get; init; }

        /// <summary>Whether authored children go inside this control.</summary>
        public required bool AcceptsChildren { get; init; }

        /// <summary>How many. A tab page holds exactly one control, a container any number.</summary>
        public int MaxChildren { get; init; } = int.MaxValue;

        public required IReadOnlyList<MarkupProperty> Properties { get; init; }

        /// <summary>Builds an instance with every constructor parameter left at its default.</summary>
        public required Func<UIControl> Create { get; init; }

        /// <summary>Attributes the toolbox puts on a freshly dropped control.</summary>
        public IReadOnlyList<KeyValuePair<string, string>> DropDefaults { get; init; } =
            Array.Empty<KeyValuePair<string, string>>();

        public MarkupProperty? Find(string name) =>
            Properties.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// What the designer knows about the framework. Built by reflection over the control
    /// assembly rather than by hand, so a control someone adds to ModernVintageGUI shows up in
    /// the toolbox and the property grid without this file being touched.
    /// </summary>
    public static class ControlCatalog
    {
        /// <summary>The root tag. It is a RectangleControl, named for what it stands for.</summary>
        public const string RootTag = "Dialog";

        /// <summary>A tab page. Not a control - it feeds TabsControl.AddTab.</summary>
        public const string TabTag = "Tab";

        /// <summary>
        /// Layout results and engine plumbing. These are public because the layout and the
        /// renderer need them, not because anyone would write them in a document.
        /// </summary>
        private static readonly HashSet<string> NotAuthorable = new(StringComparer.Ordinal)
        {
            nameof(UIControl.Position),
            nameof(UIControl.Parent),
            nameof(UIControl.Children),
            nameof(UIControl.Dialog),
            nameof(UIControl.StaticElementsTexture),
            nameof(UIControl.MeasuredContentSize),
            nameof(UIControl.HasKeyboardFocus),
            nameof(UIControl.LayoutScale),
            nameof(UIControl.IsStaticElement),
            "ScrollOffset",
            "MaxScrollOffset",
            "Fraction",     // derived from Value/Min/Max
            "SelectedTab",
            "SelectedIndex",
            "SelectedItem",
            "DrawMode",
        };

        /// <summary>
        /// Controls that hold authored children. Everything else either has no children or
        /// builds and owns them itself - a button's label, a list's rows - and taking a drop
        /// would put a control somewhere its owner is about to overwrite.
        /// </summary>
        private static readonly HashSet<string> Containers = new(StringComparer.Ordinal)
        {
            nameof(RectangleControl),
        };

        private static readonly Dictionary<string, string> Categories = new(StringComparer.Ordinal)
        {
            ["Rectangle"] = "Containers",
            ["Tabs"] = "Containers",
            ["TitleBar"] = "Containers",
            ["Label"] = "Basic",
            ["Button"] = "Basic",
            ["Image"] = "Basic",
            ["ProgressBar"] = "Basic",
            ["Checkbox"] = "Input",
            ["TextInput"] = "Input",
            ["Dropdown"] = "Input",
            ["ColorPicker"] = "Input",
            ["ListView"] = "Data",
            ["TreeView"] = "Data",
            ["DetailView"] = "Data",
            ["PixelCanvas"] = "Data",
            ["InventoryGrid"] = "Game",
            ["ItemSlot"] = "Game",
            ["ItemListView"] = "Game",
            ["ItemTypeSelector"] = "Game",
        };

        private static readonly Dictionary<string, string> Summaries = new(StringComparer.Ordinal)
        {
            ["Rectangle"] = "A container. Stacks its children along InsideOrientation.",
            ["Label"] = "Text. Sizes itself from the text and the font.",
            ["Button"] = "A vanilla styled button with an optional icon.",
            ["Checkbox"] = "A box with a caption.",
            ["TextInput"] = "A single line text field.",
            ["Dropdown"] = "A closed box that opens a list in a popup.",
            ["ProgressBar"] = "A bar between Min and Max with a caption over it.",
            ["Image"] = "A picture from an asset location.",
            ["TitleBar"] = "A full width vanilla title bar with a close button.",
            ["Tabs"] = "A tab strip over a page host. Put Tab elements inside it.",
            ["ColorPicker"] = "A hue strip and a saturation and value square.",
            ["PixelCanvas"] = "A grid of pixels that can be painted.",
            ["ListView"] = "A scrolling list of rows with an optional detail panel.",
            ["TreeView"] = "A scrolling tree of expandable nodes.",
            ["DetailView"] = "The key and value panel a list view shows for a row.",
            ["InventoryGrid"] = "A grid of item slots over a real inventory. Needs the game to draw stacks.",
            ["ItemSlot"] = "A single item slot. Needs the game to draw a stack.",
            ["ItemListView"] = "A list view whose rows carry item stacks.",
            ["ItemTypeSelector"] = "A picker for an item or block type.",
        };

        /// <summary>
        /// Sensible starting attributes so a dropped control says something straight away.
        ///
        /// Deliberately no Size on a container: a size turns auto sizing off, and a container
        /// that cannot grow clips the children that no longer fit - the second control added to
        /// it would be cut in half and the third would have no height at all. An empty container
        /// measures to twice its padding instead, which is small, so the designer draws a drop
        /// zone around it rather than pinning a size it would have to be told to give up again.
        /// </summary>
        private static readonly Dictionary<string, KeyValuePair<string, string>[]> Defaults =
            new(StringComparer.Ordinal)
            {
                ["Rectangle"] = new[]
                {
                    new KeyValuePair<string, string>("InsideOrientation", "Top"),
                    new KeyValuePair<string, string>("Padding", "8"),
                    new KeyValuePair<string, string>("BorderColor", "#7f6a4cff"),
                },
                ["Label"] = new[] { new KeyValuePair<string, string>("Text", "Label") },
                ["Button"] = new[] { new KeyValuePair<string, string>("Text", "Button") },
                ["Checkbox"] = new[] { new KeyValuePair<string, string>("Text", "Checkbox") },
                ["TextInput"] = new[] { new KeyValuePair<string, string>("PlaceholderText", "Type here") },
                ["ProgressBar"] = new[] { new KeyValuePair<string, string>("Value", "50") },
                ["TitleBar"] = new[] { new KeyValuePair<string, string>("Title", "Title") },
            };

        private static readonly Lazy<IReadOnlyList<ControlDescriptor>> _all =
            new(Build, LazyThreadSafetyMode.ExecutionAndPublication);

        public static IReadOnlyList<ControlDescriptor> All => _all.Value;

        private static readonly Lazy<Dictionary<string, ControlDescriptor>> _byTag =
            new(() => All.ToDictionary(d => d.Tag, StringComparer.OrdinalIgnoreCase),
                LazyThreadSafetyMode.ExecutionAndPublication);

        /// <summary>The descriptor for a markup tag, or null when the tag is unknown.</summary>
        public static ControlDescriptor? Get(string tag)
        {
            // The root is a plain container under a name that says what it is.
            if (string.Equals(tag, RootTag, StringComparison.OrdinalIgnoreCase))
                tag = "Rectangle";

            return _byTag.Value.TryGetValue(tag, out ControlDescriptor? d) ? d : null;
        }

        public static IEnumerable<IGrouping<string, ControlDescriptor>> ByCategory() =>
            All.OrderBy(d => CategoryOrder(d.Category)).ThenBy(d => d.Tag).GroupBy(d => d.Category);

        private static int CategoryOrder(string category) => category switch
        {
            "Containers" => 0,
            "Basic" => 1,
            "Input" => 2,
            "Data" => 3,
            "Game" => 4,
            _ => 5,
        };

        /// <summary>The tag a control type is written as: TextLabelControl becomes Label.</summary>
        public static string TagFor(Type controlType)
        {
            string name = controlType.Name;

            if (name.EndsWith("Control", StringComparison.Ordinal))
                name = name.Substring(0, name.Length - "Control".Length);

            // The only control whose class name is longer than its idea.
            if (name == "TextLabel")
                name = "Label";

            return name;
        }

        private static IReadOnlyList<ControlDescriptor> Build()
        {
            var result = new List<ControlDescriptor>();

            foreach (Type type in TypesOf(typeof(UIControl).Assembly))
            {
                if (!type.IsClass || type.IsAbstract || !typeof(UIControl).IsAssignableFrom(type))
                    continue;

                if (!type.IsPublic)
                    continue;

                ConstructorInfo? ctor = UsableConstructor(type);
                if (ctor == null)
                    continue;

                string tag = TagFor(type);

                result.Add(new ControlDescriptor
                {
                    Tag = tag,
                    ClrType = type,
                    Category = Categories.TryGetValue(tag, out string? c) ? c : "Controls",
                    Summary = Summaries.TryGetValue(tag, out string? s) ? s : type.Name,
                    AcceptsChildren = Containers.Contains(type.Name),
                    Properties = PropertiesOf(type, ctor),
                    Create = () => (UIControl)ctor.Invoke(DefaultArguments(ctor)),
                    DropDefaults = Defaults.TryGetValue(tag, out var d)
                        ? d
                        : Array.Empty<KeyValuePair<string, string>>(),
                });
            }

            result.Add(TabDescriptor());

            return result;
        }

        /// <summary>
        /// A tab page. It is not a UIControl - TabsControl.AddTab takes a caption and one
        /// control - but the document needs somewhere to write the caption, so it gets a tag.
        /// </summary>
        private static ControlDescriptor TabDescriptor()
        {
            return new ControlDescriptor
            {
                Tag = TabTag,
                ClrType = typeof(TabPage),
                Category = "Containers",
                Summary = "One page of a Tabs control. Holds a single control.",
                AcceptsChildren = true,
                MaxChildren = 1,
                Properties = Array.Empty<MarkupProperty>(),
                Create = () => throw new InvalidOperationException(
                    "A Tab is built by its Tabs parent, not on its own."),
                DropDefaults = new[] { new KeyValuePair<string, string>("Caption", "Tab") },
            };
        }

        /// <summary>
        /// Every type in the assembly that the runtime managed to load.
        ///
        /// The mod is compiled against the game's own mod assemblies, and a control that
        /// mentions one of those cannot be loaded when it is missing. That is not a reason to
        /// have no toolbox at all - the other controls are fine, so the ones that failed are
        /// dropped and the rest carry on.
        /// </summary>
        private static IEnumerable<Type> TypesOf(Assembly assembly)
        {
            try
            {
                return assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                return ex.Types.Where(t => t != null)!;
            }
        }

        /// <summary>
        /// The constructor the designer can call: every parameter optional, so a control can be
        /// built from a tag name alone. A control that needs something from the game to exist at
        /// all - a context menu needs the button it hangs off - has none and stays out.
        /// </summary>
        private static ConstructorInfo? UsableConstructor(Type type)
        {
            return type.GetConstructors(BindingFlags.Public | BindingFlags.Instance)
                .Where(c => c.GetParameters().All(p => p.IsOptional))
                .OrderBy(c => c.GetParameters().Length)
                .FirstOrDefault();
        }

        private static object?[] DefaultArguments(ConstructorInfo ctor)
        {
            return ctor.GetParameters().Select(p => p.DefaultValue).ToArray();
        }

        private static IReadOnlyList<MarkupProperty> PropertiesOf(Type type, ConstructorInfo ctor)
        {
            // A fresh instance is the honest source of the defaults: a property initialiser, a
            // constructor body and a field default all end up in the same place here.
            UIControl probe;
            try
            {
                probe = (UIControl)ctor.Invoke(DefaultArguments(ctor));
            }
            catch
            {
                // A control that cannot even be built headlessly is not usable in the designer,
                // but its properties are still worth listing - just without defaults.
                probe = null!;
            }

            var seen = new HashSet<string>(StringComparer.Ordinal);
            var props = new List<MarkupProperty>();

            // Most derived first, so a "new" property (TextLabelControl.Orientation) wins over
            // the base one it hides instead of both showing up.
            foreach (Type level in Hierarchy(type))
            {
                foreach (PropertyInfo info in level.GetProperties(
                    BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                {
                    if (!seen.Add(info.Name))
                        continue;

                    if (NotAuthorable.Contains(info.Name))
                        continue;

                    if (info.GetMethod == null || info.SetMethod == null || !info.SetMethod.IsPublic)
                        continue;

                    if (info.GetIndexParameters().Length > 0)
                        continue;

                    if (!ValueConverters.IsSupported(info.PropertyType))
                        continue;

                    object? defaultValue = null;
                    if (probe != null)
                    {
                        try { defaultValue = info.GetValue(probe); }
                        catch { /* a getter that needs the game stays without a default */ }
                    }

                    props.Add(new MarkupProperty
                    {
                        Name = info.Name,
                        Type = Nullable.GetUnderlyingType(info.PropertyType) ?? info.PropertyType,
                        Info = info,
                        DefaultValue = defaultValue,
                        Group = level == typeof(UIControl) ? "Layout" : "Control",
                    });
                }
            }

            // The control's own properties read first, the shared layout ones after them.
            return props
                .OrderBy(p => p.Group == "Layout" ? 1 : 0)
                .ThenBy(p => LayoutOrder(p.Name))
                .ThenBy(p => p.Name, StringComparer.Ordinal)
                .ToList();
        }

        /// <summary>Reading order for the shared properties, rather than alphabetical.</summary>
        private static int LayoutOrder(string name) => name switch
        {
            nameof(UIControl.Name) => 0,
            nameof(UIControl.Size) => 1,
            nameof(UIControl.IsAutoSize) => 2,
            nameof(UIControl.MaxSize) => 3,
            nameof(UIControl.Margin) => 4,
            nameof(UIControl.Padding) => 5,
            nameof(UIControl.InsideOrientation) => 6,
            nameof(UIControl.Orientation) => 7,
            nameof(UIControl.Index) => 8,
            nameof(UIControl.ClipsChildren) => 9,
            _ => 50,
        };

        private static IEnumerable<Type> Hierarchy(Type type)
        {
            for (Type? t = type; t != null && t != typeof(object); t = t.BaseType)
                yield return t;
        }
    }
}
