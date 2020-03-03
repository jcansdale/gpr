using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using System.Collections.Generic;
using McMaster.Extensions.CommandLineUtils;
using RestSharp;
using RestSharp.Authenticators;
using Octokit.GraphQL;
using Octokit.GraphQL.Model;
using Octokit.GraphQL.Core;

namespace GprTool
{
    [Command("gpr")]
    [Subcommand(
        typeof(ListCommand),
        typeof(FilesCommand),
        typeof(PushCommand),
        typeof(DeleteCommand),
        typeof(DetailsCommand),
        typeof(SetApiKeyCommand),
        typeof(XmlEncodeCommand)
    )]
    public class Program : GprCommandBase
    {
        public async static Task Main(string[] args)
        {
            try
            {
                await CommandLineApplication.ExecuteAsync<Program>(args);
            }
            catch(ApplicationException e)
            {
                Console.WriteLine(e.Message);
            }
        }

        protected override Task OnExecute(CommandLineApplication app)
        {
            // this shows help even if the --help option isn't specified
            app.ShowHelp();
            return Task.CompletedTask;
        }
    }

    [Command(Description = "List files for a package")]
    public class FilesCommand : GprCommandBase
    {
        protected override async Task OnExecute(CommandLineApplication app)
        {
            var connection = CreateConnection();

            if(PackagesPath is null)
            {
                System.Console.WriteLine("Please include a packages path");
                return;
            }

            var packageCollection = await GraphQLUtilities.FindPackageConnection(connection, PackagesPath);
            if(packageCollection == null)
            {
                Console.WriteLine("Couldn't find packages");
                return;
            }

            var query = packageCollection.Nodes.Select(p => 
                new
                {
                    p.Name, p.Statistics.DownloadsTotalCount,
                    Versions = p.Versions(100, null, null, null, null).Nodes.Select(v =>
                    new 
                    {
                        v.Version, v.Statistics.DownloadsTotalCount,
                        Files = v.Files(40, null, null, null, null).Nodes.Select(f => new { f.Name, f.UpdatedAt, f.Size }).ToList()
                    }).ToList()
                }).Compile();

            var packages = await connection.Run(query);

            foreach(var package in packages)
            {
                Console.WriteLine($"{package.Name} ({package.DownloadsTotalCount} downloads)");
                foreach (var version in package.Versions)
                {
                    if(version.Files.Count == 1)
                    {
                        var file = version.Files[0];
                        if(file.Name.Contains(version.Version))
                        {
                            System.Console.WriteLine($"  {file.Name} ({file.UpdatedAt:d}, {version.DownloadsTotalCount} downloads, {file.Size} bytes)");
                            continue;
                        }
                    }

                    System.Console.WriteLine($"  {version.Version} ({version.DownloadsTotalCount} downloads)");
                    foreach(var file in version.Files)
                    {
                        System.Console.WriteLine($"    {file.Name} ({file.UpdatedAt:d}, {file.Size} bytes)");
                    }
                }
            }
        }

        [Argument(0, Description = "Path to packages the form `owner`, `owner/repo` or `owner/repo/package`")]
        public string PackagesPath { get; set; }
    }

    [Command(Description = "Delete package versions")]
    public class DeleteCommand : GprCommandBase
    {
        protected override async Task OnExecute(CommandLineApplication app)
        {
            var connection = CreateConnection();

            if(PackagesPath is null)
            {
                System.Console.WriteLine("Please include a packages path");
                return;
            }

            var packageCollection = await GraphQLUtilities.FindPackageConnection(connection, PackagesPath);
            if(packageCollection == null)
            {
                Console.WriteLine("Couldn't find packages");
                return;
            }

            var query = packageCollection.Nodes.Select(p => 
                new
                {
                    p.Repository.Url, p.Name,
                    Versions = p.Versions(100, null, null, null, null).Nodes.Select(v =>
                    new 
                    {
                        p.Repository.Url, p.Name,
                        v.Id, v.Version, v.Statistics.DownloadsTotalCount
                    }).ToList()
                }).Compile();

            var packages = await connection.Run(query);

            if (DockerCleanUp)
            {
                foreach(var package in packages)
                {
                    if(package.Versions.Count == 1 && package.Versions[0] is var version && version.Version == "docker-base-layer")
                    {
                        Console.WriteLine($"Cleaning up '{package.Name}'");

                        var versionId = version.Id;
                        var success = await DeletePackageVersion(connection, versionId);
                        if (success)
                        {
                            Console.WriteLine($"  Deleted '{version.Version}'");
                        }
                    }
                }

                Console.WriteLine("Complete");
                return;
            }

            foreach(var package in packages)
            {
                Console.WriteLine(package.Name);
                foreach(var version in package.Versions)
                {
                    if (Force)
                    {
                        Console.WriteLine($"  Deleting '{version.Version}'");

                        var versionId = version.Id;
                        await DeletePackageVersion(connection, versionId);
                    }
                    else
                    {
                        Console.WriteLine($"  {version.Version}");
                    }
                }
            }

            if (!Force)
            {
                Console.WriteLine();
                Console.WriteLine($"To delete these package versions, use the --force option.");
            }
        }

        async Task<bool> DeletePackageVersion(IConnection connection, ID versionId)
        {
            try
            {
                var input = new DeletePackageVersionInput { PackageVersionId = versionId, ClientMutationId = "GrpTool" };
                var mutation = new Mutation().DeletePackageVersion(input).Select(p => p.Success).Compile();
                var payload = await connection.Run(mutation);
                return payload.Value;
            }
            catch(GraphQLException e)
            {
                Console.WriteLine(e.Message);
                return false;
            }
        }

        [Argument(0, Description = "Path to packages in the form `owner`, `owner/repo` or `owner/repo/package`")]
        public string PackagesPath { get; set; }

        [Option("--docker-clean-up", Description = "Clean up orphaned docker layers")]
        protected bool DockerCleanUp { get; set; }

        [Option("--force", Description = "Delete all package versions")]
        protected bool Force { get; set; }
    }

    [Command(Description = "List packages for user or org (viewer if not specified)")]
    public class ListCommand : GprCommandBase
    {
        protected override async Task OnExecute(CommandLineApplication app)
        {
            var connection = CreateConnection();

            var packages = await GetPackages(connection);

            var groups = packages.GroupBy(p => p.RepositoryUrl);
            foreach (var group in groups.OrderBy(g => g.Key))
            {
                Console.WriteLine(group.Key);
                foreach (var package in group)
                {
                    Console.WriteLine($"    {package.Name} ({package.PackageType}) [{string.Join(", ", package.Versions)}] ({package.DownloadsTotalCount} downloads)");
                }
            }
        }

        async Task<IEnumerable<PackageInfo>> GetPackages(IConnection connection)
        {
            IEnumerable<PackageInfo> result;
            if (PackageOwner is string packageOwner)
            {
                var packageConnection = new Query().User(packageOwner).Packages(first: 100);
                result = await TryGetPackages(connection, packageConnection);

                if (result is null)
                {
                    packageConnection = new Query().Organization(packageOwner).Packages(first: 100);
                    result = await TryGetPackages(connection, packageConnection);
                }

                if (result is null)
                {
                    throw new ApplicationException($"Couldn't find a user or org with the login of '{packageOwner}'");
                }
            }
            else
            {
                var packageConnection = new Query().Viewer.Packages(first: 100);
                result = await TryGetPackages(connection, packageConnection);
            }

            return result;
        }

        static async Task<IEnumerable<PackageInfo>> TryGetPackages(IConnection connection, PackageConnection packageConnection)
        {
            var query = packageConnection
                .Nodes
                .Select(p => new PackageInfo
                {
                    RepositoryUrl = p.Repository != null ? p.Repository.Url : "[PRIVATE REPOSITORIES]",
                    Name = p.Name,
                    PackageType = p.PackageType,
                    DownloadsTotalCount = p.Statistics.DownloadsTotalCount,
                    Versions = p.Versions(100, null, null, null, null).Nodes.Select(v => v.Version).ToList()
                })
                .Compile();

            try
            {
                return await connection.Run(query);
            }
            catch (GraphQLException e) when (e.Message.StartsWith("Could not resolve to a "))
            {
                return null;
            }
            catch (GraphQLException e)
            {
                throw new ApplicationException(e.Message, e);
            }
        }

        class PackageInfo
        {
            internal string RepositoryUrl;
            internal string Name;
            internal PackageType PackageType;
            internal int DownloadsTotalCount;
            internal IList<string> Versions;
        }

        [Argument(0, Description = "A user or org that owns packages")]
        public string PackageOwner { get; set; }
    }

    [Command(Description = "Publish a package")]
    public class PushCommand : GprCommandBase
    {
        protected override Task OnExecute(CommandLineApplication app)
        {
            var user = "GprTool";
            var token = GetAccessToken();
            var client = new RestClient($"https://nuget.pkg.github.com/{Owner}/");
            client.Authenticator = new HttpBasicAuthenticator(user, token);
            var request = new RestRequest(Method.PUT);
            request.AddFile("package", PackageFile);
            var response = client.Execute(request);

            if (response.StatusCode == HttpStatusCode.OK)
            {
                Console.WriteLine(response.Content);
                return Task.CompletedTask;
            }

            var nugetWarning = response.Headers.FirstOrDefault(h =>
                h.Name.Equals("X-Nuget-Warning", StringComparison.OrdinalIgnoreCase));
            if (nugetWarning != null)
            {
                Console.WriteLine(nugetWarning.Value);
                return Task.CompletedTask;
            }

            Console.WriteLine(response.StatusDescription);
            foreach (var header in response.Headers)
            {
                Console.WriteLine($"{header.Name}: {header.Value}");
            }
            return Task.CompletedTask;
        }

        [Argument(0, Description = "Path to the package file")]
        public string PackageFile { get; set; }

        [Option("--owner", Description = "The owner if repository URL wasn't specified in nupkg/nuspec")]
        public string Owner { get; } = "GPR-TOOL-DEFAULT-OWNER";
    }

    [Command(Description = "View package details")]
    public class DetailsCommand : GprCommandBase
    {
        protected override Task OnExecute(CommandLineApplication app)
        {
            var user = "GprTool";
            var token = GetAccessToken();
            var client = new RestClient($"https://nuget.pkg.github.com/{Owner}/{Name}/{Version}.json");
            client.Authenticator = new HttpBasicAuthenticator(user, token);
            var request = new RestRequest(Method.GET);
            var response = client.Execute(request);

            if (response.StatusCode == HttpStatusCode.OK)
            {
                var doc = JsonDocument.Parse(response.Content);
                var json = JsonSerializer.Serialize(doc, new JsonSerializerOptions { WriteIndented = true });
                Console.WriteLine(json);
                return Task.CompletedTask;
            }

            var nugetWarning = response.Headers.FirstOrDefault(h =>
                h.Name.Equals("X-Nuget-Warning", StringComparison.OrdinalIgnoreCase));
            if (nugetWarning != null)
            {
                Console.WriteLine(nugetWarning.Value);
                return Task.CompletedTask;
            }

            Console.WriteLine(response.StatusDescription);
            foreach (var header in response.Headers)
            {
                Console.WriteLine($"{header.Name}: {header.Value}");
            }
            return Task.CompletedTask;
        }

        [Argument(0, Description = "Package owner")]
        public string Owner { get; set; }

        [Argument(1, Description = "Package name")]
        public string Name { get; set; }

        [Argument(2, Description = "Package version")]
        public string Version { get; set; }
    }

    [Command(Name = "setApiKey", Description = "Set GitHub API key/personal access token")]
    public class SetApiKeyCommand : GprCommandBase
    {
        protected override Task OnExecute(CommandLineApplication app)
        {
            var configFile = ConfigFile ?? NuGetUtilities.GetDefaultConfigFile(Warning);
            var source = PackageSource ?? "github";

            if (ApiKey == null)
            {
                Console.WriteLine("No API key was specified");
                Console.WriteLine($"Key would be saved as ClearTextPassword to xpath /configuration/packageSourceCredentials/{source}/.");
                Console.WriteLine($"Target confile file is '{configFile}':");
                if (File.Exists(configFile))
                {
                    Console.WriteLine(File.ReadAllText(configFile));
                }
                else
                {
                    Console.WriteLine($"There is currently no file at this location.");
                }

                return Task.CompletedTask;
            }

            NuGetUtilities.SetApiKey(configFile, ApiKey, source, Warning);

            return Task.CompletedTask;
        }

        [Argument(0, Description = "Token / API key")]
        public string ApiKey { get; set; }

        [Argument(1, Description = "The name of the package source (defaults to 'github')")]
        public string PackageSource { get; set; }

        [Option("--config-file", Description = "The NuGet configuration file. If not specified, file the SpecialFolder.ApplicationData + NuGet/NuGet.Config is used")]
        string ConfigFile { get; set; }
    }


    [Command(Name = "xmlEncode", Description = "XML encode token to prevent it fron being automatically deleted")]
    public class XmlEncodeCommand : GprCommandBase
    {
        protected override Task OnExecute(CommandLineApplication app)
        {
            if (Token == null)
            {
                Console.WriteLine("No token was specified");
                return Task.CompletedTask;
            }

            var xmlEncoded = XmlEncode(Token);

            Console.WriteLine("This XML encoded token can be included in a public repository without being automatically deleted by GitHub:");
            Console.WriteLine();
            Console.WriteLine(xmlEncoded);

            Console.WriteLine();
            Console.WriteLine("For example, the following might be included in `nuget.config`:");
            Console.WriteLine(@$"<packageSourceCredentials>
  <github>
    <add key=""Username"" value=""PublicToken"" />
    <add key=""ClearTextPassword"" value=""{xmlEncoded}"" />
  </github>
</packageSourceCredentials>");

            Console.WriteLine();
            Console.WriteLine("Or in a Maven `settings.xml` file:");
            Console.WriteLine(@$"<servers>
  <server>
    <id>github</id>
    <username>PublicToken</username>
    <password>{xmlEncoded}</password>
  </server>
</servers>");

            return Task.CompletedTask;
        }

        static string XmlEncode(string str)
        {
            return string.Concat(str.ToCharArray().Select(ch => $"&#{(int)ch};"));
        }

        [Argument(0, Description = "Personal Access Token")]
        public string Token { get; set; }
    }

    /// <summary>
    /// This base type provides shared functionality.
    /// Also, declaring <see cref="HelpOptionAttribute"/> on this type means all types that inherit from it
    /// will automatically support '--help'
    /// </summary>
    [HelpOption("--help")]
    public abstract class GprCommandBase
    {
        protected abstract Task OnExecute(CommandLineApplication app);

        protected IConnection CreateConnection()
        {
            var productInformation = new ProductHeaderValue("GprTool", ThisAssembly.AssemblyInformationalVersion);
            var token = GetAccessToken();

            var connection = new Connection(productInformation, new Uri("https://api.github.com/graphql"), token);
            return connection;
        }

        public string GetAccessToken()
        {
            if (AccessToken is string accessToken)
            {
                return accessToken;
            }

            if (NuGetUtilities.FindTokenInNuGetConfig(Warning) is string configToken)
            {
                return configToken;
            }

            if (FindReadPackagesToken() is string readToken)
            {
                return readToken;
            }

            throw new ApplicationException("Couldn't find personal access token");
        }

        static string FindReadPackagesToken() =>
            (Environment.GetEnvironmentVariable("READ_PACKAGES_TOKEN") is string token && token != string.Empty) ? token : null;

        protected void Warning(string line) => Console.WriteLine(line);

        [Option("-k|--api-key", Description = "The access token to use")]
        protected string AccessToken { get; set; }
    }
}
