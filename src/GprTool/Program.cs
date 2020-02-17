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
    [Subcommand(typeof(ListCommand), typeof(PushCommand), typeof(DetailsCommand), typeof(SetApiKeyCommand))]
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

    [Command(Description = "List packages for user or org (viewer if not specified)")]
    public class ListCommand : GprCommandBase
    {
        protected override async Task OnExecute(CommandLineApplication app)
        {
            var connection = CreateConnection();

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

            var groups = result.GroupBy(p => p.RepositoryUrl);
            foreach (var group in groups.OrderBy(g => g.Key))
            {
                Console.WriteLine(group.Key);
                foreach (var package in group)
                {
                    Console.WriteLine($"    {package.Name} ({package.PackageType}) [{string.Join(", ", package.Versions)}] ({package.DownloadsTotalCount} downloads)");
                }
            }
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

        static string FindReadPackagesToken() => Environment.GetEnvironmentVariable("READ_PACKAGES_TOKEN");

        protected void Warning(string line) => Console.WriteLine(line);

        [Option("-k|--api-key", Description = "The access token to use")]
        protected string AccessToken { get; }
    }
}
