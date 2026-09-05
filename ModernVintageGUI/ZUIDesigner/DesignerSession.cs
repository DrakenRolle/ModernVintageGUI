using System.Xml.Linq;
using ModernVintageGUI.Designer.Markup;
using ModernVintageGUI.Designer.Rendering;

namespace ModernVintageGUI.Designer
{
    /// <summary>
    /// Everything one designer window is doing: the document, what is selected, and the last
    /// picture drawn from it. One per browser connection.
    /// </summary>
    public sealed class DesignerSession
    {
        private readonly DesignRenderer _renderer = new();
        private readonly Dictionary<string, int> _activeTabs = new(StringComparer.Ordinal);

        private DesignDocument _document = DesignDocument.Empty();

        public DesignerSession()
        {
            Attach(_document);
            RenderInternal();
        }

        public DesignDocument Document => _document;

        public RenderResult Preview { get; private set; } = new();

        /// <summary>The path of the selected element, or null when nothing is selected.</summary>
        public string? SelectedPath { get; private set; }

        /// <summary>GUI scale the preview is drawn at, the same slider the game has.</summary>
        public double LayoutScale { get; private set; } = 1.0;

        /// <summary>The markup as the text pane shows it. Kept in step with the document.</summary>
        public string MarkupText { get; private set; } = "";

        /// <summary>Set while the text pane holds something that is not well formed XML.</summary>
        public string? MarkupError { get; private set; }

        public string? FileName { get; set; }

        /// <summary>Raised when anything the UI shows has changed.</summary>
        public event Action? Changed;

        #region Document
        public void Load(DesignDocument document, string? fileName = null)
        {
            _document.Changed -= OnDocumentChanged;

            _document = document;
            FileName = fileName;
            SelectedPath = null;
            MarkupError = null;
            _activeTabs.Clear();

            Attach(_document);
            RenderInternal();
            Changed?.Invoke();
        }

        private void Attach(DesignDocument document)
        {
            document.Changed += OnDocumentChanged;
        }

        private void OnDocumentChanged()
        {
            RenderInternal();
            Changed?.Invoke();
        }

        /// <summary>
        /// Takes markup typed into the text pane. Bad XML is remembered as an error rather than
        /// thrown away, so the half finished text stays on screen while it is being fixed.
        /// </summary>
        public void ApplyMarkupText(string text)
        {
            MarkupText = text;

            DesignDocument? parsed = DesignDocument.Parse(text, out string? error);

            if (parsed == null)
            {
                MarkupError = error;
                Changed?.Invoke();
                return;
            }

            MarkupError = null;

            // Replacing the tree rather than the document keeps the undo history.
            _document.ReplaceWith(parsed.Xml);
        }
        #endregion

        #region Selection
        public void Select(string? path)
        {
            if (SelectedPath == path)
                return;

            SelectedPath = path;
            Changed?.Invoke();
        }

        public XElement? SelectedElement =>
            SelectedPath == null ? null : _document.ElementAt(SelectedPath);

        public ControlDescriptor? SelectedDescriptor
        {
            get
            {
                XElement? element = SelectedElement;
                return element == null ? null : ControlCatalog.Get(element.Name.LocalName);
            }
        }
        #endregion

        #region Editing
        public void Insert(string tag, string parentPath, int index)
        {
            string? path = _document.Insert(tag, parentPath, index);

            if (path != null)
                SelectedPath = path;
        }

        public void Move(string sourcePath, string parentPath, int index)
        {
            string? path = _document.Move(sourcePath, parentPath, index);

            if (path != null)
                SelectedPath = path;
        }

        public void Delete(string path)
        {
            if (_document.Delete(path) && SelectedPath == path)
                SelectedPath = null;
        }

        public void Duplicate(string path)
        {
            string? copy = _document.Duplicate(path);

            if (copy != null)
                SelectedPath = copy;
        }

        public void SetAttribute(string path, string name, string? value)
        {
            _document.SetAttribute(path, name, value);
        }

        public void Undo()
        {
            if (_document.Undo())
                SelectedPath = null;
        }

        public void Redo()
        {
            if (_document.Redo())
                SelectedPath = null;
        }

        public void SetScale(double scale)
        {
            LayoutScale = Math.Clamp(scale, 0.5, 3.0);
            RenderInternal();
            Changed?.Invoke();
        }

        /// <summary>Which page of a Tabs control the preview shows.</summary>
        public void SelectTab(string tabsPath, int index)
        {
            _activeTabs[tabsPath] = index;
            RenderInternal();
            Changed?.Invoke();
        }

        public int ActiveTab(string tabsPath) =>
            _activeTabs.TryGetValue(tabsPath, out int index) ? index : 0;
        #endregion

        /// <summary>Lays out and draws again, and refreshes the text pane from the document.</summary>
        public void RenderInternal()
        {
            Preview = _renderer.Render(_document, LayoutScale, _activeTabs);
            MarkupText = _document.ToMarkup();

            // A selection that an undo or a hand edit removed must not linger as a highlight
            // over whatever control took its place.
            if (SelectedPath != null && _document.ElementAt(SelectedPath) == null)
                SelectedPath = null;
        }

        public void Refresh() => Changed?.Invoke();
    }
}
