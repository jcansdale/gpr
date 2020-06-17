using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Text;
using System.Xml;
using NuGet.Packaging;
using NuGet.Packaging.Core;
using NuGet.Versioning;
using NUnit.Framework;

namespace GprTool.Tests
{
    [SuppressMessage("ReSharper", "MemberCanBePrivate.Local")]
    public class NuGetUtilitiesTests
    {
        public class TheSetApiKeyMethod
        {
            public string TmpDirectoryPath => Path.Combine(Directory.GetCurrentDirectory(), Guid.NewGuid().ToString("N"));

            const string NuspecXml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<package xmlns=""http://schemas.microsoft.com/packaging/2013/05/nuspecContext.xsd"">
  <metadata>
    <id>test</id>
    <version>1.0.0</version>
    <authors>abc123</authors>
    <description>abc123</description>
    <dependencies>
      <group targetFramework="".NETStandard2.0"">
        <dependency id=""test"" version=""0.0.0"" />
      </group>
    </dependencies>
  </metadata>
</package>";

            [Test]
            public void AddClearTextPassword()
            {
                var xmlDoc = new XmlDocument();
                var source = "SOURCE";
                var expectToken = "TOKEN";

                NuGetUtilities.SetApiKey(xmlDoc, expectToken, source);

                var passwordXpath = $"/configuration/packageSourceCredentials/{source}/add[@key='ClearTextPassword']/@value";
                var token = xmlDoc.SelectSingleNode(passwordXpath)?.Value;
                Assert.That(token, Is.EqualTo(expectToken));
            }

            [Test]
            public void AddUsername()
            {
                var xmlDoc = new XmlDocument();
                var source = "SOURCE";
                var token = "TOKEN";
                var expectUsername = "PersonalAccessToken";

                NuGetUtilities.SetApiKey(xmlDoc, token, source);

                var usernameXpath = $"/configuration/packageSourceCredentials/{source}/add[@key='Username']/@value";
                var username = xmlDoc.SelectSingleNode(usernameXpath)?.Value;
                Assert.That(username, Is.EqualTo(expectUsername));
            }

            [Test]
            public void PreserveExistingPackageSources()
            {
                var xml =
                    @"<?xml version=""1.0"" encoding=""utf-8""?>
<configuration>
    <packageSources>
        <clear />
    </packageSources>
</configuration>";
                var xmlDoc = new XmlDocument();
                xmlDoc.LoadXml(xml);

                NuGetUtilities.SetApiKey(xmlDoc, "TOKEN", "SOURCE");

                var xpath = $"/configuration/packageSources/clear";
                var element = xmlDoc.SelectSingleNode(xpath);
                Assert.That(element, Is.Not.Null);
            }

            [Test]
            public void PreserveExistingPackageSourceCredentials()
            {
                var existingSource = "EXISTING_SOURCE";
                var newSource = "NEW_SOURCE";
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

                var existingElement = xmlDoc.SelectSingleNode($"/configuration/packageSourceCredentials/{existingSource}");
                Assert.That(existingElement, Is.Not.Null);
                var newElement = xmlDoc.SelectSingleNode($"/configuration/packageSourceCredentials/{newSource}");
                Assert.That(newElement, Is.Not.Null);
                var packageSourceCredentialsElements = xmlDoc.SelectNodes($"/configuration/packageSourceCredentials");
                Assert.That(packageSourceCredentialsElements.Count, Is.EqualTo(1));
            }

            [Test]
            public void UpdatePackageSourceCredentials()
            {
                var existingSource = "EXISTING_SOURCE";
                var oldToken = "OLD_TOKEN";
                var newToken = "NEW_TOKEN";
                var xml =
                    $@"<?xml version=""1.0"" encoding=""utf-8""?>
<configuration>
  <packageSourceCredentials>
    <{existingSource}>
      <add key=""Username"" value=""USERNAME"" />
      <add key=""ClearTextPassword"" value=""{oldToken}"" />
    </{existingSource}>
  </packageSourceCredentials>
</configuration>";
                var xmlDoc = new XmlDocument();
                xmlDoc.LoadXml(xml);

                NuGetUtilities.SetApiKey(xmlDoc, newToken, existingSource);

                var passwordAttribute = xmlDoc.SelectSingleNode($"/configuration/packageSourceCredentials/{existingSource}/add[@key='ClearTextPassword']/@value");
                Assert.That(passwordAttribute?.Value, Is.EqualTo(newToken));
                var addElements = xmlDoc.SelectNodes($"/configuration/packageSourceCredentials/{existingSource}/add");
                Assert.That(addElements.Count, Is.EqualTo(2));
                var packageSourceCredentialsElements = xmlDoc.SelectNodes($"/configuration/packageSourceCredentials/*");
                Assert.That(packageSourceCredentialsElements.Count, Is.EqualTo(1));
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
                using var packageBuilderContext = new PackageBuilderContext(TmpDirectoryPath, new NuspecContext(manifest =>
                {
                    manifest.Metadata.Repository = new RepositoryMetadata
                    {
                        Url = repositoryUrl,
                        Type = "git"
                    };
                }));

                packageBuilderContext.Build();

                var packageFile = NuGetUtilities.BuildPackageFile(packageBuilderContext.NupkgFilename, repositoryUrl);
                packageFile.IsNuspecRewritten = true;

                Assert.That(packageFile, Is.Not.Null);
                Assert.That(packageFile.Filename, Is.EqualTo("test.1.0.0.nupkg"));
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
                Assert.That(packageFile.RepositoryUrl, Is.Not.Null);
                Assert.That(packageFile.RepositoryUrl, Is.EqualTo(expectedGithubRepositoryUrl));
            }

            [TestCase("http://github.com/jcansdale/gpr", "git", "jcansdale", "gpr", "https://github.com/jcansdale/gpr")]
            [TestCase("https://github.com/jcansdale/gpr", "git", "jcansdale", "gpr", "https://github.com/jcansdale/gpr")]
            [TestCase("https://github.com/jcansdale\\gpr", "git", "jcansdale", "gpr", "https://github.com/jcansdale/gpr")]
            [TestCase("https://github.com/jcansdale///////gpr", "git", "jcansdale", "gpr", "https://github.com/jcansdale/gpr")]
            [TestCase("  https://github.com/jcansdale/gpr ", "git", "jcansdale", "gpr", "https://github.com/jcansdale/gpr", Description = "Whitespace")]
            [TestCase("http://github.com/jcansdale/gpr", null, "jcansdale", "gpr", "https://github.com/jcansdale/gpr")]
            public void BuildOwnerAndRepositoryFromUrlFromNupkg(string repositoryUrl, string repositoryType, string expectedOwner, string expectedRepositoryName, string expectedGithubRepositoryUrl)
            {
                using var packageBuilderContext = new PackageBuilderContext(TmpDirectoryPath, new NuspecContext(manifest =>
                {
                    if (repositoryUrl is { } && repositoryType is { })
                    {
                        // Only create Repository when both Url and Type are specified 
                        manifest.Metadata.Repository = new RepositoryMetadata
                        {
                            Url = repositoryUrl,
                            Type = repositoryType
                        };
                    }

                    manifest.Metadata.SetProjectUrl(repositoryUrl);
                }));

                packageBuilderContext.Build();

                var packageFile = NuGetUtilities.BuildPackageFile(packageBuilderContext.NupkgFilename, null);

                Assert.True(NuGetUtilities.BuildOwnerAndRepositoryFromUrlFromNupkg(packageFile));

                Assert.That(packageFile.Owner, Is.EqualTo(expectedOwner));
                Assert.That(packageFile.RepositoryName, Is.EqualTo(expectedRepositoryName));
                Assert.That(packageFile.RepositoryUrl, Is.EqualTo(expectedGithubRepositoryUrl));
            }

            [Test]
            public void ReadNupkgManifest()
            {
                using var packageBuilderContext = new PackageBuilderContext(TmpDirectoryPath, new NuspecContext(manifest =>
                {
                    manifest.Metadata.Version = new NuGetVersion("1.0.0");
                    manifest.Metadata.Repository = new RepositoryMetadata
                    {
                        Url = "https://github.com/jcansdale/gpr",
                        Type = "git"
                    };
                }));

                packageBuilderContext.Build();

                var manifest = NuGetUtilities.ReadNupkgManifest(packageBuilderContext.NupkgFilename);
                Assert.That(manifest, Is.Not.Null);
                Assert.That(manifest.Metadata.Version, Is.EqualTo(packageBuilderContext.NuspecContext.Manifest.Metadata.Version));
                Assert.That(manifest.Metadata.Repository, Is.Not.Null);
                Assert.That(manifest.Metadata.Repository.Url, Is.EqualTo(packageBuilderContext.NuspecContext.Manifest.Metadata.Repository.Url));
            }

            [TestCase("1.0.0", "1.0.0", false)]
            [TestCase("1.0.0", "1.0.1", true)]
            public void ShouldRewriteNupkg_Version(string currentVersion, string updatedVersion, bool shouldUpdateVersion)
            {
                const string repositoryUrl = "https://github.com/jcansdale/gpr";

                using var packageBuilderContext = new PackageBuilderContext(TmpDirectoryPath, new NuspecContext(manifest =>
                {
                    manifest.Metadata.Version = new NuGetVersion(currentVersion);
                    manifest.Metadata.Repository = new RepositoryMetadata
                    {
                        Url = repositoryUrl,
                        Type = "git"
                    };

                    manifest.Metadata.SetProjectUrl(repositoryUrl);
                }));

                packageBuilderContext.Build();

                var packageFile = NuGetUtilities.BuildPackageFile(packageBuilderContext.NupkgFilename, repositoryUrl);

                Assert.That(
                    NuGetUtilities.ShouldRewriteNupkg(packageFile,
                        NuGetVersion.Parse(updatedVersion)), Is.EqualTo(shouldUpdateVersion));
            }

            [TestCase("https://github.com/owner/repo.git", "https://github.com/owner/repo.git", false, Description = "Equals")]
            [TestCase("https://github.com/owner/repo", "https://github.com/owner/REPO", false, Description = "Case insensitive")]
            [TestCase("https://github.com/owner/repo", "https://github.com/owner/repo.git", true, Description = "Url ends with .git")]
            [TestCase(null, "https://github.com/owner/repo.git", true)]
            [TestCase("https://google.com", "https://github.com/owner/repo.git", true)]
            public void ShouldRewriteNupkg_RepositoryUrl(string currentRepositoryUrl, string updatedRepositoryUrl, bool shouldUpdateRepositoryUrl)
            {
                using var packageBuilderContext = new PackageBuilderContext(TmpDirectoryPath, new NuspecContext(manifest =>
                {
                    if (currentRepositoryUrl == null)
                    {
                        manifest.Metadata.Repository = null;
                        return;
                    }

                    manifest.Metadata.Repository = new RepositoryMetadata
                    {
                        Url = currentRepositoryUrl,
                        Type = "git"
                    };

                    manifest.Metadata.SetProjectUrl(currentRepositoryUrl);
                }));

                packageBuilderContext.Build();

                var packageFile = NuGetUtilities.BuildPackageFile(packageBuilderContext.NupkgFilename, updatedRepositoryUrl);

                Assert.That(NuGetUtilities.ShouldRewriteNupkg(packageFile), Is.EqualTo(shouldUpdateRepositoryUrl));
            }

            [TestCase("randomvalue")]
            [TestCase("git")]
            public void ShouldRewriteNupkg_Ignores_RepositoryType(string repositoryType)
            {
                const string currentRepositoryUrl = "https://github.com/jcansdale/gpr";

                using var packageBuilderContext = new PackageBuilderContext(TmpDirectoryPath, new NuspecContext(manifest =>
                {
                    manifest.Metadata.Repository = new RepositoryMetadata
                    {
                        Url = currentRepositoryUrl,
                        Type = repositoryType
                    };

                    manifest.Metadata.SetProjectUrl(currentRepositoryUrl);
                }));

                packageBuilderContext.Build();

                var packageFile = NuGetUtilities.BuildPackageFile(packageBuilderContext.NupkgFilename, currentRepositoryUrl);

                Assert.That(NuGetUtilities.ShouldRewriteNupkg(packageFile), Is.EqualTo(false));
            }

            [TestCase("https://github.com/owner/repo.git", "https://github.com/owner/repo.git")]
            [TestCase("https://github.com/owner/repo", "https://github.com/owner/repo")]
            public void RewriteNuspec(string repositoryUrl, string expectedRepositoryUrl)
            {
                using var packageBuilderContext = new PackageBuilderContext(TmpDirectoryPath, new NuspecContext(
                    manifest =>
                    {
                        manifest.Metadata.Repository = null;
                    }));
                packageBuilderContext.Build();

                var packageFile = NuGetUtilities.BuildPackageFile(packageBuilderContext.NupkgFilename, repositoryUrl);

                NuGetUtilities.RewriteNupkg(packageFile, NuGetVersion.Parse("2.0.0"));

                using var rewrittenNupkgPackageReader = new PackageArchiveReader(File.OpenRead(packageFile.FilenameAbsolutePath));

                var rewrittenNupkgPackageIdentity = rewrittenNupkgPackageReader.GetIdentity();
                var rewrittenNupkgRepositoryMetadata = rewrittenNupkgPackageReader.NuspecReader.GetRepositoryMetadata();

                Assert.That(rewrittenNupkgPackageIdentity, Is.Not.Null);
                Assert.That(rewrittenNupkgPackageIdentity.Id, Is.EqualTo("test"));
                Assert.That(rewrittenNupkgPackageIdentity.Version, Is.EqualTo(NuGetVersion.Parse("2.0.0")));
                Assert.That(rewrittenNupkgRepositoryMetadata, Is.Not.Null);
                Assert.That(rewrittenNupkgRepositoryMetadata.Url, Is.EqualTo(expectedRepositoryUrl));
                Assert.That(rewrittenNupkgRepositoryMetadata.Type, Is.EqualTo("git"));
            }

            [Test]
            public void RewriteNuspec_Overwrites_Existing_Repository_Url()
            {
                using var packageBuilderContext = new PackageBuilderContext(TmpDirectoryPath, new NuspecContext(
                    manifest =>
                    {
                        manifest.Metadata.Repository = new RepositoryMetadata
                        {
                            Url = "https://google.com", 
                            Type = "google"
                        };
                    }));
                packageBuilderContext.Build();

                var packageFile = NuGetUtilities.BuildPackageFile(packageBuilderContext.NupkgFilename,  "https://github.com/owner/repo");

                NuGetUtilities.RewriteNupkg(packageFile, NuGetVersion.Parse("2.0.0"));

                using var rewrittenNupkgPackageReader = new PackageArchiveReader(File.OpenRead(packageFile.FilenameAbsolutePath));

                var rewrittenNupkgPackageIdentity = rewrittenNupkgPackageReader.GetIdentity();
                var rewrittenNupkgRepositoryMetadata = rewrittenNupkgPackageReader.NuspecReader.GetRepositoryMetadata();

                Assert.That(rewrittenNupkgPackageIdentity, Is.Not.Null);
                Assert.That(rewrittenNupkgPackageIdentity.Id, Is.EqualTo("test"));
                Assert.That(rewrittenNupkgPackageIdentity.Version, Is.EqualTo(NuGetVersion.Parse("2.0.0")));
                Assert.That(rewrittenNupkgRepositoryMetadata, Is.Not.Null);
                Assert.That(rewrittenNupkgRepositoryMetadata.Url, Is.EqualTo("https://github.com/owner/repo"));
                Assert.That(rewrittenNupkgRepositoryMetadata.Type, Is.EqualTo("git"));
            }

            [SuppressMessage("ReSharper", "MemberCanBePrivate.Local")]
            class NuspecContext : IDisposable
            {
                public Manifest Manifest { get; }
                public MemoryStream ManifestStream { get; }

                public NuspecContext(Action<Manifest> manifestBuilder = null)
                {
                    using var nuspecMemoryStream = new MemoryStream(Encoding.UTF8.GetBytes(NuspecXml));
                    Manifest = Manifest.ReadFrom(nuspecMemoryStream, true);
                    manifestBuilder?.Invoke(Manifest);
                    ManifestStream = new MemoryStream();
                    Manifest.Save(ManifestStream, true);
                    ManifestStream.Seek(0, SeekOrigin.Begin);
                }

                public void Dispose()
                {
                    ManifestStream?.Dispose();
                }
            }

            class PackageBuilderContext : IDisposable
            {
                readonly DisposableDirectory _disposableDirectory;

                public string WorkingDirectory => _disposableDirectory.WorkingDirectory;
                public string Filename { get; }
                public string NupkgFilename => Path.Combine(WorkingDirectory, Filename);
                public NuspecContext NuspecContext { get; }

                public PackageBuilderContext(string workingDirectory, NuspecContext nuspecContext)
                {
                    if (workingDirectory == null) throw new ArgumentNullException(nameof(workingDirectory));
                    if (nuspecContext == null) throw new ArgumentNullException(nameof(nuspecContext));
                    _disposableDirectory = new DisposableDirectory(workingDirectory);

                    Filename = $"{nuspecContext.Manifest.Metadata.Id}.{nuspecContext.Manifest.Metadata.Version}.nupkg";
                    NuspecContext = nuspecContext;
                }

                public void Build(Action<PackageBuilder> builder = null)
                {
                    using var packageBuilderOutputStream = new MemoryStream();

                    var nupkgPath = Path.Combine(WorkingDirectory, Filename);
                    var packageBuilder = new PackageBuilder(NuspecContext.ManifestStream, WorkingDirectory, s => throw new NotImplementedException());

                    builder?.Invoke(packageBuilder);

                    packageBuilder.Save(packageBuilderOutputStream);

                    File.WriteAllBytes(nupkgPath, packageBuilderOutputStream.ToArray());
                }

                public void Dispose()
                {
                    NuspecContext.Dispose();
                }
            }
        }
    }
}
