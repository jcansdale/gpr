using DotNet.Globbing;
using GprTool;
using NUnit.Framework;

[TestFixture]
class GlobExtensionTests
{
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
}