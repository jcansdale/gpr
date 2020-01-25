using System;
using System.IO;
using System.Xml;

namespace GprTool
{
    public class NuGetUtilities
    {
        public static string FindTokenInNuGetConfig(Action<string> warning = null)
        {
            var appDataDir = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var configFile = Path.Combine(appDataDir, "NuGet", "NuGet.Config");
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
    }
}
