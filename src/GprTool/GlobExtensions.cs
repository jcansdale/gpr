using System;
using System.IO;
using System.Linq;

namespace GprTool
{
    public static class GlobExtensions
    {
        public static bool IsGlobPattern(this Glob glob)
        {
            if (glob == null) throw new ArgumentNullException(nameof(glob));
            return Glob.ContainsPattern(glob.Pattern);
        }

        public static string BuildBasePathFromGlob(this Glob glob, string baseDirectory)
        {
            if (glob == null) throw new ArgumentNullException(nameof(glob));
            if (baseDirectory == null) throw new ArgumentNullException(nameof(baseDirectory));

            var normalizedPattern = Glob.Normalize(glob.Pattern);
            var normalizedBaseDirectory = Glob.Normalize(Path.GetFullPath(baseDirectory));
            var root = Path.GetPathRoot(glob.Pattern)?.Replace('\\', '/') ?? string.Empty;
            var relativePattern = root.Length > 0 && normalizedPattern.StartsWith(root, StringComparison.OrdinalIgnoreCase)
                ? normalizedPattern.Substring(root.Length)
                : normalizedPattern;

            var segments = relativePattern
                .Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries)
                .ToList();

            var matchedSegments = segments
                .TakeWhile(segment => !Glob.ContainsPattern(segment))
                .ToList();

            if (!matchedSegments.Any())
            {
                return root.Length > 0
                    ? Path.GetFullPath(root.Replace('/', Path.DirectorySeparatorChar))
                    : Path.GetFullPath(baseDirectory);
            }

            var joined = string.Join(Path.DirectorySeparatorChar.ToString(), matchedSegments);
            if (root.Length > 0)
            {
                return Path.GetFullPath(Path.Combine(root.Replace('/', Path.DirectorySeparatorChar), joined));
            }

            return Path.GetFullPath(Path.Combine(normalizedBaseDirectory.Replace('/', Path.DirectorySeparatorChar), joined));
        }
    }
}
