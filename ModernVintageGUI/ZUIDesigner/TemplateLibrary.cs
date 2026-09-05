namespace ModernVintageGUI.Designer
{
    /// <summary>A document that ships with the designer, to start from rather than from blank.</summary>
    public sealed record DesignTemplate(string Name, string Description, string Markup);

    /// <summary>
    /// The starting points in the File menu. They are files under Templates rather than strings
    /// in here, so a new one is a new file and nothing else.
    /// </summary>
    public sealed class TemplateLibrary
    {
        private readonly Lazy<IReadOnlyList<DesignTemplate>> _templates;

        public TemplateLibrary(IWebHostEnvironment environment)
        {
            _templates = new Lazy<IReadOnlyList<DesignTemplate>>(() => Load(environment.ContentRootPath));
        }

        public IReadOnlyList<DesignTemplate> All => _templates.Value;

        public DesignTemplate? Get(string name) =>
            All.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));

        private static IReadOnlyList<DesignTemplate> Load(string contentRoot)
        {
            string directory = Path.Combine(contentRoot, "Templates");

            if (!Directory.Exists(directory))
                return Array.Empty<DesignTemplate>();

            var templates = new List<DesignTemplate>();

            foreach (string file in Directory.EnumerateFiles(directory, "*.mvgui").OrderBy(f => f))
            {
                string markup = File.ReadAllText(file);

                // The first line may be a comment saying what the template is for; the designer
                // shows it in the menu and keeps it in the document.
                string description = "";
                int start = markup.IndexOf("<!--", StringComparison.Ordinal);
                int end = markup.IndexOf("-->", StringComparison.Ordinal);
                if (start >= 0 && end > start)
                {
                    description = markup.Substring(start + 4, end - start - 4).Trim();
                }

                templates.Add(new DesignTemplate(
                    Path.GetFileNameWithoutExtension(file),
                    description,
                    markup));
            }

            return templates;
        }
    }
}
