using DotNet.Globbing;
using NUnit.Framework;

namespace GprTool.Tests
{
    [TestFixture]
    public class GlobExtensionTests
    {
#if PLATFORM_WINDOWS
        [TestCase("c:\\test.nupkg", false)]
        [TestCase("c:\\test", false)]
        [TestCase("packages", false)]
        [TestCase("./packages", false)]
        [TestCase(".\\packages", false)]
        [TestCase("packages/**/*.nupkg", true)]
        [TestCase("packages\\**\\*.nupkg", true)]
        [TestCase("c:\\test?", true)]
        [TestCase("c:\\test?\\**", true)]
        [TestCase("c:\\test?\\[abc]\\**", true)]
        [TestCase("c:\\test\\**", true)]
        [TestCase("c:\\test\\**\\*.nupkg", true)]
        [TestCase("c:\\test\\*.*", true)]
        public void IsGlobPattern(string path, bool isGlobPattern)
        {
            var glob = Glob.Parse(path);
            Assert.That(glob.IsGlobPattern(), Is.EqualTo(isGlobPattern));
        }

        [TestCase(".", "c:\\", "c:\\", Description = "Relative path is directory")]
        [TestCase("packages", "c:\\", "c:\\packages", Description = "Relative path is directory")]
        [TestCase("test.nupkg", "c:\\", "c:\\test.nupkg", Description = "Relative path is filename")]
        [TestCase("packages\\**\\*.nupkg",  "c:\\",  "c:\\packages", Description = "Relative path")]
        [TestCase("packages/**/*.nupkg",  "c:\\", "c:\\packages", Description = "Relative path")]
        [TestCase("./packages/**/*.nupkg", "c:\\",   "c:\\packages", Description = "Relative path")]
        [TestCase(".\\packages/**/*.nupkg",  "c:\\", "c:\\packages", Description = "Relative path")]
        [TestCase("c:\\test?", "c:\\", "c:\\")]
        [TestCase("c:\\test?\\**", "c:\\",  "c:\\")]
        [TestCase("c:\\test?\\[abc]\\**", "c:\\",  "c:\\")]
        [TestCase("c:\\test\\*.*", "c:\\",  "c:\\test")]
        [TestCase("c:\\test\\**", "c:\\",  "c:\\test")]
        [TestCase("c:\\test\\**\\*.nupkg", "c:\\", "c:\\test")]
        [TestCase("c:\\test\\subdirectory\\**\\*.nupkg",  "c:\\", "c:\\test\\subdirectory")]
        [TestCase("c:\\test\\**\\subdirectory\\**\\*.nupkg",  "c:\\", "c:\\test")]
        public void BuildBasePathFromGlob(string path, string baseDirectory, string expectedBaseDirectory)
        {
            var glob = Glob.Parse(path);
            Assert.That(glob.BuildBasePathFromGlob(baseDirectory), Is.EqualTo(expectedBaseDirectory));
        }
#elif PLATFORM_UNIX
        [TestCase("/mnt/c/test.nupkg", false)]
        [TestCase("/mnt/c/test", false)]
        [TestCase("packages", false)]
        [TestCase("./packages", false)]
        [TestCase(".\\packages", false)]
        [TestCase("packages/**/*.nupkg", true)]
        [TestCase("packages//**/*.nupkg", true)]
        [TestCase("/mnt/c/test?", true)]
        [TestCase("/mnt/c/test?/**", true)]
        [TestCase("/mnt/c/test?/[abc]/**", true)]
        [TestCase("/mnt/c/test/**", true)]
        [TestCase("/mnt/c/test/**/*.nupkg", true)]
        [TestCase("/mnt/c/test/*.*", true)]
        public void IsGlobPattern(string path, bool isGlobPattern)
        {
            var glob = Glob.Parse(path);
            Assert.That(glob.IsGlobPattern(), Is.EqualTo(isGlobPattern));
        }

        [TestCase(".", "/mnt/c", "/mnt/c", Description = "Relative path is directory")]
        [TestCase("packages", "/mnt/c", "/mnt/c/packages", Description = "Relative path is directory")]
        [TestCase("test.nupkg", "/mnt/c", "/mnt/c/test.nupkg", Description = "Relative path is filename")]
        [TestCase("packages/**/*.nupkg",  "/mnt/c",  "/mnt/c/packages", Description = "Relative path")]
        [TestCase("packages/**/*.nupkg",  "/mnt/c", "/mnt/c/packages", Description = "Relative path")]
        [TestCase("./packages/**/*.nupkg", "/mnt/c",   "/mnt/c/packages", Description = "Relative path")]
        [TestCase(".\\packages/**/*.nupkg",  "/mnt/c", "/mnt/c/packages", Description = "Relative path")]
        [TestCase("/mnt/c/test?", "/mnt/c", "/mnt/c")]
        [TestCase("/mnt/c/test?/**", "/mnt/c",  "/mnt/c")]
        [TestCase("/mnt/c/test?/[abc]/**", "/mnt/c",  "/mnt/c")]
        [TestCase("/mnt/c/test/*.*", "/mnt/c",  "/mnt/c/test")]
        [TestCase("/mnt/c/test/**", "/mnt/c",  "/mnt/c/test")]
        [TestCase("/mnt/c/test/**/*.nupkg", "/mnt/c", "/mnt/c/test")]
        [TestCase("/mnt/c/test/subdirectory/**/*.nupkg",  "/mnt/c", "/mnt/c/test/subdirectory")]
        [TestCase("/mnt/c/test/**/subdirectory/**/*.nupkg",  "/mnt/c", "/mnt/c/test")]
        public void BuildBasePathFromGlob(string path, string baseDirectory, string expectedBaseDirectory)
        {
            var glob = Glob.Parse(path);
            Assert.That(glob.BuildBasePathFromGlob(baseDirectory), Is.EqualTo(expectedBaseDirectory));
        }
#endif
    }
}