﻿using DotNet.Globbing;
using GprTool;
using NUnit.Framework;

[TestFixture]
class GlobExtensionTests
{
    [TestCase("c:\\test.nupkg", false)]
    [TestCase("c:\\test", false)]
    [TestCase("c:\\test\\**", true)]
    [TestCase("c:\\test?\\**", true)]
    [TestCase("c:\\test?\\[abc]\\**", true)]
    [TestCase("c:\\test\\**\\*.nupkg", true)]
    public void IsGlobPattern(string path, bool isGlobPattern)
    {
        var glob = Glob.Parse(path);
        Assert.That(glob.IsGlobPattern(), Is.EqualTo(isGlobPattern));
    }

    [TestCase("c:\\test.nupkg", "c:\\test.nupkg")]
    [TestCase("c:\\test", "c:\\test")]
    [TestCase("c:\\test?\\**", "c:\\test")]
    [TestCase("c:\\test?\\[abc]\\**", "c:\\test")]
    [TestCase("c:\\test\\**", "c:\\test")]
    [TestCase("c:\\test\\**\\*.nupkg", "c:\\test")]
    [TestCase("c:\\test\\subdirectory\\**\\*.nupkg", "c:\\test\\subdirectory")]
    [TestCase("c:\\test\\**\\subdirectory\\**\\*.nupkg", "c:\\test")]
    public void BuildBasePathFromGlob(string path, string expectedBaseDirectory)
    {
        var glob = Glob.Parse(path);
        Assert.That(glob.BuildBasePathFromGlob(), Is.EqualTo(expectedBaseDirectory));
    }
}