using System;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace GprTool.Tests
{
    [TestFixture]
    class IoExtensionsTests
    {
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
            if (pattern[0] == '/')
            {
                pattern = pattern.Substring(1);
            }
            Assert.That(pattern, Is.EqualTo(expectedGlobPattern));
            Assert.That(packages.Count, Is.EqualTo(expectedFilesCount));
        }

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
            if (pattern[0] == '/')
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
            if (globPattern[0] == '/')
            {
                globPattern = globPattern.Substring(1);
            }

            Assert.That(globPattern, Is.EqualTo(globPattern));
            Assert.That(packages.Count, Is.EqualTo(2));
            Assert.That(packages[0], Is.EqualTo(nupkgAbsoluteFilename));
            Assert.That(packages[1], Is.EqualTo(snupkgAbsoluteFilename));
        }

        [Test]
        public void GetFilesByGlobPattern_Is_Relative_Filename()
        {
            using var tmpDirectory = new DisposableDirectory(Path.Combine(Directory.GetCurrentDirectory(), Guid.NewGuid().ToString("N")));

            var nupkgAbsoluteFilename = Path.Combine(tmpDirectory, "test.nupkg");
            var snupkgAbsoluteFilename = Path.Combine(tmpDirectory, "test.snupkg");

            File.WriteAllText(nupkgAbsoluteFilename, string.Empty);
            File.WriteAllText(snupkgAbsoluteFilename, string.Empty);

            var packages = tmpDirectory.WorkingDirectory.GetFilesByGlobPattern("test.nupkg", out var glob).ToList();

            var globPattern = glob.ToString().Substring(tmpDirectory.WorkingDirectory.Length).Replace("\\", "/");
            if (globPattern[0] == '/')
            {
                globPattern = globPattern.Substring(1);
            }

            Assert.That(globPattern, Is.EqualTo("test.nupkg"));
            Assert.That(packages.Count, Is.EqualTo(1));
            Assert.That(packages[0], Is.EqualTo(nupkgAbsoluteFilename));
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
            if (globPattern[0] == '/')
            {
                globPattern = globPattern.Substring(1);
            }

            Assert.That(globPattern, Is.EqualTo("test.nupkg"));
            Assert.That(packages.Count, Is.EqualTo(1));
            Assert.That(packages[0], Is.EqualTo(nupkgAbsoluteFilename));
        }
    }
}
