using System.Globalization;
using Cairo;
using IS2Mod.ControlTypes;

namespace ModernVintageGUI.Designer.Markup
{
    /// <summary>
    /// Turns attribute text into property values and back. Every conversion has to round trip:
    /// the markup is the source of truth, so writing a value the parser cannot read back would
    /// lose the document on the next save.
    /// </summary>
    public static class ValueConverters
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        /// <summary>The types an attribute is allowed to carry.</summary>
        public static bool IsSupported(Type type)
        {
            type = Nullable.GetUnderlyingType(type) ?? type;

            return type == typeof(string)
                || type == typeof(bool)
                || type == typeof(int)
                || type == typeof(double)
                || type == typeof(float)
                || type == typeof(PointD)
                || type == typeof(ElementColor)
                || type.IsEnum;
        }

        /// <summary>
        /// Parses <paramref name="text"/> for <paramref name="type"/>. Returns false rather than
        /// throwing - a half typed attribute in the markup editor is a normal state, not a crash.
        /// </summary>
        public static bool TryParse(Type type, string text, out object? value, out string? error)
        {
            type = Nullable.GetUnderlyingType(type) ?? type;
            value = null;
            error = null;

            text = text.Trim();

            if (type == typeof(string))
            {
                value = text;
                return true;
            }

            if (type == typeof(bool))
            {
                if (bool.TryParse(text, out bool b)) { value = b; return true; }
                error = "expected true or false";
                return false;
            }

            if (type == typeof(int))
            {
                if (int.TryParse(text, NumberStyles.Integer, Inv, out int i)) { value = i; return true; }
                error = "expected a whole number";
                return false;
            }

            if (type == typeof(double) || type == typeof(float))
            {
                if (double.TryParse(text, NumberStyles.Float, Inv, out double d))
                {
                    value = type == typeof(float) ? (float)d : d;
                    return true;
                }
                error = "expected a number";
                return false;
            }

            if (type == typeof(PointD))
            {
                if (TryParsePoint(text, out PointD p)) { value = p; return true; }
                error = "expected \"width,height\", for example \"200,120\"";
                return false;
            }

            if (type == typeof(ElementColor))
            {
                if (TryParseColor(text, out ElementColor? c)) { value = c; return true; }
                error = "expected #rgb, #rrggbb or #rrggbbaa";
                return false;
            }

            if (type.IsEnum)
            {
                if (Enum.TryParse(type, text, ignoreCase: true, out object? e)) { value = e; return true; }
                error = "expected one of " + string.Join(", ", Enum.GetNames(type));
                return false;
            }

            error = "unsupported attribute type " + type.Name;
            return false;
        }

        /// <summary>The text form of a value, i.e. the inverse of <see cref="TryParse"/>.</summary>
        public static string Format(object? value)
        {
            switch (value)
            {
                case null:
                    return "";
                case string s:
                    return s;
                case bool b:
                    return b ? "true" : "false";
                case int i:
                    return i.ToString(Inv);
                case double d:
                    return Round(d);
                case float f:
                    return Round(f);
                case PointD p:
                    return Round(p.X) + "," + Round(p.Y);
                case ElementColor c:
                    return FormatColor(c);
                case Enum e:
                    return e.ToString();
                default:
                    return value.ToString() ?? "";
            }
        }

        private static string Round(double d)
        {
            // Author units are hand written numbers; trailing float noise in the markup would
            // make every save look like a change.
            return Math.Round(d, 4).ToString(Inv);
        }

        public static bool TryParsePoint(string text, out PointD point)
        {
            point = default;

            string[] parts = text.Split(new[] { ',', 'x', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2)
                return false;

            if (!double.TryParse(parts[0], NumberStyles.Float, Inv, out double x)) return false;
            if (!double.TryParse(parts[1], NumberStyles.Float, Inv, out double y)) return false;

            point = new PointD(x, y);
            return true;
        }

        public static bool TryParseColor(string text, out ElementColor? color)
        {
            color = null;

            if (string.IsNullOrWhiteSpace(text))
                return false;

            string hex = text.Trim();
            if (hex.StartsWith("#", StringComparison.Ordinal))
                hex = hex.Substring(1);

            // #rgb shorthand, the same one CSS uses.
            if (hex.Length == 3)
                hex = "" + hex[0] + hex[0] + hex[1] + hex[1] + hex[2] + hex[2];

            if (hex.Length == 6)
                hex += "ff";

            if (hex.Length != 8)
                return false;

            if (!byte.TryParse(hex.Substring(0, 2), NumberStyles.HexNumber, Inv, out byte r)) return false;
            if (!byte.TryParse(hex.Substring(2, 2), NumberStyles.HexNumber, Inv, out byte g)) return false;
            if (!byte.TryParse(hex.Substring(4, 2), NumberStyles.HexNumber, Inv, out byte b)) return false;
            if (!byte.TryParse(hex.Substring(6, 2), NumberStyles.HexNumber, Inv, out byte a)) return false;

            color = new ElementColor(r, g, b, a);
            return true;
        }

        public static string FormatColor(ElementColor c)
        {
            // The alpha byte is always written. Dropping it for opaque colours would be prettier
            // but makes "is this attribute still the default" harder to answer.
            return $"#{c.R:x2}{c.G:x2}{c.B:x2}{c.A:x2}";
        }

        /// <summary>The CSS colour for a swatch in the property grid.</summary>
        public static string ToCss(ElementColor c)
        {
            return string.Format(Inv, "rgba({0},{1},{2},{3})", c.R, c.G, c.B, Math.Round(c.A / 255.0, 3));
        }

        /// <summary>True when two values are the same as far as the markup is concerned.</summary>
        public static bool SameValue(object? a, object? b)
        {
            if (a is null || b is null)
                return a is null && b is null;

            if (a is ElementColor ca && b is ElementColor cb)
                return ca.R == cb.R && ca.G == cb.G && ca.B == cb.B && ca.A == cb.A;

            if (a is PointD pa && b is PointD pb)
                return Math.Abs(pa.X - pb.X) < 0.0001 && Math.Abs(pa.Y - pb.Y) < 0.0001;

            return Equals(a, b);
        }
    }
}
