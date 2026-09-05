using System.Text.Json.Serialization;
using Cairo;
using IS2Mod.ControlTypes;
using ModernVintageGUI.ControlTypes;
using IS2Mod.Enums;
using ModernVintageGUI.Designer.Markup;

namespace ModernVintageGUI.Designer.Rendering
{
    /// <summary>
    /// One control's box on the canvas, tied back to the element it came from.
    ///
    /// The whole list goes to the browser as JSON. Working out which container the cursor is
    /// over has to happen there rather than here: a drop target that only updates after a round
    /// trip per mouse move would lag behind the cursor.
    /// </summary>
    public sealed record HitNode
    {
        [JsonPropertyName("path")] public required string Path { get; init; }
        [JsonPropertyName("parent")] public string? Parent { get; init; }
        [JsonPropertyName("tag")] public required string Tag { get; init; }
        [JsonPropertyName("name")] public string Name { get; init; } = "";

        [JsonPropertyName("x")] public double X { get; init; }
        [JsonPropertyName("y")] public double Y { get; init; }
        [JsonPropertyName("w")] public double Width { get; init; }
        [JsonPropertyName("h")] public double Height { get; init; }

        /// <summary>Inner padding in device pixels, so an empty container still has a drop zone.</summary>
        [JsonPropertyName("pad")] public double Padding { get; init; }

        /// <summary>Whether a drop lands inside this node.</summary>
        [JsonPropertyName("container")] public bool IsContainer { get; init; }

        /// <summary>How many children it may hold. A tab page holds one.</summary>
        [JsonPropertyName("capacity")] public int Capacity { get; init; }

        /// <summary>
        /// "v" when children stack downwards, "h" when they stack sideways, "z" when they are
        /// laid on top of each other. It decides whether the insertion caret is a horizontal
        /// line between rows or a vertical one between columns.
        /// </summary>
        [JsonPropertyName("axis")] public string Axis { get; init; } = "z";

        /// <summary>Depth in the tree. The deepest container under the cursor wins.</summary>
        [JsonPropertyName("depth")] public int Depth { get; init; }

        /// <summary>
        /// A container with nothing in it. It measures to twice its padding and so is barely
        /// there, which would make it impossible to aim at - the designer draws a drop zone for
        /// it during a drag and lets the pointer hit that instead.
        /// </summary>
        [JsonPropertyName("empty")] public bool IsEmpty { get; init; }
    }

    public sealed class RenderResult
    {
        public string ImageDataUri { get; init; } = "";
        public int Width { get; init; }
        public int Height { get; init; }
        public IReadOnlyList<HitNode> Nodes { get; init; } = Array.Empty<HitNode>();
        public IReadOnlyList<Diagnostic> Diagnostics { get; init; } = Array.Empty<Diagnostic>();
        public string? Error { get; init; }
        public double Milliseconds { get; init; }
    }

    /// <summary>
    /// Lays a document out with the real layout code and draws it with the real drawing code,
    /// exactly like the layout harness does. Nothing about the picture is an approximation of
    /// the game - it is the same two calls the game makes.
    /// </summary>
    public sealed class DesignRenderer
    {
        /// <summary>
        /// Cairo contexts are not safe to use from several threads, and a Blazor Server host
        /// serves circuits on the thread pool. One lock around the drawing is plenty: a render
        /// takes a few milliseconds and a designer has one user.
        /// </summary>
        private static readonly object _cairoGate = new();

        public RenderResult Render(
            DesignDocument document,
            double layoutScale,
            IReadOnlyDictionary<string, int>? activeTabs = null)
        {
            var started = System.Diagnostics.Stopwatch.StartNew();

            BuildResult build = document.Build();

            if (build.Root == null)
            {
                return new RenderResult
                {
                    Diagnostics = build.Diagnostics,
                    Error = build.Diagnostics.FirstOrDefault(d => d.Severity == DiagnosticSeverity.Error)?.Message
                            ?? "Nothing to draw.",
                };
            }

            try
            {
                lock (_cairoGate)
                {
                    UIControl root = build.Root;

                    SelectTabs(build, activeTabs);

                    root.LayoutScale = layoutScale;
                    root.PerformLayout();

                    int width = Math.Max(1, (int)Math.Ceiling(root.Size.X));
                    int height = Math.Max(1, (int)Math.Ceiling(root.Size.Y));

                    // A runaway size in a half typed document must not try to allocate a
                    // gigabyte of surface.
                    const int limit = 8000;
                    if (width > limit || height > limit)
                    {
                        return new RenderResult
                        {
                            Diagnostics = build.Diagnostics,
                            Error = $"The dialog measures {width}x{height} device pixels, which is " +
                                    $"past the {limit} pixel limit of the preview.",
                        };
                    }

                    byte[] png;

                    using (var surface = new ImageSurface(Format.Argb32, width, height))
                    using (var context = new Context(surface))
                    {
                        context.Antialias = Antialias.Best;
                        root.GenerateRenderData(surface, context);
                        png = PngEncoder.Encode(surface);
                    }

                    return new RenderResult
                    {
                        ImageDataUri = "data:image/png;base64," + Convert.ToBase64String(png),
                        Width = width,
                        Height = height,
                        Nodes = CollectNodes(build, layoutScale),
                        Diagnostics = build.Diagnostics.Concat(CheckOverflow(build, layoutScale)).ToList(),
                        Milliseconds = started.Elapsed.TotalMilliseconds,
                    };
                }
            }
            catch (Exception ex)
            {
                return new RenderResult
                {
                    Diagnostics = build.Diagnostics,
                    Error = ex.Message,
                };
            }
        }

        /// <summary>
        /// Names the containers whose children do not fit.
        ///
        /// A container with a Size does not auto size, and one that cannot grow cuts the
        /// children that no longer fit at its edge - the next control added to it comes out half
        /// height and the one after that comes out with no height at all. That is the layout
        /// working as designed, and it is completely invisible in the picture, because what got
        /// cut is simply not drawn. So it is said out loud instead.
        /// </summary>
        private static IEnumerable<Diagnostic> CheckOverflow(BuildResult build, double layoutScale)
        {
            var diagnostics = new List<Diagnostic>();

            foreach ((string path, UIControl control) in build.ByPath)
            {
                if (control.IsAutoSize || control.Children.Count == 0)
                    continue;

                // Cutting on purpose: a clipping container, or one with a bar to scroll with.
                if (control.ClipsChildren)
                    continue;

                if (control is RectangleControl rectangle &&
                    (rectangle.EnableVerticalScrollbar || rectangle.EnableHorizontalScrollbar))
                {
                    continue;
                }

                double padding = control.Padding * layoutScale * 2;
                double neededX = control.MeasuredContentSize.X + padding;
                double neededY = control.MeasuredContentSize.Y + padding;

                // A pixel of slack: sizes come out of text measurement and are not whole numbers.
                bool tooNarrow = neededX > control.Size.X + 1;
                bool tooShort = neededY > control.Size.Y + 1;

                if (!tooNarrow && !tooShort)
                    continue;

                string tag = ControlCatalog.TagFor(control.GetType());
                string name = string.IsNullOrEmpty(control.Name) ? "" : " " + control.Name;

                // The laid out box rather than the Size attribute: a stacking parent stretches
                // its children across itself, so the two are often different numbers and quoting
                // the attribute would not match what is on screen.
                diagnostics.Add(new Diagnostic(DiagnosticSeverity.Warning, 0, path,
                    $"<{tag}{name}> needs {Math.Ceiling(neededX)}x{Math.Ceiling(neededY)} for its " +
                    $"children but is {Math.Round(control.Size.X)}x{Math.Round(control.Size.Y)} and " +
                    "cannot grow, so the ones that no longer fit are cut off. Clear Size to let it " +
                    "size itself from what is put in it."));
            }

            return diagnostics;
        }

        /// <summary>
        /// Puts the page the designer is looking at on top. A tab that is not selected has its
        /// content taken off the tree, so without this only the first page could ever be edited.
        /// </summary>
        private static void SelectTabs(BuildResult build, IReadOnlyDictionary<string, int>? activeTabs)
        {
            if (activeTabs == null)
                return;

            foreach ((string path, int index) in activeTabs)
            {
                if (build.ByPath.TryGetValue(path, out UIControl? control) && control is TabsControl tabs)
                {
                    tabs.Select(index);
                }
            }
        }

        /// <summary>
        /// Walks the laid out tree and records a box for every control that came from the
        /// document. Controls a composite builds for itself - a button's label, a scrollbar -
        /// are skipped: they have no element to select and nothing may be dropped into them.
        /// </summary>
        private static List<HitNode> CollectNodes(BuildResult build, double layoutScale)
        {
            var nodes = new List<HitNode>();

            // The content of a tab page is reachable in the tree, but its path is the path of
            // the content element. The page itself needs to answer for it as a drop target,
            // which is what this maps back.
            var pageOfContent = new Dictionary<UIControl, string>();
            foreach ((string path, UIControl host) in build.ContentHostOf)
            {
                if (!build.PathOf.TryGetValue(host, out string? hostPath) || hostPath != path)
                    pageOfContent[host] = path;
            }

            void Walk(UIControl control, string? parentPath, int depth)
            {
                string? path = build.PathOf.TryGetValue(control, out string? p) ? p : null;

                if (path != null)
                {
                    // A tab page sits between the Tabs element and its content in the document
                    // but not in the control tree, so the content would otherwise report the
                    // Tabs element as its parent and the two structures would disagree.
                    if (pageOfContent.TryGetValue(control, out string? pagePath))
                    {
                        parentPath = pagePath;
                        depth++;
                    }

                    nodes.Add(Describe(control, path, parentPath, depth, layoutScale));
                    parentPath = path;
                    depth++;
                }

                foreach (UIControl child in control.Children)
                {
                    Walk(child, parentPath, depth);
                }
            }

            Walk(build.Root!, null, 0);

            // An empty tab page has no content control and therefore no box of its own. It
            // still has to be a drop target, so it borrows the page host's box - the strip's
            // sibling, which is where its content would be drawn.
            AddEmptyTabPages(build, nodes, layoutScale);

            return nodes;
        }

        private static void AddEmptyTabPages(BuildResult build, List<HitNode> nodes, double layoutScale)
        {
            foreach ((string tabsPath, UIControl control) in build.ByPath)
            {
                if (control is not TabsControl tabs)
                    continue;

                HitNode? tabsNode = nodes.FirstOrDefault(n => n.Path == tabsPath);
                if (tabsNode == null)
                    continue;

                // The page host is the second child a TabsControl builds: strip on top, pages
                // under it.
                if (tabs.Children.Count < 2)
                    continue;

                UIControl host = tabs.Children[1];

                for (int i = 0; i < tabs.Tabs.Count; i++)
                {
                    string pagePath = MarkupBuilder.ChildPath(tabsPath, i);

                    // A page that holds a control is full, so it is never a drop target and its
                    // content already reported the box that matters.
                    if (tabs.Tabs[i].Content != null)
                        continue;

                    nodes.Add(new HitNode
                    {
                        Path = pagePath,
                        Parent = tabsPath,
                        Tag = ControlCatalog.TabTag,
                        Name = tabs.Tabs[i].Caption,
                        X = host.Position.X,
                        Y = host.Position.Y,
                        Width = Math.Max(host.Size.X, 40 * layoutScale),
                        Height = Math.Max(host.Size.Y, 40 * layoutScale),
                        Padding = 0,
                        IsContainer = true,
                        Capacity = 1,
                        Axis = "z",
                        Depth = tabsNode.Depth + 1,
                        IsEmpty = true,
                    });
                }
            }
        }

        private static HitNode Describe(
            UIControl control, string path, string? parentPath, int depth, double layoutScale)
        {
            ControlDescriptor? descriptor = ControlCatalog.Get(ControlCatalog.TagFor(control.GetType()));

            bool container = descriptor?.AcceptsChildren ?? false;

            return new HitNode
            {
                Path = path,
                Parent = parentPath,
                Tag = ControlCatalog.TagFor(control.GetType()),
                Name = control.Name ?? "",
                X = control.Position.X,
                Y = control.Position.Y,
                Width = control.Size.X,
                Height = control.Size.Y,
                Padding = control.Padding * layoutScale,
                IsContainer = container,
                Capacity = container ? (descriptor?.MaxChildren ?? int.MaxValue) : 0,
                Axis = AxisOf(control.InsideOrientation),
                Depth = depth,
                IsEmpty = container && control.Children.Count == 0,
            };
        }

        private static string AxisOf(Orientation orientation) => orientation switch
        {
            Orientation.Top or Orientation.Bottom => "v",
            Orientation.Left or Orientation.Right => "h",
            _ => "z",
        };
    }
}
