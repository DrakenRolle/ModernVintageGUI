using IS2Mod.ControlTypes;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace LayoutHarness
{
    /// <summary>
    /// A readable, comparable dump of a laid out control tree. Two snapshots being equal is the
    /// definition of "the layout pass is idempotent".
    /// </summary>
    internal static class LayoutSnapshot
    {
        public static string Capture(UIControl root)
        {
            var sb = new StringBuilder();
            Append(sb, root, 0);
            return sb.ToString();
        }

        private static void Append(StringBuilder sb, UIControl control, int depth)
        {
            sb.Append(' ', depth * 2);
            sb.Append(Describe(control));
            sb.Append('\n');

            foreach (UIControl child in control.Children)
            {
                Append(sb, child, depth + 1);
            }
        }

        public static string Describe(UIControl control)
        {
            string name = string.IsNullOrEmpty(control.Name) ? "-" : control.Name;
            string text = control is TextLabelControl label ? $" text=\"{label.Text}\"" : "";

            return string.Format(
                CultureInfo.InvariantCulture,
                "{0,-20} name={1,-14} pos=({2,7:0.##},{3,7:0.##}) size=({4,7:0.##},{5,7:0.##}){6}",
                control.GetType().Name,
                name,
                control.Position.X,
                control.Position.Y,
                control.Size.X,
                control.Size.Y,
                text);
        }

        /// <summary>
        /// Depth first walk including the root.
        /// </summary>
        public static IEnumerable<UIControl> Walk(UIControl root)
        {
            yield return root;

            foreach (UIControl child in root.Children)
            {
                foreach (UIControl descendant in Walk(child))
                {
                    yield return descendant;
                }
            }
        }

        /// <summary>
        /// First line that differs between two snapshots, or null when they are equal.
        /// </summary>
        public static string? FirstDifference(string a, string b)
        {
            string[] linesA = a.Split('\n');
            string[] linesB = b.Split('\n');

            for (int i = 0; i < System.Math.Max(linesA.Length, linesB.Length); i++)
            {
                string lineA = i < linesA.Length ? linesA[i] : "<missing>";
                string lineB = i < linesB.Length ? linesB[i] : "<missing>";

                if (lineA != lineB)
                {
                    return $"line {i + 1}\n    before: {lineA.Trim()}\n    after:  {lineB.Trim()}";
                }
            }

            return null;
        }
    }
}
