using System.Xml;
using System.Xml.Linq;
using Cairo;
using IS2Mod.ControlTypes;
using ModernVintageGUI.ControlTypes;

namespace ModernVintageGUI.Designer.Markup
{
    public enum DiagnosticSeverity { Warning, Error }

    /// <summary>Something the document says that the builder could not honour.</summary>
    public sealed record Diagnostic(DiagnosticSeverity Severity, int Line, string Path, string Message)
    {
        public override string ToString() =>
            Line > 0 ? $"line {Line}: {Message}" : Message;
    }

    /// <summary>
    /// A built tree plus the two directions of the mapping between markup and controls. The
    /// designer needs both: a click on the canvas asks which element that control came from, a
    /// click in the tree asks which control an element became.
    /// </summary>
    public sealed class BuildResult
    {
        public UIControl? Root { get; init; }
        public IReadOnlyDictionary<string, UIControl> ByPath { get; init; } = new Dictionary<string, UIControl>();
        public IReadOnlyDictionary<UIControl, string> PathOf { get; init; } = new Dictionary<UIControl, string>();

        /// <summary>
        /// Where children of an element go. Usually the control itself; for a tab page it is the
        /// control the page holds, which is a different object than the page.
        /// </summary>
        public IReadOnlyDictionary<string, UIControl> ContentHostOf { get; init; } = new Dictionary<string, UIControl>();

        public IReadOnlyList<Diagnostic> Diagnostics { get; init; } = Array.Empty<Diagnostic>();

        public bool HasErrors => Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);
    }

    /// <summary>
    /// Turns a markup document into the real control tree. Nothing here knows about the
    /// designer: the same code is what a mod would call to load a .mvgui file at runtime.
    /// </summary>
    public static class MarkupBuilder
    {
        /// <summary>The path of the root element. Children extend it with their index.</summary>
        public const string RootPath = "r";

        public static string ChildPath(string parentPath, int index) => parentPath + "/" + index;

        /// <summary>The path of an element inside its document.</summary>
        public static string PathOfElement(XElement element)
        {
            var parts = new List<int>();

            for (XElement? e = element; e?.Parent != null; e = e.Parent)
            {
                parts.Add(e.Parent.Elements().ToList().IndexOf(e));
            }

            parts.Reverse();

            return parts.Count == 0 ? RootPath : RootPath + "/" + string.Join("/", parts);
        }

        /// <summary>The element a path points at, or null when the path no longer resolves.</summary>
        public static XElement? ElementAt(XDocument document, string path)
        {
            XElement? current = document.Root;
            if (current == null)
                return null;

            foreach (string part in path.Split('/').Skip(1))
            {
                if (!int.TryParse(part, out int index))
                    return null;

                List<XElement> children = current.Elements().ToList();
                if (index < 0 || index >= children.Count)
                    return null;

                current = children[index];
            }

            return current;
        }

        public static BuildResult Build(XDocument document)
        {
            var diagnostics = new List<Diagnostic>();
            var byPath = new Dictionary<string, UIControl>(StringComparer.Ordinal);
            var pathOf = new Dictionary<UIControl, string>();
            var hostOf = new Dictionary<string, UIControl>(StringComparer.Ordinal);

            if (document.Root == null)
            {
                diagnostics.Add(new Diagnostic(DiagnosticSeverity.Error, 0, RootPath, "The document is empty."));
                return new BuildResult { Diagnostics = diagnostics };
            }

            UIControl? root = BuildElement(document.Root, RootPath, byPath, pathOf, hostOf, diagnostics);

            CheckTitleBars(document.Root, diagnostics);

            return new BuildResult
            {
                Root = root,
                ByPath = byPath,
                PathOf = pathOf,
                ContentHostOf = hostOf,
                Diagnostics = diagnostics,
            };
        }

        private static UIControl? BuildElement(
            XElement element,
            string path,
            Dictionary<string, UIControl> byPath,
            Dictionary<UIControl, string> pathOf,
            Dictionary<string, UIControl> hostOf,
            List<Diagnostic> diagnostics)
        {
            int line = LineOf(element);

            if (string.Equals(element.Name.LocalName, ControlCatalog.TabTag, StringComparison.OrdinalIgnoreCase))
            {
                diagnostics.Add(new Diagnostic(DiagnosticSeverity.Error, line, path,
                    $"<{ControlCatalog.TabTag}> is only allowed directly inside <Tabs>."));
                return null;
            }

            ControlDescriptor? descriptor = ControlCatalog.Get(element.Name.LocalName);
            if (descriptor == null)
            {
                diagnostics.Add(new Diagnostic(DiagnosticSeverity.Error, line, path,
                    $"Unknown control <{element.Name.LocalName}>."));
                return null;
            }

            UIControl control;
            try
            {
                control = descriptor.Create();
            }
            catch (Exception ex)
            {
                diagnostics.Add(new Diagnostic(DiagnosticSeverity.Error, line, path,
                    $"<{element.Name.LocalName}> could not be created: {Unwrap(ex).Message}"));
                return null;
            }

            ApplyAttributes(element, path, descriptor, control, diagnostics);

            byPath[path] = control;
            pathOf[control] = path;
            hostOf[path] = control;

            if (control is TabsControl tabs)
            {
                BuildTabs(element, path, tabs, byPath, pathOf, hostOf, diagnostics);
                return control;
            }

            List<XElement> children = element.Elements().ToList();

            if (children.Count > 0 && !descriptor.AcceptsChildren)
            {
                diagnostics.Add(new Diagnostic(DiagnosticSeverity.Warning, line, path,
                    $"<{element.Name.LocalName}> builds its own content, so the {children.Count} " +
                    "element(s) inside it are ignored."));
                return control;
            }

            for (int i = 0; i < children.Count; i++)
            {
                UIControl? child = BuildElement(
                    children[i], ChildPath(path, i), byPath, pathOf, hostOf, diagnostics);

                if (child != null)
                    control.Children.Add(child);
            }

            return control;
        }

        private static void BuildTabs(
            XElement element,
            string path,
            TabsControl tabs,
            Dictionary<string, UIControl> byPath,
            Dictionary<UIControl, string> pathOf,
            Dictionary<string, UIControl> hostOf,
            List<Diagnostic> diagnostics)
        {
            List<XElement> pages = element.Elements().ToList();

            for (int i = 0; i < pages.Count; i++)
            {
                XElement page = pages[i];
                string pagePath = ChildPath(path, i);
                int line = LineOf(page);

                if (!string.Equals(page.Name.LocalName, ControlCatalog.TabTag, StringComparison.OrdinalIgnoreCase))
                {
                    diagnostics.Add(new Diagnostic(DiagnosticSeverity.Error, line, pagePath,
                        $"<Tabs> only holds <{ControlCatalog.TabTag}> elements, found " +
                        $"<{page.Name.LocalName}>."));
                    continue;
                }

                string caption = (string?)page.Attribute("Caption") ?? "Tab";

                List<XElement> contents = page.Elements().ToList();
                if (contents.Count > 1)
                {
                    diagnostics.Add(new Diagnostic(DiagnosticSeverity.Warning, line, pagePath,
                        "A tab shows one control. Wrap several in a <Rectangle>."));
                }

                UIControl? content = contents.Count > 0
                    ? BuildElement(contents[0], ChildPath(pagePath, 0), byPath, pathOf, hostOf, diagnostics)
                    : null;

                tabs.AddTab(caption, content);

                // A tab page is not a control, so it has no box of its own on the canvas. Its
                // content is what a drop lands in, and what the outline selects.
                if (content != null)
                    hostOf[pagePath] = content;
            }
        }

        private static void ApplyAttributes(
            XElement element,
            string path,
            ControlDescriptor descriptor,
            UIControl control,
            List<Diagnostic> diagnostics)
        {
            int line = LineOf(element);
            bool sawSize = false;
            bool sawAutoSize = false;

            foreach (XAttribute attribute in element.Attributes())
            {
                if (attribute.IsNamespaceDeclaration)
                    continue;

                string name = attribute.Name.LocalName;

                MarkupProperty? property = descriptor.Find(name);
                if (property == null)
                {
                    diagnostics.Add(new Diagnostic(DiagnosticSeverity.Warning, line, path,
                        $"<{element.Name.LocalName}> has no property \"{name}\"."));
                    continue;
                }

                if (!ValueConverters.TryParse(property.Type, attribute.Value, out object? value, out string? error))
                {
                    diagnostics.Add(new Diagnostic(DiagnosticSeverity.Error, line, path,
                        $"{name}=\"{attribute.Value}\": {error}."));
                    continue;
                }

                try
                {
                    property.Info.SetValue(control, value);
                }
                catch (Exception ex)
                {
                    diagnostics.Add(new Diagnostic(DiagnosticSeverity.Error, line, path,
                        $"{name} could not be set: {Unwrap(ex).Message}"));
                    continue;
                }

                if (property.Name == nameof(UIControl.Size)) sawSize = true;
                if (property.Name == nameof(UIControl.IsAutoSize)) sawAutoSize = true;
            }

            // The constructors read a given size as "this control does not auto size" - see the
            // UIControl constructor. The Size setter alone does not, so a document that says
            // Size="200,120" and nothing else would be measured from its content and the size
            // would look ignored. Writing IsAutoSize explicitly still wins.
            if (sawSize && !sawAutoSize)
            {
                control.IsAutoSize = control.Size.X == 0 && control.Size.Y == 0;
            }
        }

        /// <summary>
        /// A title bar is the top edge of the window, not a control in a stack. It is laid out
        /// inside its parent's padding like anything else, so it only reaches both edges as the
        /// first child of a root with no padding. The designer places it there on a drop; this
        /// catches a document that says otherwise, which a hand edit can still produce.
        /// </summary>
        private static void CheckTitleBars(XElement root, List<Diagnostic> diagnostics)
        {
            List<XElement> bars = root.Descendants()
                .Where(e => DesignDocument.IsTitleBar(e.Name.LocalName))
                .ToList();

            if (bars.Count == 0)
                return;

            if (bars.Count > 1)
            {
                diagnostics.Add(new Diagnostic(DiagnosticSeverity.Warning, LineOf(bars[1]),
                    PathOfElement(bars[1]),
                    $"A dialog has one title bar; this document has {bars.Count}."));
            }

            XElement bar = bars[0];

            if (bar.Parent != root || root.Elements().First() != bar)
            {
                diagnostics.Add(new Diagnostic(DiagnosticSeverity.Warning, LineOf(bar), PathOfElement(bar),
                    "A title bar belongs first inside the root. Drag it onto the canvas to put it " +
                    "there, or move it in the markup."));
                return;
            }

            string padding = (string?)root.Attribute("Padding") ?? "0";

            if (padding != "0")
            {
                diagnostics.Add(new Diagnostic(DiagnosticSeverity.Warning, LineOf(root), RootPath,
                    $"The root has Padding=\"{padding}\", so the title bar is inset instead of " +
                    "reaching the edges. Put the padding on the content under the bar."));
            }
        }

        private static int LineOf(XObject node) =>
            node is IXmlLineInfo info && info.HasLineInfo() ? info.LineNumber : 0;

        private static Exception Unwrap(Exception ex) => ex.InnerException ?? ex;
    }
}
