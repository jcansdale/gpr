using System;
using System.Threading;
using System.Threading.Tasks;
using GprTool;
using NUnit.Framework;
using McMaster.Extensions.CommandLineUtils;
using NSubstitute;

public static class GprCommandBaseTests
{
    public class TheGetAccessTokenMethod
    {
        [TestCase("AccessToken", null, null, "AccessToken")]
        [TestCase(null, "GitHubToken", null, "GitHubToken")]
        [TestCase("AccessToken", "GitHubToken", null, "AccessToken")]
        [TestCase(null, null, "ReadPackagesToken", "ReadPackagesToken")]
        [TestCase(null, "GitHubToken", "ReadPackagesToken", "GitHubToken")]
        public void GetAccessToken(string accessToken, string githubToken, string readToken, string expectToken)
        {
            var target = Substitute.For<GprCommandBase>();
            target.AccessToken = accessToken;
            Environment.SetEnvironmentVariable("GITHUB_TOKEN", githubToken);
            Environment.SetEnvironmentVariable("READ_PACKAGES_TOKEN", readToken);

            var token = target.GetAccessToken();

            Assert.That(token, Is.EqualTo(expectToken));
        }
    }
}
