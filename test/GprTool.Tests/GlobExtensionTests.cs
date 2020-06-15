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

    [TestCase("./packages/**/*.nupkg", "c:\\test\\packages")]
    [TestCase(".\\packages/**/*.nupkg", "c:\\test\\packages")]
    [TestCase("packages", "c:\\test\\packages")]
    [TestCase("c:\\test.nupkg", "c:\\test.nupkg")]
    [TestCase("c:\\test", "c:\\test")]
    [TestCase("c:\\test?", "c:\\test")]
    [TestCase("c:\\test?\\**", "c:\\test")]
    [TestCase("c:\\test?\\[abc]\\**", "c:\\test")]
    [TestCase("c:\\test\\*.*", "c:\\test")]
    [TestCase("c:\\test\\**", "c:\\test")]
    [TestCase("c:\\test\\**\\*.nupkg", "c:\\test")]
    [TestCase("c:\\test\\subdirectory\\**\\*.nupkg", "c:\\test\\subdirectory")]
    [TestCase("c:\\test\\**\\subdirectory\\**\\*.nupkg", "c:\\test")]
    public void BuildBasePathFromGlob(string path, string expectedBaseDirectory)
    {
        var glob = Glob.Parse(path);
        Assert.That(glob.BuildBasePathFromGlob("c:\\test"), Is.EqualTo(expectedBaseDirectory));
    }

    [TestCase("packages/**/*.nupkg", "c:\\test", "c:\\test\\packages")]
    [TestCase("packages\\**\\*.nupkg", "c:\\test", "c:\\test\\packages")]
    [TestCase("packages", "c:\\test", "c:\\test\\packages")]
    public void BuildBasePathFromGlob_Uses_BaseDirectory_When_Path_Is_Not_Rooted(string relativePath, string baseDirectory, string expectedPath)
    {
        var glob = Glob.Parse(relativePath);
        Assert.That(glob.BuildBasePathFromGlob(baseDirectory), Is.EqualTo(expectedPath));
    }
}