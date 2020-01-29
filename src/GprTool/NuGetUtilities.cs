using System;
using System.IO;
using System.Xml;

namespace GprTool
{
    public class NuGetUtilities
    {
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
            var appDataDir = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            if (appDataDir == string.Empty)
            {
                warning?.Invoke("Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData) is empty string.");
                warning?.Invoke("Defaulting to use 'nuget.config' in current directory.");
                return "nuget.config";
            }

            return Path.Combine(appDataDir, "NuGet", "NuGet.Config");
        }
    }
}
