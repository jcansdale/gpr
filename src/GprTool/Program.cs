using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using McMaster.Extensions.CommandLineUtils;
using RestSharp;
using RestSharp.Authenticators;
using Octokit.GraphQL;

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

    [Command(Description = "List my packages")]
    public class ListCommand : GprCommandBase
    {
        protected override async Task OnExecute(CommandLineApplication app)
        {
            var connection = CreateConnection();

            var query = new Query()
                .Viewer
                .Packages(first: 100)
                .Nodes
                .Select(p => new
                {
                    RepositoryUrl = p.Repository != null ? p.Repository.Url : null,
                    p.Name,
                    p.Statistics.DownloadsTotalCount,
//                    p.PackageType, what happened to this?
                    Versions = p.Versions(100, null, null, null, null).Nodes.Select(v => v.Version).ToList()
                })
                .Compile();

            var result = await connection.Run(query);

            var groups = result.GroupBy(p => p.RepositoryUrl);
            foreach(var group in groups)
            {
                Console.WriteLine(group.Key);
                foreach(var package in group)
                {
                    Console.WriteLine($"    {package.Name} [{string.Join(", ", package.Versions)}] ({package.DownloadsTotalCount} downloads)");
                }
            }
        }
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

            throw new ApplicationException("Couldn't find personal access token");
        }

        protected void Warning(string line) => Console.WriteLine(line);

        [Option("-k|--api-key", Description = "The access token to use")]
        protected string AccessToken { get; }
    }
}
