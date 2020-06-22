using System;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace GprTool.Tests
{
    [TestFixture]
    public class IoExtensionsTests
    {
        [TestCase("foo.nupkg", "foo.nupkg", "foo.nupkg")]
        [TestCase("*.nupkg", "foo.nupkg;bar.nupkg", "foo.nupkg;bar.nupkg")]
        [TestCase("*.nupkg", "foo bar.nupkg", "foo bar.nupkg")]
        [TestCase("dir/foo.nupkg", "dir/foo.nupkg", "dir/foo.nupkg")]
        [TestCase("foo bar/baz.nupkg", "foo bar/baz.nupkg", "foo bar/baz.nupkg")]
        [TestCase("foo bar/baz.*", "foo bar/baz.nupkg", "foo bar/baz.nupkg")]
        public void GetFilesByGlobPattern(string globPattern, string files, string expectedFiles)
        {
            using var tmpDirectory = new DisposableDirectory(Path.Combine(Directory.GetCurrentDirectory(), Guid.NewGuid().ToString("N")));
            var paths = files.Split(';').Select(f => Path.Combine(tmpDirectory.WorkingDirectory, f).Replace('/', Path.DirectorySeparatorChar));
            foreach (var path in paths)
            {
                var dir = Path.GetDirectoryName(path);
                Directory.CreateDirectory(dir);
                File.WriteAllText(path, string.Empty);
            }

            var packages = tmpDirectory.WorkingDirectory.GetFilesByGlobPattern(globPattern, out var glob).ToList();

            var expectedPaths = expectedFiles.Split(';').Select(f => Path.Combine(tmpDirectory.WorkingDirectory, f).Replace('/', Path.DirectorySeparatorChar));
            foreach (var expectFile in expectedPaths)
            {
                Assert.Contains(expectFile, packages);
            }
            Assert.That(packages.Count(), Is.EqualTo(expectedPaths.Count()));
        }

        [TestCase("./nupkg", "*.*", 2)]
        [TestCase("./nupkg/**/*.*", "**/*.*", 2)]
        [TestCase("./nupkg/**/**", "**/**", 2)]
        [TestCase("./nupkg/**/*.nupkg", "**/*.nupkg", 1)]
        #if PLATFORM_WINDOWS
        [TestCase(".\\nupkg", "*.*", 2)]
        [TestCase(".\\nupkg\\**\\*.*", "**/*.*", 2)]
        [TestCase(".\\nupkg\\**\\**", "**/**", 2)]
        [TestCase(".\\nupkg\\**\\*.nupkg", "**/*.nupkg", 1)]
        #endif
        [TestCase("nupkg", "*.*", 2)]
        [TestCase("nupkg/**/*.nupkg", "**/*.nupkg", 1)]
        [TestCase("nupkg/**/*.snupkg", "**/*.snupkg", 1)]
        [TestCase("nupkg/**/*.*", "**/*.*", 2)]
        [TestCase("nupkg/**/**", "**/**", 2)]
        public void GetFilesByGlobPattern_Is_Relative_Directory(string globPattern, string expectedGlobPattern, int expectedFilesCount)
        {
            using var tmpDirectory = new DisposableDirectory(Path.Combine(Directory.GetCurrentDirectory(), Guid.NewGuid().ToString("N")));

            var nupkgDirectory = Path.Combine(tmpDirectory, "nupkg");
            Directory.CreateDirectory(nupkgDirectory);

            var nupkgAbsoluteFilename = Path.Combine(nupkgDirectory, "test.nupkg");
            var snupkgAbsoluteFilename = Path.Combine(nupkgDirectory, "test.snupkg");

            File.WriteAllText(nupkgAbsoluteFilename, string.Empty);
            File.WriteAllText(snupkgAbsoluteFilename, string.Empty);

            var packages = tmpDirectory.WorkingDirectory.GetFilesByGlobPattern(globPattern, out var glob).ToList();
            var pattern = glob.ToString().Substring(nupkgDirectory.Length).Replace("\\", "/");
            if (pattern[0] == '/' || pattern[0] == '\\')
            {
                pattern = pattern.Substring(1);
            }
            Assert.That(pattern, Is.EqualTo(expectedGlobPattern));
            Assert.That(packages.Count, Is.EqualTo(expectedFilesCount));
        }

        [TestCase("./nupkg", "*.*", 2)]
        #if PLATFORM_WINDOWS
        [TestCase(".\\nupkg", "*.*", 2)]
        #endif
        [TestCase("nupkg", "*.*", 2)]
        [TestCase("nupkg/**/*.nupkg", "**/*.nupkg", 1)]
        [TestCase("nupkg/**/*.snupkg", "**/*.snupkg", 1)]
        [TestCase("nupkg/**/*.*", "**/*.*", 2)]
        [TestCase("nupkg/**/**", "**/**", 2)]
        public void GetFilesByGlobPattern_Is_FullPath_Directory(string globPattern, string expectedGlobPattern, int expectedFilesCount)
        {
            using var tmpDirectory = new DisposableDirectory(Path.Combine(Directory.GetCurrentDirectory(), Guid.NewGuid().ToString("N")));

            var nupkgDirectory = Path.Combine(tmpDirectory, "nupkg");
            Directory.CreateDirectory(nupkgDirectory);

            var nupkgAbsoluteFilename = Path.Combine(nupkgDirectory, "test.nupkg");
            var snupkgAbsoluteFilename = Path.Combine(nupkgDirectory, "test.snupkg");

            File.WriteAllText(nupkgAbsoluteFilename, string.Empty);
            File.WriteAllText(snupkgAbsoluteFilename, string.Empty);

            var packages = tmpDirectory.WorkingDirectory.GetFilesByGlobPattern(Path.Combine(tmpDirectory, globPattern), out var glob).ToList();
            var pattern = glob.ToString().Substring(nupkgDirectory.Length).Replace("\\", "/");
            if (pattern[0] == '/' || pattern[0] == '\\')
            {
                pattern = pattern.Substring(1);
            }
            Assert.That(pattern, Is.EqualTo(expectedGlobPattern));
            Assert.That(packages.Count, Is.EqualTo(expectedFilesCount));
        }

        [Test]
        public void GetFilesByGlobPattern_Is_Dot_Directory()
        {
            using var tmpDirectory = new DisposableDirectory(Path.Combine(Directory.GetCurrentDirectory(), Guid.NewGuid().ToString("N")));

            var nupkgAbsoluteFilename = Path.Combine(tmpDirectory, "test.nupkg");
            var snupkgAbsoluteFilename = Path.Combine(tmpDirectory, "test.snupkg");
            File.WriteAllText(nupkgAbsoluteFilename, string.Empty);
            File.WriteAllText(snupkgAbsoluteFilename, string.Empty);

            var packages = tmpDirectory.WorkingDirectory.GetFilesByGlobPattern(".", out var glob).ToList();
            
            var globPattern = glob.ToString().Substring(tmpDirectory.WorkingDirectory.Length).Replace("\\", "/");
            if (globPattern[0] == '/' || globPattern[0] == '\\')
            {
                globPattern = globPattern.Substring(1);
            }

            Assert.That(globPattern, Is.EqualTo(globPattern));
            Assert.That(packages.Count, Is.EqualTo(2));
            Assert.That(packages, Does.Contain(nupkgAbsoluteFilename));
            Assert.That(packages, Does.Contain(snupkgAbsoluteFilename));
        }

        [TestCase("test.nupkg")]
        [TestCase("./test.nupkg")]
        [TestCase("test.nupkg")]
#if PLATFORM_WINDOWS
        [TestCase(".\\test.nupkg")]
        #endif
        public void GetFilesByGlobPattern_Is_Relative_Filename(string relativeFilename)
        {
            using var tmpDirectory = new DisposableDirectory(Path.Combine(Directory.GetCurrentDirectory(), Guid.NewGuid().ToString("N")));

            var nupkgAbsoluteFilename = Path.Combine(tmpDirectory, "test.nupkg");
            var snupkgAbsoluteFilename = Path.Combine(tmpDirectory, "test.snupkg");

            File.WriteAllText(nupkgAbsoluteFilename, string.Empty);
            File.WriteAllText(snupkgAbsoluteFilename, string.Empty);

            var packages = tmpDirectory.WorkingDirectory.GetFilesByGlobPattern(relativeFilename, out var glob).ToList();

            var globPattern = glob.ToString().Substring(tmpDirectory.WorkingDirectory.Length).Replace("\\", "/");
            if (globPattern[0] == '/' || globPattern[0] == '\\')
            {
                globPattern = globPattern.Substring(1);
            }

            Assert.That(globPattern, Is.EqualTo("test.nupkg"));
            Assert.That(packages.Count, Is.EqualTo(1));
            Assert.That(packages, Does.Contain(nupkgAbsoluteFilename));
        }

        [TestCase("test.nupkg;test.snupkg")]
        [TestCase("./test.nupkg;./test.snupkg")]
        [TestCase("./test.nupkg;              ./test.snupkg", Description = "Whitespace")]
#if PLATFORM_WINDOWS
        [TestCase(".\\test.nupkg;.\\test.snupkg")]
#endif
        public void GetFilesByGlobPatterns_Is_Multiple_Relative_Filenames(string relativeFilenames)
        {
            using var tmpDirectory = new DisposableDirectory(Path.Combine(Directory.GetCurrentDirectory(), Guid.NewGuid().ToString("N")));

            var nupkgAbsoluteFilename = Path.Combine(tmpDirectory, "test.nupkg");
            var snupkgAbsoluteFilename = Path.Combine(tmpDirectory, "test.snupkg");
            var bogusNupkgAbsoluteFilename = Path.Combine(tmpDirectory, "testbogus.snupkg");

            File.WriteAllText(nupkgAbsoluteFilename, string.Empty);
            File.WriteAllText(snupkgAbsoluteFilename, string.Empty);
            File.WriteAllText(bogusNupkgAbsoluteFilename, string.Empty);

            var globPatterns = relativeFilenames.Split(';');
            var packages = tmpDirectory.WorkingDirectory.GetFilesByGlobPattern(globPatterns, out var globs).ToList();

            Assert.That(globs, Does.Contain(' '));
            Assert.That(packages.Count, Is.EqualTo(2));
            Assert.That(packages, Does.Contain(nupkgAbsoluteFilename));
            Assert.That(packages, Does.Contain(snupkgAbsoluteFilename));
        }

        [Test]
        public void GetFilesByGlobPattern_Is_FullPath_Filename()
        {
            using var tmpDirectory = new DisposableDirectory(Path.Combine(Directory.GetCurrentDirectory(), Guid.NewGuid().ToString("N")));

            var nupkgAbsoluteFilename = Path.Combine(tmpDirectory, "test.nupkg");
            var snupkgAbsoluteFilename = Path.Combine(tmpDirectory, "test.snupkg");

            File.WriteAllText(nupkgAbsoluteFilename, string.Empty);
            File.WriteAllText(snupkgAbsoluteFilename, string.Empty);

            var packages = tmpDirectory.WorkingDirectory.GetFilesByGlobPattern(nupkgAbsoluteFilename, out var glob).ToList();

            var globPattern = glob.ToString().Substring(tmpDirectory.WorkingDirectory.Length).Replace("\\", "/");
            if (globPattern[0] == '/' || globPattern[0] == '\\')
            {
                globPattern = globPattern.Substring(1);
            }

            Assert.That(globPattern, Is.EqualTo("test.nupkg"));
            Assert.That(packages.Count, Is.EqualTo(1));
            Assert.That(packages, Does.Contain(nupkgAbsoluteFilename));
        }

        [Test]
        public void GetFilesByGlobPatterns_Is_Multiple_FullPath_Filenames()
        {
            using var tmpDirectory = new DisposableDirectory(Path.Combine(Directory.GetCurrentDirectory(), Guid.NewGuid().ToString("N")));

            var nupkgAbsoluteFilename = Path.Combine(tmpDirectory, "test.nupkg");
            var snupkgAbsoluteFilename = Path.Combine(tmpDirectory, "test.snupkg");
            var bogusNupkgAbsoluteFilename = Path.Combine(tmpDirectory, "testbogus.snupkg");

            File.WriteAllText(nupkgAbsoluteFilename, string.Empty);
            File.WriteAllText(snupkgAbsoluteFilename, string.Empty);
            File.WriteAllText(bogusNupkgAbsoluteFilename, string.Empty);

            var packages = tmpDirectory.WorkingDirectory.GetFilesByGlobPattern(new[] { nupkgAbsoluteFilename, snupkgAbsoluteFilename }, out var glob).ToList();

            Assert.That(glob.ToString(), Is.EqualTo($"{nupkgAbsoluteFilename} {snupkgAbsoluteFilename}"));

            Assert.That(packages.Count, Is.EqualTo(2));
            Assert.That(packages, Does.Contain(nupkgAbsoluteFilename));
            Assert.That(packages, Does.Contain(snupkgAbsoluteFilename));
        }
    }
}
