using NUnit.Framework;

namespace GprTool.Tests
{
    [TestFixture]
    public class StringExtensions
    {
        [TestCase(null, null, null, null)]
        [TestCase("jcansdale/gpr", null, null, null)]
        [TestCase("https://github.com/jcansdale/gpr", "jcansdale", "gpr", "https://github.com/jcansdale/gpr")]
        [TestCase("https://github.com/jcansdale\\gpr", "jcansdale", "gpr", "https://github.com/jcansdale/gpr")]
        [TestCase("https://github.com/jcansdale///////gpr", "jcansdale", "gpr", "https://github.com/jcansdale///////gpr")]
        public void BuildGithubRepositoryDetails(string repositoryUrl, string expectedOwner, string expectedRepositoryName, string expectedGithubRepositoryUrl)
        {
            var (githubOwner, githubRepositoryName, githubRepositoryUri) = repositoryUrl.BuildGithubRepositoryDetails();
            Assert.That(githubOwner, Is.EqualTo(expectedOwner));
            Assert.That(githubRepositoryName, Is.EqualTo(expectedRepositoryName));
            if (expectedGithubRepositoryUrl == null)
            {
                Assert.That(githubRepositoryUri, Is.Null);
                return;
            }
            Assert.That(githubRepositoryUri, Is.Not.Null);
            Assert.That(githubRepositoryUri.ToString(), Is.EqualTo(expectedGithubRepositoryUrl));
        }
    }
}
