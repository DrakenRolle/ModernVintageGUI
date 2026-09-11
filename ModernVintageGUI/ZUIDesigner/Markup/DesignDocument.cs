using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace ModernVintageGUI.Designer.Markup
{
    /// <summary>
    /// The document being designed. The markup is the source of truth: dropping a control,
    /// dragging one somewhere else and typing in the property grid all edit this XML, and the
    /// control tree is rebuilt from it afterwards. That is what keeps the text pane and the
    /// canvas from being able to disagree.
    /// </summary>
    public sealed class DesignDocument
    {
        private readonly List<string> _undo = new();
        private readonly List<string> _redo = new();

        private XDocument _xml;

        private DesignDocument(XDocument xml)
        {
            _xml = xml;
        }

        public XDocument Xml => _xml;

        public bool CanUndo => _undo.Count > 0;
        public bool CanRedo => _redo.Count > 0;

        /// <summary>Raised after any edit, so the designer knows to lay out and draw again.</summary>
        public event Action? Changed;

        public static DesignDocument Empty()
        {
            return Parse(
                $"<{ControlCatalog.RootTag} Name=\"root\" InsideOrientation=\"Top\" Padding=\"10\" " +
                "BackgroundColor=\"#33291fff\">\n</" + ControlCatalog.RootTag + ">",
                out _)!;
        }

        /// <summary>
        /// Reads markup. Returns null and an error when the text is not well formed XML - a
        /// state the markup editor is in on most keystrokes, so it must not throw.
        /// </summary>
        public static DesignDocument? Parse(string markup, out string? error)
        {
            error = null;

            try
            {
                XDocument xml = XDocument.Parse(markup, LoadOptions.SetLineInfo);

                if (xml.Root == null)
                {
                    error = "The document has no root element.";
                    return null;
                }

                return new DesignDocument(xml);
            }
            catch (XmlException ex)
            {
                error = $"line {ex.LineNumber}, column {ex.LinePosition}: {ex.Message}";
                return null;
            }
        }

        /// <summary>Builds the control tree this document describes.</summary>
        public BuildResult Build() => MarkupBuilder.Build(_xml);

        /// <summary>The markup, formatted the way it is written to a file.</summary>
        public string ToMarkup()
        {
            var settings = new XmlWriterSettings
            {
                Indent = true,
                IndentChars = "  ",
                OmitXmlDeclaration = true,
                NewLineChars = "\n",
                Encoding = new UTF8Encoding(false),
            };

            var sb = new StringBuilder();
            using (XmlWriter writer = XmlWriter.Create(sb, settings))
            {
                _xml.Save(writer);
            }

            return sb.ToString();
        }

        public XElement? ElementAt(string path) => MarkupBuilder.ElementAt(_xml, path);

        #region Editing
        /// <summary>
        /// Replaces the whole document, e.g. after the markup pane was edited by hand. The old
        /// text goes on the undo stack, so a bad paste is one Ctrl+Z away.
        /// </summary>
        public void ReplaceWith(XDocument replacement)
        {
            PushUndo();
            _xml = replacement;
            Changed?.Invoke();
        }

        /// <summary>
        /// Inserts a new control of <paramref name="tag"/> as child number
        /// <paramref name="index"/> of <paramref name="parentPath"/>. Returns the path of the
        /// new element, or null when the target does not accept it.
        /// </summary>
        public string? Insert(string tag, string parentPath, int index)
        {
            ControlDescriptor? descriptor = ControlCatalog.Get(tag);

            if (descriptor == null)
                return null;

            if (IsTitleBar(descriptor.Tag))
            {
                (parentPath, index) = TitleBarSlot();
            }

            XElement? parent = ElementAt(parentPath);

            if (parent == null)
                return null;

            if (!CanHold(parent, out int capacity))
                return null;

            int count = parent.Elements().Count();
            if (count >= capacity)
                return null;

            var element = new XElement(descriptor.Tag);

            foreach (KeyValuePair<string, string> attribute in descriptor.DropDefaults)
            {
                element.SetAttributeValue(attribute.Key, attribute.Value);
            }

            element.SetAttributeValue("Name", UniqueName(descriptor.Tag));

            // A tab strip with no page is a strip of nothing, so a new Tabs starts with one.
            if (descriptor.ClrType.Name == "TabsControl")
            {
                element.Add(new XElement(ControlCatalog.TabTag, new XAttribute("Caption", "Tab 1")));
            }

            PushUndo();
            InsertAt(parent, index, element);

            if (IsTitleBar(descriptor.Tag))
                MakeRoomForTitleBar();

            Changed?.Invoke();

            return MarkupBuilder.PathOfElement(element);
        }

        /// <summary>The tag of the vanilla title bar, which the designer places for you.</summary>
        public const string TitleBarTag = "TitleBar";

        public static bool IsTitleBar(string tag) =>
            string.Equals(tag, TitleBarTag, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Where a title bar goes, wherever it was dropped: first child of the root.
        ///
        /// A title bar is the top edge of the window rather than a control in a stack. It
        /// measures to a minimum width and lets the parent stretch it across the dialog, so it
        /// only looks right as the first thing in the outermost container - anywhere else it is
        /// a bar across the middle of something.
        /// </summary>
        public (string ParentPath, int Index) TitleBarSlot() => (MarkupBuilder.RootPath, 0);

        /// <summary>
        /// A title bar has to reach both edges, and it cannot: it is laid out inside its parent's
        /// padding like every other control. So the root gives up its padding, and the content
        /// under the bar carries it instead - which is how the framework's own title bar dialogs
        /// are built.
        /// </summary>
        private void MakeRoomForTitleBar()
        {
            XElement? root = _xml.Root;
            if (root == null)
                return;

            string? padding = (string?)root.Attribute("Padding");

            if (padding == null || padding == "0")
                return;

            root.SetAttributeValue("Padding", "0");

            // The padding the root gave up moves to the content beside the bar, so the dialog
            // does not suddenly draw its controls flush against the frame.
            foreach (XElement sibling in root.Elements())
            {
                if (IsTitleBar(sibling.Name.LocalName))
                    continue;

                if (CanHold(sibling, out _) && sibling.Attribute("Padding") == null)
                    sibling.SetAttributeValue("Padding", padding);
            }
        }

        /// <summary>
        /// Moves an existing element. Returns the path it ended up at, which is not the one that
        /// was asked for when the element moved forwards inside its own parent.
        /// </summary>
        public string? Move(string sourcePath, string parentPath, int index)
        {
            XElement? source = ElementAt(sourcePath);

            // Dragging the title bar somewhere else does not move it - there is only one place
            // it belongs. Dropping it back on that place is what puts it right again after a
            // hand edit left it in the middle of a stack.
            if (source != null && IsTitleBar(source.Name.LocalName))
            {
                (parentPath, index) = TitleBarSlot();
            }

            XElement? parent = ElementAt(parentPath);

            if (source == null || parent == null || source == parent)
                return null;

            // Dropping a container into its own subtree would detach the whole branch.
            if (source.Descendants().Contains(parent))
                return null;

            if (!CanHold(parent, out int capacity))
                return null;

            bool sameParent = source.Parent == parent;
            int currentIndex = sameParent ? parent.Elements().ToList().IndexOf(source) : -1;

            if (sameParent && (index == currentIndex || index == currentIndex + 1))
                return sourcePath; // dropped back where it already is

            if (!sameParent && parent.Elements().Count() >= capacity)
                return null;

            PushUndo();

            // Re-resolve after the undo snapshot: PushUndo does not touch the tree, but taking
            // the element out shifts the indices of everything after it in the same parent.
            source.Remove();

            if (sameParent && index > currentIndex)
                index--;

            InsertAt(parent, index, source);

            if (IsTitleBar(source.Name.LocalName))
                MakeRoomForTitleBar();

            Changed?.Invoke();

            return MarkupBuilder.PathOfElement(source);
        }

        /// <summary>Removes an element. The root cannot be removed.</summary>
        public bool Delete(string path)
        {
            XElement? element = ElementAt(path);

            if (element == null || element.Parent == null)
                return false;

            PushUndo();
            element.Remove();
            Changed?.Invoke();

            return true;
        }

        /// <summary>Copies an element and its subtree in next to the original.</summary>
        public string? Duplicate(string path)
        {
            XElement? element = ElementAt(path);

            if (element?.Parent == null)
                return null;

            var copy = new XElement(element);
            RenameSubtree(copy);

            PushUndo();
            element.AddAfterSelf(copy);
            Changed?.Invoke();

            return MarkupBuilder.PathOfElement(copy);
        }

        /// <summary>
        /// Writes one attribute. An empty value removes it, which is how a property goes back to
        /// its default instead of being pinned to a value that happens to equal the default.
        /// </summary>
        public bool SetAttribute(string path, string name, string? value)
        {
            XElement? element = ElementAt(path);
            if (element == null)
                return false;

            string? existing = (string?)element.Attribute(name);
            string? wanted = string.IsNullOrWhiteSpace(value) ? null : value;

            if (existing == wanted)
                return false;

            PushUndo();
            element.SetAttributeValue(name, wanted);
            Changed?.Invoke();

            return true;
        }

        private static void InsertAt(XElement parent, int index, XElement element)
        {
            List<XElement> siblings = parent.Elements().ToList();

            if (index <= 0)
            {
                parent.AddFirst(element);
            }
            else if (index >= siblings.Count)
            {
                parent.Add(element);
            }
            else
            {
                siblings[index - 1].AddAfterSelf(element);
            }
        }

        /// <summary>Whether an element takes children, and how many.</summary>
        public static bool CanHold(XElement element, out int capacity)
        {
            capacity = 0;

            if (string.Equals(element.Name.LocalName, ControlCatalog.TabTag, StringComparison.OrdinalIgnoreCase))
            {
                capacity = 1;
                return true;
            }

            ControlDescriptor? descriptor = ControlCatalog.Get(element.Name.LocalName);
            if (descriptor == null || !descriptor.AcceptsChildren)
                return false;

            capacity = descriptor.MaxChildren;
            return true;
        }

        /// <summary>Every Name the document uses, so a new one can avoid all of them.</summary>
        private HashSet<string> TakenNames()
        {
            return new HashSet<string>(
                _xml.Descendants()
                    .Select(e => (string?)e.Attribute("Name"))
                    .Where(n => !string.IsNullOrEmpty(n))!,
                StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// A name none of <paramref name="taken"/> uses, e.g. "button3". The name is added to the
        /// set, so handing out several in a row cannot produce the same one twice - which is
        /// exactly what renaming a copied subtree does, before any of it is in the document.
        /// </summary>
        private static string NextName(string tag, HashSet<string> taken)
        {
            string stem = char.ToLowerInvariant(tag[0]) + tag.Substring(1);

            for (int i = 1; ; i++)
            {
                string candidate = stem + i;
                if (taken.Add(candidate))
                    return candidate;
            }
        }

        /// <summary>A name no element in the document uses yet.</summary>
        private string UniqueName(string tag) => NextName(tag, TakenNames());

        /// <summary>Gives a copied subtree fresh names, so two controls never share one.</summary>
        private void RenameSubtree(XElement root)
        {
            HashSet<string> taken = TakenNames();

            foreach (XElement element in root.DescendantsAndSelf())
            {
                if (element.Attribute("Name") != null)
                {
                    element.SetAttributeValue("Name", NextName(element.Name.LocalName, taken));
                }
            }
        }
        #endregion

        #region Undo
        private void PushUndo()
        {
            _undo.Add(ToMarkup());
            _redo.Clear();

            // Deep enough to get out of trouble, shallow enough that a long session does not
            // keep every version of the document alive.
            const int limit = 100;
            if (_undo.Count > limit)
                _undo.RemoveRange(0, _undo.Count - limit);
        }

        public bool Undo() => Step(_undo, _redo);

        public bool Redo() => Step(_redo, _undo);

        private bool Step(List<string> from, List<string> to)
        {
            if (from.Count == 0)
                return false;

            string markup = from[^1];
            from.RemoveAt(from.Count - 1);

            to.Add(ToMarkup());

            _xml = XDocument.Parse(markup, LoadOptions.SetLineInfo);
            Changed?.Invoke();

            return true;
        }
        #endregion
    }
}
