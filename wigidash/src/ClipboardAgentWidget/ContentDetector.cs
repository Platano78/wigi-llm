using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace ClipboardAgentWidget
{
    public static class ContentDetector
    {
        // === Content Type Detection ===

        public static string DetectType(string text)
        {
            if (string.IsNullOrEmpty(text)) return "Empty";
            string trimmed = text.TrimStart();

            // JSON
            if ((trimmed.StartsWith("{") && trimmed.TrimEnd().EndsWith("}")) ||
                (trimmed.StartsWith("[") && trimmed.TrimEnd().EndsWith("]")))
                return "JSON";

            // XML/HTML
            if (trimmed.StartsWith("<?xml") || trimmed.StartsWith("<!DOCTYPE") ||
                (trimmed.StartsWith("<") && trimmed.Contains(">") && trimmed.Contains("</")))
                return "XML";

            // SQL
            if (Regex.IsMatch(trimmed, @"^\s*(SELECT|INSERT|UPDATE|DELETE|CREATE|ALTER|DROP|WITH)\s", RegexOptions.IgnoreCase))
                return "SQL";

            // C#
            if (trimmed.Contains("using System") || Regex.IsMatch(trimmed, @"\b(namespace|class|public\s+(static\s+)?(void|int|string|bool|async))\b"))
                return "C#";

            // Python
            if (Regex.IsMatch(trimmed, @"^(def |class |import |from .+ import |if __name__)"))
                return "Python";

            // JavaScript/TypeScript
            if (Regex.IsMatch(trimmed, @"^(const |let |var |function |import |export |=>)") ||
                trimmed.Contains("console.log"))
                return "JS";

            // URL
            if (Regex.IsMatch(trimmed, @"^https?://\S+$"))
                return "URL";

            // Path
            if (Regex.IsMatch(trimmed, @"^[A-Z]:\\|^/[a-z]|^\\\\"))
                return "Path";

            // Base64
            if (Regex.IsMatch(trimmed, @"^[A-Za-z0-9+/=]{20,}$") && trimmed.Length % 4 == 0)
                return "Base64";

            return "Text";
        }

        // === Token Estimation ===

        public static int EstimateTokens(string text)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            // GPT/Claude tokenizer approximation: ~3.5 chars per token for code, ~4 for English
            // Count words + punctuation/symbols as rough proxy
            int words = text.Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries).Length;
            int symbols = text.Count(c => !char.IsLetterOrDigit(c) && !char.IsWhiteSpace(c));
            return (int)(words * 1.3 + symbols * 0.5);
        }

        public static string FormatTokenCount(int tokens)
        {
            if (tokens >= 1000) return (tokens / 1000.0).ToString("F1") + "K";
            return tokens.ToString();
        }

        // === Smart Format ===

        public static string SmartFormat(string text)
        {
            string type = DetectType(text);
            switch (type)
            {
                case "JSON": return FormatJson(text);
                case "XML": return FormatXml(text);
                case "SQL": return FormatSql(text);
                default: return text; // No formatting for unknown types
            }
        }

        private static string FormatJson(string text)
        {
            try
            {
                // Simple JSON prettifier without external dependencies
                var sb = new StringBuilder();
                int indent = 0;
                bool inString = false;
                bool escaped = false;

                foreach (char c in text.Trim())
                {
                    if (escaped) { sb.Append(c); escaped = false; continue; }
                    if (c == '\\' && inString) { sb.Append(c); escaped = true; continue; }
                    if (c == '"') { inString = !inString; sb.Append(c); continue; }
                    if (inString) { sb.Append(c); continue; }

                    switch (c)
                    {
                        case '{': case '[':
                            sb.Append(c);
                            sb.AppendLine();
                            indent++;
                            sb.Append(new string(' ', indent * 2));
                            break;
                        case '}': case ']':
                            sb.AppendLine();
                            indent--;
                            sb.Append(new string(' ', indent * 2));
                            sb.Append(c);
                            break;
                        case ',':
                            sb.Append(c);
                            sb.AppendLine();
                            sb.Append(new string(' ', indent * 2));
                            break;
                        case ':':
                            sb.Append(": ");
                            break;
                        case ' ': case '\t': case '\n': case '\r':
                            break; // Skip whitespace outside strings
                        default:
                            sb.Append(c);
                            break;
                    }
                }
                return sb.ToString();
            }
            catch { return text; }
        }

        private static string FormatXml(string text)
        {
            try
            {
                // Simple XML indent
                var sb = new StringBuilder();
                int indent = 0;
                string[] tokens = Regex.Split(text, @"(<[^>]+>)").Where(s => !string.IsNullOrWhiteSpace(s)).ToArray();
                foreach (string token in tokens)
                {
                    string t = token.Trim();
                    if (t.StartsWith("</")) indent--;
                    sb.Append(new string(' ', Math.Max(0, indent) * 2));
                    sb.AppendLine(t);
                    if (t.StartsWith("<") && !t.StartsWith("</") && !t.StartsWith("<?") && !t.EndsWith("/>"))
                        indent++;
                }
                return sb.ToString().TrimEnd();
            }
            catch { return text; }
        }

        private static string FormatSql(string text)
        {
            try
            {
                // Uppercase keywords and add newlines
                string[] keywords = { "SELECT", "FROM", "WHERE", "AND", "OR", "JOIN", "LEFT JOIN",
                    "RIGHT JOIN", "INNER JOIN", "ON", "GROUP BY", "ORDER BY", "HAVING",
                    "INSERT INTO", "VALUES", "UPDATE", "SET", "DELETE FROM", "CREATE TABLE",
                    "ALTER TABLE", "DROP TABLE", "LIMIT", "OFFSET", "UNION" };

                string result = text;
                foreach (string kw in keywords)
                {
                    result = Regex.Replace(result, @"\b" + kw.Replace(" ", @"\s+") + @"\b",
                        "\n" + kw, RegexOptions.IgnoreCase);
                }
                return result.TrimStart('\n');
            }
            catch { return text; }
        }

        // === Text Transform ===

        private static readonly string[] TransformNames = { "UPPER", "lower", "Title", "camelCase", "snake_case", "kebab-case" };

        public static int TransformCount { get { return TransformNames.Length; } }

        public static string GetTransformName(int index)
        {
            return TransformNames[index % TransformNames.Length];
        }

        public static string ApplyTransform(string text, int transformIndex)
        {
            switch (transformIndex % TransformNames.Length)
            {
                case 0: return text.ToUpper();
                case 1: return text.ToLower();
                case 2: return ToTitleCase(text);
                case 3: return ToCamelCase(text);
                case 4: return ToSnakeCase(text);
                case 5: return ToKebabCase(text);
                default: return text;
            }
        }

        private static string ToTitleCase(string text)
        {
            return System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(text.ToLower());
        }

        private static string ToCamelCase(string text)
        {
            var words = Regex.Split(text, @"[\s_\-]+").Where(w => w.Length > 0).ToArray();
            if (words.Length == 0) return text;
            return words[0].ToLower() + string.Join("", words.Skip(1).Select(w =>
                char.ToUpper(w[0]) + (w.Length > 1 ? w.Substring(1).ToLower() : "")));
        }

        private static string ToSnakeCase(string text)
        {
            // Split on spaces, hyphens, or camelCase boundaries
            string spaced = Regex.Replace(text, @"([a-z])([A-Z])", "$1 $2");
            return Regex.Replace(spaced, @"[\s\-]+", "_").ToLower();
        }

        private static string ToKebabCase(string text)
        {
            string spaced = Regex.Replace(text, @"([a-z])([A-Z])", "$1 $2");
            return Regex.Replace(spaced, @"[\s_]+", "-").ToLower();
        }

        // === Prompt Snippets ===

        private static readonly string[][] Snippets =
        {
            new[] { "Senior Dev", "Act as a senior software engineer. Review the following and provide expert-level feedback with specific actionable improvements." },
            new[] { "PR Review", "Review this code change as a pull request reviewer. Check for bugs, performance issues, security concerns, and code style. Be constructive." },
            new[] { "Debug This", "You are a debugging expert. Analyze the following error/code and identify the root cause. Provide a step-by-step fix." },
            new[] { "Doc It", "Generate clear, concise documentation for the following code. Include purpose, parameters, return values, and usage examples." },
            new[] { "Test Cases", "Generate comprehensive unit test cases for the following code. Cover edge cases, error conditions, and happy paths." },
            new[] { "Optimize", "Analyze the following code for performance. Identify bottlenecks and suggest optimizations with before/after examples." },
        };

        public static int SnippetCount { get { return Snippets.Length; } }

        public static string GetSnippetName(int index)
        {
            return Snippets[index % Snippets.Length][0];
        }

        public static string GetSnippetText(int index)
        {
            return Snippets[index % Snippets.Length][1];
        }

        // === Escape/Encode ===

        private static readonly string[] EscapeNames = { "JSON Esc", "URL Enc", "Base64", "HTML Ent", "Unescape" };

        public static int EscapeCount { get { return EscapeNames.Length; } }

        public static string GetEscapeName(int index)
        {
            return EscapeNames[index % EscapeNames.Length];
        }

        public static string ApplyEscape(string text, int escapeIndex)
        {
            switch (escapeIndex % EscapeNames.Length)
            {
                case 0: // JSON Escape
                    return text.Replace("\\", "\\\\").Replace("\"", "\\\"")
                               .Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");
                case 1: // URL Encode
                    return Uri.EscapeDataString(text);
                case 2: // Base64
                    return Convert.ToBase64String(Encoding.UTF8.GetBytes(text));
                case 3: // HTML Entities
                    return System.Net.WebUtility.HtmlEncode(text);
                case 4: // Unescape (try all)
                    return UnescapeAll(text);
                default:
                    return text;
            }
        }

        private static string UnescapeAll(string text)
        {
            // Try Base64 decode first
            try
            {
                if (Regex.IsMatch(text.Trim(), @"^[A-Za-z0-9+/=]+$") && text.Trim().Length % 4 == 0)
                {
                    byte[] bytes = Convert.FromBase64String(text.Trim());
                    string decoded = Encoding.UTF8.GetString(bytes);
                    if (decoded.All(c => !char.IsControl(c) || c == '\n' || c == '\r' || c == '\t'))
                        return decoded;
                }
            }
            catch { }

            // Try URL decode
            string urlDecoded = Uri.UnescapeDataString(text);
            if (urlDecoded != text) return urlDecoded;

            // Try HTML decode
            string htmlDecoded = System.Net.WebUtility.HtmlDecode(text);
            if (htmlDecoded != text) return htmlDecoded;

            // Try JSON unescape
            return text.Replace("\\n", "\n").Replace("\\r", "\r").Replace("\\t", "\t")
                       .Replace("\\\"", "\"").Replace("\\\\", "\\");
        }

        // === Stats ===

        public static string GetStats(string text)
        {
            if (string.IsNullOrEmpty(text)) return "empty";
            int lines = text.Split('\n').Length;
            int chars = text.Length;
            return lines + " lines, " + chars + " chars";
        }
    }
}
