using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace GprTool
{
    public enum PackageScope
    {
        User,
        Organization,
        Repository
    }

    public sealed class PackagePath
    {
        public string Owner { get; set; }
        public string Repository { get; set; }
        public string PackageName { get; set; }
        public string[] Names => PackageName == null ? null : new[] { PackageName };
    }

    public sealed class GitHubPackage
    {
        public string Name { get; set; }
        public string PackageType { get; set; }
        public bool IsPrivate { get; set; }
        public string RepositoryUrl { get; set; }
        public int DownloadsTotalCount { get; set; }
        public List<GitHubPackageVersion> Versions { get; set; } = new List<GitHubPackageVersion>();
    }

    public sealed class GitHubPackageVersion
    {
        public string Id { get; set; }
        public string Version { get; set; }
        public int DownloadsTotalCount { get; set; }
        public List<GitHubPackageFile> Files { get; set; } = new List<GitHubPackageFile>();
    }

    public sealed class GitHubPackageFile
    {
        public string Name { get; set; }
        public DateTimeOffset UpdatedAt { get; set; }
        public long? Size { get; set; }
    }

    public sealed class GitHubPackagesPage
    {
        public bool HasNextPage { get; set; }
        public string EndCursor { get; set; }
        public List<GitHubPackage> Packages { get; set; } = new List<GitHubPackage>();
    }

    public static class GraphQLUtilities
    {
        const string GraphQlEndpoint = "https://api.github.com/graphql";

        public static PackagePath ParsePackagesPath(string packagesPath)
        {
            if (string.IsNullOrWhiteSpace(packagesPath))
            {
                return null;
            }

            var split = packagesPath
                .Split('/', StringSplitOptions.RemoveEmptyEntries)
                .Select(part => part.Trim())
                .Where(part => part.Length > 0)
                .ToArray();

            return new PackagePath
            {
                Owner = split.Length > 0 ? split[0] : null,
                Repository = split.Length > 1 ? split[1] : null,
                PackageName = split.Length > 2 ? split[2] : null
            };
        }

        public static async Task<(PackageScope Scope, GitHubPackagesPage Page)> FindPackagesPage(
            string token,
            string packagesPath,
            string after,
            CancellationToken cancellationToken)
        {
            var path = ParsePackagesPath(packagesPath);
            if (path?.Owner == null)
            {
                return default;
            }

            if (path.Repository != null)
            {
                var repositoryPage = await QueryPackagesPage(token, PackageScope.Repository, path, after, cancellationToken);
                return (PackageScope.Repository, repositoryPage);
            }

            try
            {
                var userPage = await QueryPackagesPage(token, PackageScope.User, path, after, cancellationToken);
                return (PackageScope.User, userPage);
            }
            catch (ApplicationException error) when (error.Message.StartsWith("Could not resolve to a User", StringComparison.Ordinal))
            {
            }

            var orgPage = await QueryPackagesPage(token, PackageScope.Organization, path, after, cancellationToken);
            return (PackageScope.Organization, orgPage);
        }

        public static async Task<bool> DeletePackageVersion(string token, string versionId, CancellationToken cancellationToken)
        {
            const string mutation = @"mutation DeletePackageVersion($versionId:ID!) {
  deletePackageVersion(input:{ packageVersionId:$versionId, clientMutationId:""GprTool"" }) {
    success
  }
}";

            using var document = await SendRequest(token, mutation, new Dictionary<string, object>
            {
                ["versionId"] = versionId
            }, cancellationToken);

            var root = document.RootElement.GetProperty("data");
            if (root.TryGetProperty("deletePackageVersion", out var payload)
                && payload.ValueKind != JsonValueKind.Null
                && payload.TryGetProperty("success", out var success))
            {
                return success.GetBoolean();
            }

            return false;
        }

        static async Task<GitHubPackagesPage> QueryPackagesPage(
            string token,
            PackageScope scope,
            PackagePath path,
            string after,
            CancellationToken cancellationToken)
        {
            var query = scope switch
            {
                PackageScope.Repository => RepositoryQuery,
                PackageScope.User => UserQuery,
                _ => OrganizationQuery
            };

            var variables = new Dictionary<string, object>
            {
                ["owner"] = path.Owner,
                ["after"] = after
            };

            if (path.Repository != null)
            {
                variables["repo"] = path.Repository;
            }

            if (path.Names != null)
            {
                variables["names"] = path.Names;
            }

            using var document = await SendRequest(token, query, variables, cancellationToken);
            var container = scope switch
            {
                PackageScope.Repository => document.RootElement.GetProperty("data").GetProperty("repository"),
                PackageScope.User => document.RootElement.GetProperty("data").GetProperty("user"),
                _ => document.RootElement.GetProperty("data").GetProperty("organization")
            };

            if (container.ValueKind == JsonValueKind.Null)
            {
                throw new ApplicationException($"Could not resolve to a {scope}");
            }

            var packages = container.GetProperty("packages");
            var pageInfo = packages.GetProperty("pageInfo");
            var result = new GitHubPackagesPage
            {
                HasNextPage = pageInfo.GetProperty("hasNextPage").GetBoolean(),
                EndCursor = pageInfo.GetProperty("endCursor").ValueKind == JsonValueKind.Null
                    ? null
                    : pageInfo.GetProperty("endCursor").GetString()
            };

            foreach (var packageNode in packages.GetProperty("nodes").EnumerateArray())
            {
                var package = new GitHubPackage
                {
                    Name = packageNode.GetProperty("name").GetString(),
                    PackageType = packageNode.GetProperty("packageType").GetString(),
                    DownloadsTotalCount = packageNode.GetProperty("statistics").GetProperty("downloadsTotalCount").GetInt32(),
                    RepositoryUrl = packageNode.TryGetProperty("repository", out var repositoryElement)
                        && repositoryElement.ValueKind != JsonValueKind.Null
                        && repositoryElement.TryGetProperty("url", out var urlElement)
                            ? urlElement.GetString()
                            : "[PRIVATE REPOSITORIES]",
                    IsPrivate = !packageNode.TryGetProperty("repository", out var repoElement)
                        || repoElement.ValueKind == JsonValueKind.Null
                        || repoElement.GetProperty("isPrivate").GetBoolean()
                };

                var versions = packageNode.GetProperty("versions").GetProperty("nodes");
                foreach (var versionNode in versions.EnumerateArray())
                {
                    var version = new GitHubPackageVersion
                    {
                        Id = versionNode.GetProperty("id").GetString(),
                        Version = versionNode.GetProperty("version").GetString(),
                        DownloadsTotalCount = versionNode.GetProperty("statistics").GetProperty("downloadsTotalCount").GetInt32()
                    };

                    var filesElement = versionNode.GetProperty("files").GetProperty("nodes");
                    foreach (var fileNode in filesElement.EnumerateArray())
                    {
                        version.Files.Add(new GitHubPackageFile
                        {
                            Name = fileNode.GetProperty("name").GetString(),
                            UpdatedAt = fileNode.GetProperty("updatedAt").GetDateTimeOffset(),
                            Size = fileNode.TryGetProperty("size", out var sizeElement) && sizeElement.ValueKind != JsonValueKind.Null
                                ? sizeElement.GetInt64()
                                : (long?)null
                        });
                    }

                    package.Versions.Add(version);
                }

                result.Packages.Add(package);
            }

            return result;
        }

        static async Task<JsonDocument> SendRequest(
            string token,
            string query,
            Dictionary<string, object> variables,
            CancellationToken cancellationToken)
        {
            using var client = CreateClient(token);
            using var request = new HttpRequestMessage(HttpMethod.Post, GraphQlEndpoint)
            {
                Content = new StringContent(JsonSerializer.Serialize(new
                {
                    query,
                    variables
                }), Encoding.UTF8, "application/json")
            };

            using var response = await client.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync();
            var document = JsonDocument.Parse(body);

            if (!response.IsSuccessStatusCode)
            {
                throw new ApplicationException($"{(int)response.StatusCode} {response.ReasonPhrase}: {body}");
            }

            if (document.RootElement.TryGetProperty("errors", out var errors)
                && errors.ValueKind == JsonValueKind.Array
                && errors.GetArrayLength() > 0)
            {
                var message = string.Join(Environment.NewLine, errors.EnumerateArray().Select(error => error.GetProperty("message").GetString()));
                document.Dispose();
                throw new ApplicationException(message);
            }

            return document;
        }

        static HttpClient CreateClient(string token)
        {
            var client = new HttpClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("bearer", token);
            client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue(Program.AssemblyProduct, Program.AssemblyInformationalVersion));
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            return client;
        }

        const string RepositoryQuery = @"query RepositoryPackages($owner:String!, $repo:String!, $after:String, $names:[String!]) {
  repository(owner:$owner, name:$repo) {
    packages(first:100, after:$after, names:$names) {
      pageInfo { endCursor hasNextPage }
      nodes {
        name
        packageType
        repository { isPrivate url }
        statistics { downloadsTotalCount }
        versions(first:100) {
          nodes {
            id
            version
            statistics { downloadsTotalCount }
            files(first:100) {
              nodes { name updatedAt size }
            }
          }
        }
      }
    }
  }
}";

        const string UserQuery = @"query UserPackages($owner:String!, $after:String) {
  user(login:$owner) {
    packages(first:100, after:$after) {
      pageInfo { endCursor hasNextPage }
      nodes {
        name
        packageType
        repository { isPrivate url }
        statistics { downloadsTotalCount }
        versions(first:100) {
          nodes {
            id
            version
            statistics { downloadsTotalCount }
            files(first:100) {
              nodes { name updatedAt size }
            }
          }
        }
      }
    }
  }
}";

        const string OrganizationQuery = @"query OrganizationPackages($owner:String!, $after:String) {
  organization(login:$owner) {
    packages(first:100, after:$after) {
      pageInfo { endCursor hasNextPage }
      nodes {
        name
        packageType
        repository { isPrivate url }
        statistics { downloadsTotalCount }
        versions(first:100) {
          nodes {
            id
            version
            statistics { downloadsTotalCount }
            files(first:100) {
              nodes { name updatedAt size }
            }
          }
        }
      }
    }
  }
}";
    }
}
