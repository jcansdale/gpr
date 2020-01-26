using System.Xml;
using NUnit.Framework;
using GprTool;

public class NuGetUtilitiesTests
{
    public class TheSetApiKeyMethod
    {
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
    }
}
