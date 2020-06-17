using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml;
using System.Xml.Linq;
using NuGet.Packaging;
using NuGet.Versioning;

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
    }

    public class NuGetUtilities
    {
        public static bool BuildOwnerAndRepositoryFromUrl(PackageFile packageFile, string repositoryUrl)
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

            if (!Uri.TryCreate(repositoryUrl, UriKind.Absolute, out var repositoryUri) 
                || repositoryUri.Host != "github.com")
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
            packageFile.RepositoryUrl = $"https://github.com/{packageFile.Owner}/{packageFile.RepositoryName}";

            return true;
        }

        public static bool BuildOwnerAndRepositoryFromUrlFromNupkg(PackageFile packageFile)
        {
            var manifest = ReadNupkgManifest(packageFile.FilenameAbsolutePath);
            return BuildOwnerAndRepositoryFromUrl(packageFile, FindRepositoryUrl(manifest));
        }

        public static PackageFile BuildPackageFile(string filename, string repositoryUrl)
        {
            var packageFile = new PackageFile
            {
                Filename = Path.GetFileName(filename),
                FilenameAbsolutePath = Path.GetFullPath(filename)
            };

            BuildOwnerAndRepositoryFromUrl(packageFile, repositoryUrl);

            return packageFile;
        }

        public static Manifest ReadNupkgManifest(string nupkgPath)
        {
            if (nupkgPath == null) throw new ArgumentNullException(nameof(nupkgPath));
            using var packageArchiveReader = new PackageArchiveReader(nupkgPath.ReadSharedToStream());
            return Manifest.ReadFrom(packageArchiveReader.GetNuspec(), false);
        }

        public static bool ShouldRewriteNupkg(PackageFile packageFile, NuGetVersion nuGetVersion = null)
        {
            if (packageFile == null) throw new ArgumentNullException(nameof(packageFile));

            var manifest = ReadNupkgManifest(packageFile.FilenameAbsolutePath);

            if (nuGetVersion != null && !nuGetVersion.Equals(manifest.Metadata.Version))
            {
                return true;
            }
            
            return !string.Equals(packageFile.RepositoryUrl, FindRepositoryUrl(manifest), StringComparison.OrdinalIgnoreCase);
        }

        static string FindRepositoryUrl(Manifest manifest)
        {
            // Metadata.Repository.Url appears to return null if <repository type=... /> hasn't been set.
            // This happens when a project is built with `dotnet pack` and the RepositoryUrl property.
            // We need to use Metadata.ProjectUrl instead!
            
            return manifest.Metadata.Repository?.Url ?? manifest.Metadata.ProjectUrl?.ToString();
        }

        public static void RewriteNupkg(PackageFile packageFile, NuGetVersion nuGetVersion = null)
        {
            if (packageFile == null) throw new ArgumentNullException(nameof(packageFile));
            
            var randomId = Guid.NewGuid().ToString("N");
  
            using var packageArchiveReader = new PackageArchiveReader(
                packageFile.FilenameAbsolutePath.ReadSharedToStream(), false);
            
            var nuspecXDocument = packageArchiveReader.NuspecReader.Xml;
            var packageXElement = nuspecXDocument.Single("package");
            var metadataXElement = packageXElement.Single("metadata");
            var packageId = packageXElement.Single("id").Value;
            var versionXElement = metadataXElement.Single("version");
            
            if (nuGetVersion != null)
            {
                versionXElement.SetValue(nuGetVersion); 
            }
            else
            {
                nuGetVersion = NuGetVersion.Parse(versionXElement.Value);
            }

            var repositoryXElement = metadataXElement.SingleOrDefault("repository");
            if (repositoryXElement == null)
            {
                repositoryXElement = new XElement("repository");
                repositoryXElement.SetAttributeValue("url", packageFile.RepositoryUrl);
                repositoryXElement.SetAttributeValue("type", "git");
                metadataXElement.Add(repositoryXElement);
            }
            else
            {
                repositoryXElement.SetAttributeValue("url", packageFile.RepositoryUrl);
                repositoryXElement.SetAttributeValue("type", "git");
            }
            
            using var nuspecMemoryStream = new MemoryStream();
            nuspecXDocument.Save(nuspecMemoryStream);
            nuspecMemoryStream.Seek(0, SeekOrigin.Begin);

            var packageFileWorkingDirectoryAbsolutePath = Path.GetDirectoryName(packageFile.FilenameAbsolutePath);
            var packageFileRewriteWorkingDirectory = Path.Combine(packageFileWorkingDirectoryAbsolutePath,
                $"{packageId}.{nuGetVersion}_{randomId}");

            using var tmpDirectory = new DisposableDirectory(packageFileRewriteWorkingDirectory);

            ZipFile.ExtractToDirectory(packageFile.FilenameAbsolutePath, tmpDirectory);

            var nuspecDstFilename = Path.Combine(tmpDirectory, $"{packageId}.nuspec");
            File.WriteAllBytes(nuspecDstFilename, nuspecMemoryStream.ToArray());

            using var outputStream = new MemoryStream();
            
            var packageBuilder = new PackageBuilder(nuspecMemoryStream, tmpDirectory,
                propertyProvider => throw new NotImplementedException());
            packageBuilder.Save(outputStream);

            packageFile.Filename = $"{packageId}.{nuGetVersion}.nupkg";
            packageFile.FilenameAbsolutePath = Path.Combine(packageFileWorkingDirectoryAbsolutePath, Path.ChangeExtension(packageFile.Filename, ".zip"));
            packageFile.IsNuspecRewritten = true;

            File.WriteAllBytes(packageFile.FilenameAbsolutePath, outputStream.ToArray());
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
                warning?.Invoke($"Couldn't find a personal access token for GitHub in:");
                warning?.Invoke(configFile);
                warning?.Invoke("");
                warning?.Invoke("Please generate a token with 'repo', 'write:packages', 'read:packages' and 'delete:packages' scopes:");
                warning?.Invoke("https://github.com/settings/tokens");
                warning?.Invoke("");
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
        
        public static implicit operator string (DisposableDirectory directory) => directory.WorkingDirectory;

        public DisposableDirectory(string workingDirectory)
        {
            WorkingDirectory = workingDirectory;
            Directory.CreateDirectory(workingDirectory);
        }
        
        public void Dispose()
        {
            Directory.Delete(WorkingDirectory, true);
        }
    }
}
