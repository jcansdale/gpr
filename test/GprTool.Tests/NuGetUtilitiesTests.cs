using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using NUnit.Framework;

namespace GprTool.Tests
{
    [SuppressMessage("ReSharper", "MemberCanBePrivate.Local")]
    public class NuGetUtilitiesTests
    {
        public class TheSetApiKeyMethod
        {
            public string TmpDirectoryPath => Path.Combine(Directory.GetCurrentDirectory(), Guid.NewGuid().ToString("N"));

            [Test]
            public void AddClearTextPassword()
            {
                var xmlDoc = new XmlDocument();
                const string source = "SOURCE";
                const string expectedToken = "TOKEN";

                NuGetUtilities.SetApiKey(xmlDoc, expectedToken, source);

                var passwordXpath = $"/configuration/packageSourceCredentials/{source}/add[@key='ClearTextPassword']/@value";
                var token = xmlDoc.SelectSingleNode(passwordXpath)?.Value;
                Assert.That(token, Is.EqualTo(expectedToken));
            }

            [Test]
            public void AddUsername()
            {
                var xmlDoc = new XmlDocument();
                const string source = "SOURCE";

                NuGetUtilities.SetApiKey(xmlDoc, "TOKEN", source);

                var usernameXpath = $"/configuration/packageSourceCredentials/{source}/add[@key='Username']/@value";
                var username = xmlDoc.SelectSingleNode(usernameXpath)?.Value;
                Assert.That(username, Is.EqualTo("PersonalAccessToken"));
            }

            [Test]
            public void PreserveExistingPackageSources()
            {
                const string xml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<configuration>
    <packageSources>
        <clear />
    </packageSources>
</configuration>";
                var xmlDoc = new XmlDocument();
                xmlDoc.LoadXml(xml);

                NuGetUtilities.SetApiKey(xmlDoc, "TOKEN", "SOURCE");

                var element = xmlDoc.SelectSingleNode("/configuration/packageSources/clear");
                Assert.That(element, Is.Not.Null);
            }

            [Test]
            public void PreserveExistingPackageSourceCredentials()
            {
                const string existingSource = "EXISTING_SOURCE";
                const string newSource = "NEW_SOURCE";
                var xml =
                    $@"<?xml version=""1.0"" encoding=""utf-8""?>
<configuration>
  <packageSourceCredentials>
    <{existingSource}>
      <add key=""Username"" value=""USERNAME"" />
      <add key=""ClearTextPassword"" value=""TOKEN"" />
    </{existingSource}>
  </packageSourceCredentials>
</configuration>";
                var xmlDoc = new XmlDocument();
                xmlDoc.LoadXml(xml);

                NuGetUtilities.SetApiKey(xmlDoc, "TOKEN", newSource);

                Assert.That(xmlDoc.SelectSingleNode($"/configuration/packageSourceCredentials/{existingSource}"), Is.Not.Null);
                Assert.That(xmlDoc.SelectSingleNode($"/configuration/packageSourceCredentials/{newSource}"), Is.Not.Null);
                Assert.That(xmlDoc.SelectNodes("/configuration/packageSourceCredentials")?.Count, Is.EqualTo(1));
            }

            [Test]
            public void UpdatePackageSourceCredentials()
            {
                const string existingSource = "EXISTING_SOURCE";
                var xml =
                    $@"<?xml version=""1.0"" encoding=""utf-8""?>
<configuration>
  <packageSourceCredentials>
    <{existingSource}>
      <add key=""Username"" value=""USERNAME"" />
      <add key=""ClearTextPassword"" value=""OLD_TOKEN"" />
    </{existingSource}>
  </packageSourceCredentials>
</configuration>";
                var xmlDoc = new XmlDocument();
                xmlDoc.LoadXml(xml);

                NuGetUtilities.SetApiKey(xmlDoc, "NEW_TOKEN", existingSource);

                var passwordAttribute = xmlDoc.SelectSingleNode($"/configuration/packageSourceCredentials/{existingSource}/add[@key='ClearTextPassword']/@value");
                Assert.That(passwordAttribute?.Value, Is.EqualTo("NEW_TOKEN"));
                Assert.That(xmlDoc.SelectNodes($"/configuration/packageSourceCredentials/{existingSource}/add")?.Count, Is.EqualTo(2));
                Assert.That(xmlDoc.SelectNodes("/configuration/packageSourceCredentials/*")?.Count, Is.EqualTo(1));
            }

            [TestCase(null, null, null, null)]
            [TestCase("jcansdale/gpr", "jcansdale", "gpr", "https://github.com/jcansdale/gpr")]
            [TestCase("jcansdale//gpr", "jcansdale", "gpr", "https://github.com/jcansdale/gpr")]
            [TestCase("/jcansdale/gpr", "jcansdale", "gpr", "https://github.com/jcansdale/gpr")]
            [TestCase("http://github.com/jcansdale/gpr", "jcansdale", "gpr", "https://github.com/jcansdale/gpr")]
            [TestCase("https://github.com/jcansdale/gpr", "jcansdale", "gpr", "https://github.com/jcansdale/gpr")]
            [TestCase("https://github.com/jcansdale\\gpr", "jcansdale", "gpr", "https://github.com/jcansdale/gpr")]
            [TestCase("https://github.com/jcansdale///////gpr", "jcansdale", "gpr", "https://github.com/jcansdale/gpr")]
            [TestCase("  https://github.com/jcansdale/gpr ", "jcansdale", "gpr", "https://github.com/jcansdale/gpr", Description = "Whitespace")]
            public void BuildPackageFile(string repositoryUrl, string expectedOwner, string expectedRepositoryName, string expectedGithubRepositoryUrl)
            {
                using var packageBuilderContext = new PackageBuilderContext(TmpDirectoryPath, "1.0.0", repositoryUrl);
                packageBuilderContext.Build();

                var packageFile = NuGetUtilities.BuildPackageFile(packageBuilderContext.NupkgFilename, repositoryUrl);
                packageFile.IsNuspecRewritten = true;

                Assert.That(packageFile, Is.Not.Null);
                Assert.That(packageFile.Filename, Is.EqualTo("test.1.0.0.nupkg"));
                Assert.That(packageFile.FilenameAbsolutePath, Is.EqualTo(packageBuilderContext.NupkgFilename));

                if (expectedGithubRepositoryUrl == null)
                {
                    Assert.Null(packageFile.Owner);
                    Assert.Null(packageFile.RepositoryName);
                    Assert.That(packageFile.RepositoryUrl, Is.Null);
                    return;
                }

                Assert.That(packageFile.Owner, Is.EqualTo(expectedOwner));
                Assert.That(packageFile.RepositoryName, Is.EqualTo(expectedRepositoryName));
                Assert.That(packageFile.RepositoryUrl, Is.EqualTo(expectedGithubRepositoryUrl));
            }

            [TestCase("jcansdale/gpr", "jcansdale", "gpr", "https://github.com/jcansdale/gpr")]
            [TestCase("jcansdale//gpr", "jcansdale", "gpr", "https://github.com/jcansdale/gpr")]
            [TestCase("/jcansdale/gpr", "jcansdale", "gpr", "https://github.com/jcansdale/gpr")]
            [TestCase("http://github.com/jcansdale/gpr", "jcansdale", "gpr", "https://github.com/jcansdale/gpr")]
            [TestCase("https://github.com/jcansdale/gpr", "jcansdale", "gpr", "https://github.com/jcansdale/gpr")]
            [TestCase("https://github.com/jcansdale\\gpr", "jcansdale", "gpr", "https://github.com/jcansdale/gpr")]
            [TestCase("https://github.com/jcansdale///////gpr", "jcansdale", "gpr", "https://github.com/jcansdale/gpr")]
            [TestCase("  https://github.com/jcansdale/gpr ", "jcansdale", "gpr", "https://github.com/jcansdale/gpr", Description = "Whitespace")]
            [TestCase("https://foo.bar.com/ritchxu/gpr", "ritchxu", "gpr", "https://foo.bar.com/ritchxu/gpr")]
            public void BuildOwnerAndRepositoryFromUrlFromNupkg(string repositoryUrl, string expectedOwner, string expectedRepositoryName, string expectedGithubRepositoryUrl)
            {
                using var packageBuilderContext = new PackageBuilderContext(TmpDirectoryPath, "1.0.0", repositoryUrl);
                packageBuilderContext.Build();

                var packageFile = NuGetUtilities.BuildPackageFile(packageBuilderContext.NupkgFilename, null);

                Assert.True(NuGetUtilities.BuildOwnerAndRepositoryFromUrlFromNupkg(packageFile));

                Assert.That(packageFile.Owner, Is.EqualTo(expectedOwner));
                Assert.That(packageFile.RepositoryName, Is.EqualTo(expectedRepositoryName));
                Assert.That(packageFile.RepositoryUrl, Is.EqualTo(expectedGithubRepositoryUrl));
            }

            [TestCase("ritchxu/gpr", true, true, "ritchxu", "gpr", "https://github.com/ritchxu/gpr", "nuget.pkg.github.com")]
            [TestCase("https://foo.githubenterprise.com/ritchxu/gpr", true, true, "ritchxu", "gpr", "https://foo.githubenterprise.com/ritchxu/gpr", "nuget.foo.githubenterprise.com")]
            [TestCase("https://foo.bar.com/ritchxu/gpr", true, true, "ritchxu", "gpr", "https://foo.bar.com/ritchxu/gpr", "nuget.foo.bar.com")]
            [TestCase("https://foo.bar.com/ritchxu/gpr", false, true, "ritchxu", "gpr", "https://foo.bar.com/ritchxu/gpr", "foo.bar.com/_registry/nuget")]
            public void BuildOwnerAndRepositoryFromUrl(
                string repositoryUrl,
                bool subdomainIsolation,
                bool expectedResult,
                string expectedOwner,
                string expectedRepositoryName,
                string expectedGithubRepositoryUrl,
                string expectedNugetPackageEndpoint)
            {
                var packageFile = new PackageFile();

                Assert.That(NuGetUtilities.BuildOwnerAndRepositoryFromUrl(packageFile, repositoryUrl, subdomainIsolation), Is.EqualTo(expectedResult));
                Assert.That(packageFile.Owner, Is.EqualTo(expectedOwner));
                Assert.That(packageFile.RepositoryName, Is.EqualTo(expectedRepositoryName));
                Assert.That(packageFile.RepositoryUrl, Is.EqualTo(expectedGithubRepositoryUrl));
                Assert.That(packageFile.NugetPackageEndpoint, Is.EqualTo(expectedNugetPackageEndpoint));
            }

            [Test]
            public void ReadNupkgManifest()
            {
                using var packageBuilderContext = new PackageBuilderContext(TmpDirectoryPath, "1.0.0", "https://github.com/jcansdale/gpr");
                packageBuilderContext.Build();

                var manifest = NuGetUtilities.ReadNupkgManifest(packageBuilderContext.NupkgFilename);
                Assert.That(manifest, Is.Not.Null);
                Assert.That(manifest.Metadata.Id, Is.EqualTo("test"));
                Assert.That(manifest.Metadata.Version, Is.EqualTo(PackageVersion.Parse("1.0.0")));
                Assert.That(manifest.Metadata.Repository?.Url, Is.EqualTo("https://github.com/jcansdale/gpr"));
            }

            [TestCase("1.0.0", "1.0.0", false)]
            [TestCase("1.0.0", "1.0.1", true)]
            public void ShouldRewriteNupkg_Version(string currentVersion, string updatedVersion, bool shouldUpdateVersion)
            {
                const string repositoryUrl = "https://github.com/jcansdale/gpr";
                using var packageBuilderContext = new PackageBuilderContext(TmpDirectoryPath, currentVersion, repositoryUrl);
                packageBuilderContext.Build();

                var packageFile = NuGetUtilities.BuildPackageFile(packageBuilderContext.NupkgFilename, repositoryUrl);

                Assert.That(NuGetUtilities.ShouldRewriteNupkg(packageFile, PackageVersion.Parse(updatedVersion)), Is.EqualTo(shouldUpdateVersion));
            }

            [TestCase("https://github.com/owner/repo.git", "https://github.com/owner/repo.git", false, Description = "Equals")]
            [TestCase("https://github.com/owner/repo", "https://github.com/owner/REPO", false, Description = "Case insensitive")]
            [TestCase("https://github.com/owner/repo", "https://github.com/owner/repo.git", true, Description = "Url ends with .git")]
            [TestCase(null, "https://github.com/owner/repo.git", true)]
            [TestCase("https://google.com", "https://github.com/owner/repo.git", true)]
            public void ShouldRewriteNupkg_RepositoryUrl(string currentRepositoryUrl, string updatedRepositoryUrl, bool shouldUpdateRepositoryUrl)
            {
                using var packageBuilderContext = new PackageBuilderContext(TmpDirectoryPath, "1.0.0", currentRepositoryUrl);
                packageBuilderContext.Build();

                var packageFile = NuGetUtilities.BuildPackageFile(packageBuilderContext.NupkgFilename, updatedRepositoryUrl);

                Assert.That(NuGetUtilities.ShouldRewriteNupkg(packageFile), Is.EqualTo(shouldUpdateRepositoryUrl));
            }

            [TestCase("randomvalue")]
            [TestCase("git")]
            public void ShouldRewriteNupkg_Ignores_RepositoryType(string repositoryType)
            {
                const string currentRepositoryUrl = "https://github.com/jcansdale/gpr";
                using var packageBuilderContext = new PackageBuilderContext(TmpDirectoryPath, "1.0.0", currentRepositoryUrl, repositoryType);
                packageBuilderContext.Build();

                var packageFile = NuGetUtilities.BuildPackageFile(packageBuilderContext.NupkgFilename, currentRepositoryUrl);

                Assert.That(NuGetUtilities.ShouldRewriteNupkg(packageFile), Is.False);
            }

            [TestCase("https://github.com/owner/repo.git", "https://github.com/owner/repo.git")]
            [TestCase("https://github.com/owner/repo", "https://github.com/owner/repo")]
            public void RewriteNuspec(string repositoryUrl, string expectedRepositoryUrl)
            {
                using var packageBuilderContext = new PackageBuilderContext(TmpDirectoryPath, "1.0.0", null);
                packageBuilderContext.Build();

                var packageFile = NuGetUtilities.BuildPackageFile(packageBuilderContext.NupkgFilename, repositoryUrl);

                NuGetUtilities.RewriteNupkg(packageFile, PackageVersion.Parse("2.0.0"));

                var manifest = NuGetUtilities.ReadNupkgManifest(packageFile.FilenameAbsolutePath);

                Assert.That(manifest.Metadata.Id, Is.EqualTo("test"));
                Assert.That(manifest.Metadata.Version, Is.EqualTo(PackageVersion.Parse("2.0.0")));
                Assert.That(manifest.Metadata.Repository?.Url, Is.EqualTo(expectedRepositoryUrl));
                Assert.That(manifest.Metadata.Repository?.Type, Is.EqualTo("git"));
            }

            [Test]
            public void RewriteNuspec_Overwrites_Existing_Repository_Url()
            {
                using var packageBuilderContext = new PackageBuilderContext(TmpDirectoryPath, "1.0.0", "https://google.com", "google");
                packageBuilderContext.Build();

                var packageFile = NuGetUtilities.BuildPackageFile(packageBuilderContext.NupkgFilename, "https://github.com/owner/repo");

                NuGetUtilities.RewriteNupkg(packageFile, PackageVersion.Parse("2.0.0"));

                var manifest = NuGetUtilities.ReadNupkgManifest(packageFile.FilenameAbsolutePath);

                Assert.That(manifest.Metadata.Id, Is.EqualTo("test"));
                Assert.That(manifest.Metadata.Version, Is.EqualTo(PackageVersion.Parse("2.0.0")));
                Assert.That(manifest.Metadata.Repository?.Url, Is.EqualTo("https://github.com/owner/repo"));
                Assert.That(manifest.Metadata.Repository?.Type, Is.EqualTo("git"));
            }

            sealed class PackageBuilderContext : IDisposable
            {
                readonly DisposableDirectory _disposableDirectory;

                public string WorkingDirectory => _disposableDirectory.WorkingDirectory;
                public string Filename { get; }
                public string NupkgFilename => Path.Combine(WorkingDirectory, Filename);
                readonly string _version;
                readonly string _repositoryUrl;
                readonly string _repositoryType;

                public PackageBuilderContext(string workingDirectory, string version, string repositoryUrl, string repositoryType = "git")
                {
                    _disposableDirectory = new DisposableDirectory(workingDirectory);
                    _version = version;
                    _repositoryUrl = repositoryUrl;
                    _repositoryType = repositoryType;
                    Filename = $"test.{version}.nupkg";
                }

                public void Build()
                {
                    using var archive = ZipFile.Open(NupkgFilename, ZipArchiveMode.Create);
                    var nuspecEntry = archive.CreateEntry("test.nuspec");
                    using var writer = new StreamWriter(nuspecEntry.Open(), new UTF8Encoding(false));
                    writer.Write(BuildNuspec());

                    var libEntry = archive.CreateEntry("lib/netstandard2.0/test.dll");
                    using var libWriter = new StreamWriter(libEntry.Open(), new UTF8Encoding(false));
                    libWriter.Write(string.Empty);
                }

                string BuildNuspec()
                {
                    var package = new XElement("package",
                        new XAttribute(XNamespace.Xmlns + "x", "http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd"),
                        new XElement("metadata",
                            new XElement("id", "test"),
                            new XElement("version", _version),
                            new XElement("authors", "abc123"),
                            new XElement("description", "abc123")));

                    if (_repositoryUrl != null)
                    {
                        package.Element("metadata")?.Add(new XElement("repository",
                            new XAttribute("url", _repositoryUrl),
                            new XAttribute("type", _repositoryType)));
                    }

                    return new XDocument(new XDeclaration("1.0", "utf-8", null), package).ToString();
                }

                public void Dispose()
                {
                    _disposableDirectory.Dispose();
                }
            }
        }
    }
}
