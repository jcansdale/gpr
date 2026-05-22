using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace GprTool
{
    public sealed class Glob
    {
        readonly Regex _regex;

        Glob(string pattern)
        {
            Pattern = Normalize(pattern ?? throw new ArgumentNullException(nameof(pattern)));
            _regex = new Regex(BuildRegex(Pattern), RegexOptions.CultureInvariant | GetRegexOptions());
        }

        public string Pattern { get; }

        public static Glob Parse(string pattern) => new Glob(pattern);

        public bool IsMatch(string filename)
        {
            if (filename == null) throw new ArgumentNullException(nameof(filename));
            return _regex.IsMatch(Normalize(filename));
        }

        public override string ToString() => Pattern;

        internal static string Normalize(string value) => value.Replace('\\', '/');

        internal static bool ContainsPattern(string segment) =>
            segment.IndexOfAny(new[] { '*', '?', '[' }) >= 0;

        static RegexOptions GetRegexOptions()
        {
            return RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? RegexOptions.IgnoreCase : RegexOptions.None;
        }

        static string BuildRegex(string pattern)
        {
            var builder = new StringBuilder("^");

            for (var index = 0; index < pattern.Length; index++)
            {
                var current = pattern[index];

                if (current == '*')
                {
                    var isDoubleStar = index + 1 < pattern.Length && pattern[index + 1] == '*';
                    if (isDoubleStar)
                    {
                        builder.Append(".*");
                        index++;
                    }
                    else
                    {
                        builder.Append("[^/]*");
                    }

                    continue;
                }

                if (current == '?')
                {
                    builder.Append("[^/]");
                    continue;
                }

                if (current == '[')
                {
                    var endIndex = pattern.IndexOf(']', index + 1);
                    if (endIndex > index)
                    {
                        var content = pattern.Substring(index + 1, endIndex - index - 1);
                        if (content.StartsWith("!"))
                        {
                            content = "^" + Regex.Escape(content.Substring(1));
                        }
                        else if (content.StartsWith("^"))
                        {
                            content = "\\" + content;
                        }
                        else
                        {
                            content = Regex.Escape(content);
                        }

                        builder.Append('[');
                        builder.Append(content);
                        builder.Append(']');
                        index = endIndex;
                        continue;
                    }
                }

                builder.Append(Regex.Escape(current.ToString()));
            }

            builder.Append("$");
            return builder.ToString();
        }
    }
}
