using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DotNet.Globbing;

namespace GprTool
{
    public static class IoExtensions
    {
        public static IEnumerable<string> GetFilesByGlobPattern(this string baseDirectory, string globPattern, out Glob outGlob)
        {
            var baseDirectoryGlobPattern = Path.GetFullPath(Path.Combine(baseDirectory, globPattern));
        
            if (string.Equals(".", globPattern))
            {
                globPattern = Path.GetFullPath(Path.Combine(baseDirectory, "*.*"));
            } else if (Directory.Exists(baseDirectoryGlobPattern))
            {
                globPattern = Path.GetFullPath(Path.Combine(baseDirectoryGlobPattern, "*.*"));
            } else if (File.Exists(baseDirectoryGlobPattern))
            {
                globPattern = Path.GetFullPath(baseDirectoryGlobPattern);
            }

            var basePathFromGlob = Path.GetDirectoryName(Glob.Parse(globPattern).BuildBasePathFromGlob(baseDirectory));

            var glob = Path.IsPathRooted(globPattern)
                ? Glob.Parse(globPattern)
                : Glob.Parse(baseDirectoryGlobPattern);

            outGlob = glob;

            return Directory
                .GetFiles(basePathFromGlob, "*.*", SearchOption.AllDirectories)
                .Where(filename =>
                    filename.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase)
                    || filename.EndsWith(".snupkg", StringComparison.OrdinalIgnoreCase))
                .Where(filename => glob.IsMatch(filename));
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
