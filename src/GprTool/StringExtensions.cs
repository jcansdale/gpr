using System;
using System.Linq;

namespace GprTool
{
    public static class StringExtensions
    {
        public static (string owner, string repositoryName, Uri repositoryUri) BuildGithubRepositoryDetails(this string url)
        {
            if (url == null)
            {
                return default;
            }

            if (!Uri.TryCreate(url, UriKind.Absolute, out var repositoryUri))
            {
                return default;
            }

            var ownerAndRepositoryName = repositoryUri.PathAndQuery
                .Substring(1) 
                .Replace("\\", "/")
                .Split("/", StringSplitOptions.RemoveEmptyEntries)
                .Take(2)
                .ToList();

            return ownerAndRepositoryName.Count != 2 ? default : (ownerAndRepositoryName[0], ownerAndRepositoryName[1], uri: repositoryUri);
        }
    }
}
