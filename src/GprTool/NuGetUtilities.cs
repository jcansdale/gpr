using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace GprTool
{
    public class PackageFile
    {
        public string Owner { get; set; }
        public string RepositoryName { get; set; }
        public string RepositoryUrl { get; set; }
        public bool IsNuspecRewritten { get; set; }
        public bool IsUploaded { get; set; }

        public string Filename { get; set; }
        public string FilenameAbsolutePath { get; set; }
        public string NugetPackageEndpoint { get; set; }
    }

    public class NuGetUtilities
    {
        public static bool BuildOwnerAndRepositoryFromUrl(PackageFile packageFile, string repositoryUrl, bool subdomainIsolation)
        {
            if (repositoryUrl == null)
            {
                return false;
            }

            repositoryUrl = repositoryUrl.Trim();

            if (Uri.IsWellFormedUriString(repositoryUrl, UriKind.Relative))
            {
                repositoryUrl = $"https://github.com/{repositoryUrl}";
            }

            if (!Uri.TryCreate(repositoryUrl, UriKind.Absolute, out var repositoryUri))
            {
                return false;
            }

            if (repositoryUri.Scheme != Uri.UriSchemeHttps)
            {
                repositoryUri = new UriBuilder(repositoryUri)
                {
                    Scheme = Uri.UriSchemeHttps,
                    Port = -1
                }.Uri;
            }

            var ownerAndRepositoryName = repositoryUri.PathAndQuery
                .Substring(1)
                .Replace("\\", "/")
                .Split("/", StringSplitOptions.RemoveEmptyEntries)
                .ToList();

            if (ownerAndRepositoryName.Count != 2)
            {
                return false;
            }

            packageFile.Owner = ownerAndRepositoryName[0];
            packageFile.RepositoryName = ownerAndRepositoryName[1];
            packageFile.RepositoryUrl = $"https://{repositoryUri.Host}/{packageFile.Owner}/{packageFile.RepositoryName}";

            if (repositoryUri.Host != "github.com")
            {
                packageFile.NugetPackageEndpoint = subdomainIsolation ? $"nuget.{repositoryUri.Host}" : $"{repositoryUri.Host}/_registry/nuget";
            }
            else
            {
                packageFile.NugetPackageEndpoint = "nuget.pkg.github.com";
            }

            return true;
        }

        public static bool BuildOwnerAndRepositoryFromUrlFromNupkg(PackageFile packageFile, bool subdomainIsolation = true)
        {
            var manifest = ReadNupkgManifest(packageFile.FilenameAbsolutePath);
            return BuildOwnerAndRepositoryFromUrl(packageFile, manifest.Metadata.Repository?.Url, subdomainIsolation);
        }

        public static PackageFile BuildPackageFile(string filename, string repositoryUrl, bool subdomainIsolation = true)
        {
            var packageFile = new PackageFile
            {
                Filename = Path.GetFileName(filename),
                FilenameAbsolutePath = Path.GetFullPath(filename)
            };

            BuildOwnerAndRepositoryFromUrl(packageFile, repositoryUrl, subdomainIsolation);

            return packageFile;
        }

        public static PackageManifest ReadNupkgManifest(string nupkgPath)
        {
            using var archive = ZipFile.OpenRead(nupkgPath);
            var nuspecEntry = archive.Entries.SingleOrDefault(entry =>
                entry.FullName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase));
            if (nuspecEntry == null)
            {
                throw new InvalidOperationException($"Could not find a .nuspec entry in '{nupkgPath}'.");
            }

            using var stream = nuspecEntry.Open();
            var document = XDocument.Load(stream);
            var packageElement = document.Single("package");
            var metadataElement = packageElement.Single("metadata");

            var versionText = metadataElement.Single("version").Value;
            var metadata = new ManifestMetadata
            {
                Id = metadataElement.Single("id").Value,
                Version = PackageVersion.Parse(versionText)
            };

            if (metadataElement.SingleOrDefault("repository") is { } repositoryElement)
            {
                metadata.Repository = new RepositoryMetadata
                {
                    Url = repositoryElement.GetOptionalAttributeValue("url"),
                    Type = repositoryElement.GetOptionalAttributeValue("type")
                };
            }

            return new PackageManifest(metadata);
        }

        public static bool ShouldRewriteNupkg(PackageFile packageFile, PackageVersion packageVersion = null)
        {
            if (packageFile == null) throw new ArgumentNullException(nameof(packageFile));

            var manifest = ReadNupkgManifest(packageFile.FilenameAbsolutePath);

            if (packageVersion != null && !packageVersion.Equals(manifest.Metadata.Version))
            {
                return true;
            }

            return !string.Equals(packageFile.RepositoryUrl, manifest.Metadata.Repository?.Url, StringComparison.OrdinalIgnoreCase);
        }

        public static void RewriteNupkg(PackageFile packageFile, PackageVersion packageVersion = null)
        {
            if (packageFile == null) throw new ArgumentNullException(nameof(packageFile));

            var randomId = Guid.NewGuid().ToString("N");
            var packageFileWorkingDirectoryAbsolutePath = Path.GetDirectoryName(packageFile.FilenameAbsolutePath);
            var rewrittenPath = Path.Combine(packageFileWorkingDirectoryAbsolutePath, randomId);

            using var tmpDirectory = new DisposableDirectory(rewrittenPath);

            ZipFile.ExtractToDirectory(packageFile.FilenameAbsolutePath, tmpDirectory, overwriteFiles: true);

            var nuspecFilename = Directory
                .EnumerateFiles(tmpDirectory, "*.nuspec", SearchOption.AllDirectories)
                .Single();

            var document = XDocument.Load(nuspecFilename);
            var packageElement = document.Single("package");
            var metadataElement = packageElement.Single("metadata");

            if (packageVersion == null)
            {
                packageVersion = PackageVersion.Parse(metadataElement.Single("version").Value);
            }
            else
            {
                metadataElement.Single("version").SetValue(packageVersion.ToString());
            }

            var packageId = metadataElement.Single("id").Value;
            var repositoryElement = metadataElement.SingleOrDefault("repository");
            if (repositoryElement == null)
            {
                repositoryElement = new XElement("repository");
                metadataElement.Add(repositoryElement);
            }

            repositoryElement.SetAttributeValue("url", packageFile.RepositoryUrl);
            repositoryElement.SetAttributeValue("type", "git");

            using (var writer = XmlWriter.Create(nuspecFilename, new XmlWriterSettings
            {
                Encoding = new UTF8Encoding(false),
                Indent = true,
                OmitXmlDeclaration = false
            }))
            {
                document.Save(writer);
            }

            packageFile.Filename = $"{packageId}.{packageVersion}.nupkg";
            packageFile.FilenameAbsolutePath = Path.Combine(packageFileWorkingDirectoryAbsolutePath, Path.ChangeExtension(packageFile.Filename, ".zip"));
            packageFile.IsNuspecRewritten = true;

            if (File.Exists(packageFile.FilenameAbsolutePath))
            {
                File.Delete(packageFile.FilenameAbsolutePath);
            }

            ZipFile.CreateFromDirectory(tmpDirectory, packageFile.FilenameAbsolutePath);
        }

        public static string FindTokenInNuGetConfig(Action<string> warning = null)
        {
            var configFile = GetDefaultConfigFile(warning);
            if (!File.Exists(configFile))
            {
                warning?.Invoke($"Couldn't find file at '{configFile}'");
                return null;
            }

            var xmlDoc = new XmlDocument();
            xmlDoc.Load(configFile);
            var tokenValue = xmlDoc.SelectSingleNode("/configuration/packageSourceCredentials/github/add[@key='ClearTextPassword']/@value");
            if (tokenValue == null)
            {
                warning?.Invoke("Couldn't find a personal access token for GitHub in:");
                warning?.Invoke(configFile);
                warning?.Invoke(string.Empty);
                warning?.Invoke("Please generate a token with 'repo', 'write:packages', 'read:packages' and 'delete:packages' scopes:");
                warning?.Invoke("https://github.com/settings/tokens");
                warning?.Invoke(string.Empty);
                warning?.Invoke(@"The token can be added under the 'configuration' element of your NuGet.Config file:
<packageSourceCredentials>
  <github>
    <add key=""Username"" value=""USERNAME"" />
    <add key=""ClearTextPassword"" value=""TOKEN"" />
  </github>
</packageSourceCredentials>
");

                return null;
            }

            return tokenValue.Value;
        }

        public static void SetApiKey(string configFile, string token, string source, Action<string> warning = null)
        {
            var xmlDoc = new XmlDocument();
            if (File.Exists(configFile))
            {
                xmlDoc.Load(configFile);
            }

            SetApiKey(xmlDoc, token, source);

            var dir = Path.GetDirectoryName(configFile);
            if (!Directory.Exists(dir))
            {
                warning?.Invoke($"Creating directory: {dir}");
                Directory.CreateDirectory(dir);
            }

            warning?.Invoke($"Saving file to: {configFile}");
            xmlDoc.Save(configFile);
            warning?.Invoke(File.ReadAllText(configFile));
        }

        public static void SetApiKey(XmlDocument xmlDoc, string token, string source)
        {
            var configurationElement = xmlDoc.SelectSingleNode("/configuration") ?? xmlDoc.CreateElement("configuration");
            var packageSourceCredentialsElement = configurationElement.SelectSingleNode("packageSourceCredentials") ?? xmlDoc.CreateElement("packageSourceCredentials");
            var sourceElement = packageSourceCredentialsElement.SelectSingleNode(source) ?? xmlDoc.CreateElement(source);
            sourceElement.RemoveAll();
            var addUsernameElement = xmlDoc.CreateElement("add");
            addUsernameElement.SetAttribute("key", "Username");
            addUsernameElement.SetAttribute("value", "PersonalAccessToken");
            var addClearTextPasswordElement = xmlDoc.CreateElement("add");
            addClearTextPasswordElement.SetAttribute("key", "ClearTextPassword");
            addClearTextPasswordElement.SetAttribute("value", token);
            sourceElement.AppendChild(addUsernameElement);
            sourceElement.AppendChild(addClearTextPasswordElement);
            packageSourceCredentialsElement.AppendChild(sourceElement);
            configurationElement.AppendChild(packageSourceCredentialsElement);
            xmlDoc.AppendChild(configurationElement);
        }

        public static string GetDefaultConfigFile(Action<string> warning = null)
        {
            string baseDir;
            if (Environment.OSVersion.Platform == PlatformID.Win32NT)
            {
                baseDir = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            }
            else
            {
                var userDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                baseDir = Path.Combine(userDir, ".nuget");
            }

            return Path.Combine(baseDir, "NuGet", "NuGet.Config");
        }
    }

    public class DisposableDirectory : IDisposable
    {
        public string WorkingDirectory { get; }

        public static implicit operator string(DisposableDirectory directory) => directory.WorkingDirectory;

        public DisposableDirectory(string workingDirectory)
        {
            WorkingDirectory = workingDirectory;
            Directory.CreateDirectory(workingDirectory);
        }

        public void Dispose()
        {
            if (Directory.Exists(WorkingDirectory))
            {
                Directory.Delete(WorkingDirectory, true);
            }
        }
    }
}
