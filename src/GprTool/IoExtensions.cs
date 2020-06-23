using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DotNet.Globbing;

namespace GprTool
{
    public static class IoExtensions
    {
        public static IEnumerable<string> GetFilesByGlobPatterns(this string baseDirectory, string[] globPatterns, out string outGlobs)
        {
            globPatterns = globPatterns ?? throw new ArgumentNullException(nameof(globPatterns));

            var globList = new List<Glob>();

            var files = Enumerable.Empty<string>();
            foreach (var globPattern in globPatterns)
            {
                files = files.Concat(GetFilesByGlobPattern(baseDirectory, globPattern, out Glob outGlob));
                globList.Add(outGlob);
            }

            outGlobs = string.Join(' ', globList);
            return files;
        }

        public static IEnumerable<string> GetFilesByGlobPattern(this string baseDirectory, string globPattern, out Glob outGlob)
        {
            globPattern = globPattern ?? throw new ArgumentNullException(nameof(globPattern));

            var baseDirectoryGlobPattern = Path.GetFullPath(Path.Combine(baseDirectory, globPattern.Trim()));
            var fileNames = new List<string>();

            if (Directory.Exists(baseDirectoryGlobPattern))
            {
                globPattern = Path.GetFullPath(Path.Combine(baseDirectoryGlobPattern, "*.*"));
            } else if (File.Exists(baseDirectoryGlobPattern))
            {
                globPattern = Path.GetFullPath(baseDirectoryGlobPattern);
            }

            var glob = Path.IsPathRooted(globPattern)
                ? Glob.Parse(globPattern)
                : Glob.Parse(baseDirectoryGlobPattern);

            var basePathFromGlob = Path.GetDirectoryName(glob.BuildBasePathFromGlob(baseDirectory));

            outGlob = glob;

            return Directory
                .GetFiles(basePathFromGlob, "*.*", SearchOption.AllDirectories)
                .Where(filename =>
                    filename.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase)
                    || filename.EndsWith(".snupkg", StringComparison.OrdinalIgnoreCase))
                .Where(filename => fileNames.Contains(filename, StringComparer.Ordinal) || glob.IsMatch(filename));
        }

        public static FileStream OpenReadShared(this string filename)
        {
            return File.Open(filename, FileMode.Open, FileAccess.Read, FileShare.Read);
        }

        public static MemoryStream ReadSharedToStream(this string filename)
        {
            using var fileStream = filename.OpenReadShared();
            var outputStream = new MemoryStream();
            fileStream.CopyTo(outputStream);
            outputStream.Seek(0, SeekOrigin.Begin);
            return outputStream;
        }
    }
}
