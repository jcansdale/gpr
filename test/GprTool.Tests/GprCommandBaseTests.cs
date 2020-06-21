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
        [TestCase("AccessToken", "AccessToken")]
        public void GetAccessToken(string accessToken, string expectToken)
        {
            var target = Substitute.For<GprCommandBase>();
            target.AccessToken = accessToken;

            var token = target.GetAccessToken();

            Assert.That(token, Is.EqualTo(expectToken));
        }
    }
}
