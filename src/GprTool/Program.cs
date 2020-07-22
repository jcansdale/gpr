using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Threading;
using McMaster.Extensions.CommandLineUtils;
using NuGet.Versioning;
using RestSharp;
using RestSharp.Authenticators;
using Octokit.GraphQL;
using Octokit.GraphQL.Model;
using Octokit.GraphQL.Core;
using Polly;
using static Octokit.GraphQL.Variable;

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
        typeof(EncodeCommand)
    )]
    public class Program : GprCommandBase
    {
        public static async Task<int> Main(string[] args)
        {
            var gprWaitDebugger = Environment.GetEnvironmentVariable("GPR_WAIT_DEBUGGER")?.Trim();
            if (string.Equals("1", gprWaitDebugger) ||
                string.Equals("true", gprWaitDebugger, StringComparison.OrdinalIgnoreCase))
            {
                var processId = Process.GetCurrentProcess().Id;
                while (!Debugger.IsAttached)
                {
                    Console.WriteLine($"Waiting for debugger to attach. Process id: {processId}.");
                    Thread.Sleep(1000);
                }

                Console.WriteLine("Debugger is attached.");

                Debugger.Break();
            }

            try
            {
                return await CommandLineApplication.ExecuteAsync<Program>(args);
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                return 1;
            }
        }

        protected override Task<int> OnExecuteAsyncImpl(CommandLineApplication app, CancellationToken cancellationToken)
        {
            // this shows help even if the --help option isn't specified
            app.ShowHelp();
            return Task.FromResult(1);
        }
    }

    [Command(Description = "List files for a package")]
    public class FilesCommand : GprCommandBase
    {
        protected override async Task<int> OnExecuteAsyncImpl(CommandLineApplication app,
            CancellationToken cancellationToken)
        {
            var connection = CreateConnection();

            if (PackagesPath is null)
            {
                Console.WriteLine("Please include a packages path");
                return 1;
            }

            var vars = new Dictionary<string, object>
            {
                { "after", null },
            };

            var packageConnection = await GraphQLUtilities.FindPackageConnection(connection, PackagesPath, 100, Var("after"), vars);
            if (packageConnection == null)
            {
                Console.WriteLine("Couldn't find packages");
                return 1;
            }

            var query = packageConnection
                .Select(p => new
                {
                    p.PageInfo.EndCursor,
                    p.PageInfo.HasNextPage,
                    Packages = p.Nodes.Select(p => new
                    {
                        // If access token doesn't have `repo` scope, private packages will have a null `Repository` object
                        IsPrivate = p.Repository != null ? p.Repository.IsPrivate : true,
                        p.Name,
                        p.Statistics.DownloadsTotalCount,
                        Versions = p.Versions(null, null, null, null, null).AllPages().Select(v =>
                        new
                        {
                            v.Version,
                            v.Statistics.DownloadsTotalCount,
                            Files = v.Files(null, null, null, null, null).AllPages(40).Select(f => new { f.Name, f.UpdatedAt, f.Size }).ToList()
                        }).ToList()
                    }).ToList()
                }).Compile();

            long publicStorage = 0;
            long privateStorage = 0;

            while (true)
            {
                var packages = await connection.Run(query, vars, cancellationToken: cancellationToken);

                foreach (var package in packages.Packages)
                {
                    Console.WriteLine($"{package.Name} ({package.DownloadsTotalCount} downloads)");
                    foreach (var version in package.Versions)
                    {
                        foreach (var file in version.Files)
                        {
                            if (file.Size != null)
                            {
                                if (package.IsPrivate)
                                {
                                    privateStorage += (int)file.Size;
                                }
                                else
                                {
                                    publicStorage += (int)file.Size;
                                }
                            }
                        }

                        if (version.Files.Count == 1)
                        {
                            var file = version.Files[0];
                            if (file.Name.Contains(version.Version))
                            {
                                Console.WriteLine($"  {file.Name} ({file.UpdatedAt:d}, {version.DownloadsTotalCount} downloads, {file.Size} bytes)");
                                continue;
                            }
                        }

                        Console.WriteLine($"  {version.Version} ({version.DownloadsTotalCount} downloads)");
                        foreach (var file in version.Files)
                        {
                            Console.WriteLine($"    {file.Name} ({file.UpdatedAt:d}, {file.Size} bytes)");
                        }
                    }
                }

                if(packages.HasNextPage)
                {
                    vars["after"] = packages.EndCursor;
                    continue;
                }

                break;
            }

            Console.WriteLine();
            Console.WriteLine($"Public storage used {publicStorage / (1024 * 1024)} MB");
            Console.WriteLine($"Private storage used {privateStorage / (1024 * 1024)} MB");

            return 0;
        }

        [Argument(0, Description = "Path to packages the form `owner`, `owner/repo` or `owner/repo/package`")]
        public string PackagesPath { get; set; }
    }

    [Command(Description = "Delete package versions")]
    public class DeleteCommand : GprCommandBase
    {
        protected override async Task<int> OnExecuteAsyncImpl(CommandLineApplication app,
            CancellationToken cancellationToken)
        {
            var connection = CreateConnection();

            if (PackagesPath is null)
            {
                Console.WriteLine("Please include a packages path");
                return 1;
            }

            var vars = new Dictionary<string, object>
            {
                { "after", null },
            };

            var packageConnection = await GraphQLUtilities.FindPackageConnection(connection, PackagesPath, 100, Var("after"), vars);
            if (packageConnection == null)
            {
                Console.WriteLine("Couldn't find packages");
                return 1;
            }

            var query = packageConnection.Select(p => new
            {
                p.PageInfo.EndCursor,
                p.PageInfo.HasNextPage,
                Packages = p.Nodes.Select(p => new
                {
                    p.Repository.Url,
                    p.Name,
                    Versions = p.Versions(null, null, null, null, null).AllPages().Select(v =>
                    new
                    {
                        v.Id,
                        v.Version,
                        v.Statistics.DownloadsTotalCount
                    }).ToList()
                }).ToList()
            }).Compile();

            var packagesDeleted = 0;

            var hasNextPage = false;
            do
            {
                var result = await connection.Run(query, vars, cancellationToken: cancellationToken);
                var packages = result.Packages.ToList();

                if (DockerCleanUp)
                {
                    foreach (var package in packages)
                    {
                        if (package.Versions.Count == 1 && package.Versions[0] is var version && version.Version == "docker-base-layer")
                        {
                            Console.WriteLine($"Cleaning up '{package.Name}'");

                            var versionId = version.Id;
                            var success = await DeletePackageVersion(connection, versionId, cancellationToken);
                            if (success)
                            {
                                Console.WriteLine($"  Deleted '{version.Version}'");
                                packagesDeleted++;
                            }
                        }
                    }

                    Console.WriteLine("Complete");
                    return 0;
                }

                foreach (var package in packages)
                {
                    Console.WriteLine(package.Name);
                    foreach (var version in package.Versions)
                    {
                        if (Force)
                        {
                            Console.WriteLine($"  Deleting '{version.Version}'");

                            var versionId = version.Id;
                            await DeletePackageVersion(connection, versionId, cancellationToken);
                            packagesDeleted++;
                        }
                        else
                        {
                            Console.WriteLine($"  {version.Version}");
                        }
                    }
                }

                hasNextPage = result.HasNextPage;
                vars["after"] = result.EndCursor;
            }
            while (hasNextPage);

            if (!Force)
            {
                Console.WriteLine();
                Console.WriteLine("To delete these package versions, use the --force option.");
            }

            return 0;
        }

        async Task<bool> DeletePackageVersion(IConnection connection, ID versionId, CancellationToken cancellationToken)
        {
            try
            {
                var input = new DeletePackageVersionInput { PackageVersionId = versionId, ClientMutationId = "GrpTool" };
                var mutation = new Mutation().DeletePackageVersion(input).Select(p => p.Success).Compile();
                var payload = await connection.Run(mutation, cancellationToken: cancellationToken);
                return payload != null && payload.Value;
            }
            catch (GraphQLException e)
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
        protected override async Task<int> OnExecuteAsyncImpl(CommandLineApplication app,
            CancellationToken cancellationToken)
        {
            var connection = CreateConnection();

            if (PackageOwner is null)
            {
                Console.WriteLine("Please include a packages path");
                return 1;
            }

            var vars = new Dictionary<string, object>
            {
                { "after", null },
            };

            var packageConnection = await GraphQLUtilities.FindPackageConnection(connection, PackageOwner, 100, Var("after"), vars);
            if (packageConnection == null)
            {
                Console.WriteLine("Couldn't find packages");
                return 1;
            }

            var query = packageConnection
                .Select(p => new
                {
                    p.PageInfo.EndCursor,
                    p.PageInfo.HasNextPage,
                    Packages = p.Nodes.Select(p => new PackageInfo
                    {
                        RepositoryUrl = p.Repository != null ? p.Repository.Url : "[PRIVATE REPOSITORIES]",
                        Name = p.Name,
                        PackageType = p.PackageType,
                        DownloadsTotalCount = p.Statistics.DownloadsTotalCount,
                        Versions = p.Versions(null, null, null, null, null).AllPages().Select(v => v.Version).ToList()
                    }).ToList()
                }).Compile();

            var hasNextPage = false;
            do
            {
                var packages = await connection.Run(query, vars);

                var groups = packages.Packages.GroupBy(p => p.RepositoryUrl);
                foreach (var group in groups.OrderBy(g => g.Key))
                {
                    Console.WriteLine(group.Key);
                    foreach (var package in group)
                    {
                        Console.WriteLine($"    {package.Name} ({package.PackageType}) [{string.Join(", ", package.Versions)}] ({package.DownloadsTotalCount} downloads)");
                    }
                }

                hasNextPage = packages.HasNextPage;
                vars["after"] = packages.EndCursor;
            }
            while (hasNextPage);

            return 0;
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
        static IAsyncPolicy<IRestResponse> BuildRetryAsyncPolicy(int retryNumber, int retrySleepSeconds, int timeoutSeconds)
        {
            if (retryNumber <= 0)
            {
                return Policy.NoOpAsync<IRestResponse>();
            }

            var retryPolicy = Policy
                // http://restsharp.org/usage/exceptions.html
                .HandleResult<IRestResponse>(x => FindWarning(x) is null
                                                  && x.StatusCode != HttpStatusCode.NotFound
                                                  && x.StatusCode != HttpStatusCode.Unauthorized
                                                  && x.StatusCode != HttpStatusCode.Conflict
                                                  && x.StatusCode != HttpStatusCode.BadRequest
                                                  && x.StatusCode != HttpStatusCode.OK)
                .WaitAndRetryAsync(retryNumber, retryAttempt => TimeSpan.FromSeconds(retrySleepSeconds));

            var timeoutPolicy = Policy.TimeoutAsync<IRestResponse>(timeoutSeconds);

            return Policy.WrapAsync(retryPolicy, timeoutPolicy);
        }

        protected override async Task<int> OnExecuteAsyncImpl(CommandLineApplication app,
            CancellationToken cancellationToken)
        {
            if (GlobPatterns is null)
            {
                Console.WriteLine("Please include a package path or glob pattern.");
                return 1;
            }

            NuGetVersion nuGetVersion = null;
            if (Version != null && !NuGetVersion.TryParse(Version, out nuGetVersion))
            {
                Console.WriteLine($"Invalid version: {Version}");
                return 1;
            }

            var packageFiles = new List<PackageFile>();
            var currentDirectory = Directory.GetCurrentDirectory();
            packageFiles.AddRange(
                currentDirectory
                    .GetFilesByGlobPatterns(GlobPatterns, out var glob)
                    .Select(x => NuGetUtilities.BuildPackageFile(x, RepositoryUrl)));

            if (!packageFiles.Any())
            {
                Console.WriteLine($"Unable to find any packages matching glob pattern: {glob}. Valid filename extensions are .nupkg, .snupkg.");
                return 1;
            }

            Console.WriteLine($"Found {packageFiles.Count} package{(packageFiles.Count > 1 ? "s" : string.Empty)}.");

            foreach (var packageFile in packageFiles)
            {
                if (!File.Exists(packageFile.FilenameAbsolutePath))
                {
                    Console.WriteLine($"Package file was not found: {packageFile}");
                    return 1;
                }

                if (RepositoryUrl == null)
                {
                    NuGetUtilities.BuildOwnerAndRepositoryFromUrlFromNupkg(packageFile);
                }
                else
                {
                    NuGetUtilities.BuildOwnerAndRepositoryFromUrl(packageFile, RepositoryUrl);
                }

                if (packageFile.Owner == null
                    || packageFile.RepositoryName == null
                    || packageFile.RepositoryUrl == null)
                {
                    Console.WriteLine(
                        $"Project is missing a valid <RepositoryUrl /> XML element value: {packageFile.RepositoryUrl}. " +
                        $"Package filename: {packageFile.FilenameAbsolutePath} " +
                        "Please use --repository option to set a valid upstream GitHub repository. " +
                        "Additional details are available at: https://docs.microsoft.com/en-us/dotnet/core/tools/csproj#repositoryurl");
                    return 1;
                }
            }

            const string user = "GprTool";
            var token = GetAccessToken();

            // Retry X times ->
            // Sleep for X seconds ->
            // Timeout if the request takes longer than X seconds.
            var retryPolicy = BuildRetryAsyncPolicy(Math.Max(0, Retries), 10, 300);

            await packageFiles.ForEachAsync(
                (packageFile, packageCancellationToken) => UploadPackageAsync(packageFile, nuGetVersion, token, retryPolicy, packageCancellationToken),
                (packageFile, exception) =>
                {
                    Console.WriteLine($"[{packageFile.Filename}]: {exception.Message}");
                }, cancellationToken, Math.Max(1, Concurrency));

            static async Task UploadPackageAsync(PackageFile packageFile,
                NuGetVersion nuGetVersion, string token, IAsyncPolicy<IRestResponse> retryPolicy, CancellationToken cancellationToken)
            {
                if (packageFile == null) throw new ArgumentNullException(nameof(packageFile));

                cancellationToken.ThrowIfCancellationRequested();

                NuGetVersion packageVersion;

                var shouldRewriteNuspec = NuGetUtilities.ShouldRewriteNupkg(packageFile, nuGetVersion);

                if (shouldRewriteNuspec)
                {
                    NuGetUtilities.RewriteNupkg(packageFile, nuGetVersion);

                    var manifest = NuGetUtilities.ReadNupkgManifest(packageFile.FilenameAbsolutePath);
                    packageVersion = manifest.Metadata.Version;
                }
                else
                {
                    var manifest = NuGetUtilities.ReadNupkgManifest(packageFile.FilenameAbsolutePath);
                    packageVersion = manifest.Metadata.Version;
                }

                await using var packageStream = packageFile.FilenameAbsolutePath.ReadSharedToStream();

                Console.WriteLine($"[{packageFile.Filename}]: " +
                                  $"Repository url: {packageFile.RepositoryUrl}. " +
                                  $"Version: {packageVersion}. " +
                                  $"Size: {packageStream.Length} bytes. ");

                await retryPolicy.ExecuteAndCaptureAsync(retryCancellationToken =>
                    UploadPackageAsyncImpl(packageFile, packageStream, token, retryCancellationToken), cancellationToken);
            }

            static async Task<IRestResponse> UploadPackageAsyncImpl(PackageFile packageFile, MemoryStream packageStream, string token,
                CancellationToken cancellationToken)
            {
                if (packageFile == null) throw new ArgumentNullException(nameof(packageFile));
                if (packageStream == null) throw new ArgumentNullException(nameof(packageStream));

                var client = WithRestClient($"https://nuget.pkg.github.com/{packageFile.Owner}/",
                    x =>
                    {
                        x.Authenticator = new HttpBasicAuthenticator(user, token);
                        x.ConfigureWebRequest(r =>
                        {
                            r.ServicePoint.Expect100Continue = true;
                            r.SendChunked = true;
                        });
                    });

                var request = new RestRequest(Method.PUT);

                packageStream.Seek(0, SeekOrigin.Begin);

                cancellationToken.ThrowIfCancellationRequested();

                request.AddFile("package", packageStream.CopyTo, packageFile.Filename, packageStream.Length);

                Console.WriteLine($"[{packageFile.Filename}]: Uploading package.");

                var response = await client.ExecuteAsync(request, cancellationToken);

                packageFile.IsUploaded = response.StatusCode == HttpStatusCode.OK;

                if (packageFile.IsUploaded)
                {
                    Console.WriteLine($"[{packageFile.Filename}]: {response.Content}");
                    return response;
                }

                if (FindWarning(response) is { } warning)
                {
                    Console.WriteLine($"[{packageFile.Filename}]: {warning}");
                    return response;
                }

                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    Console.WriteLine($"[{packageFile.Filename}]: {response.StatusDescription}");
                    Console.WriteLine($"[{packageFile.Filename}]: Check that '{packageFile.RepositoryUrl}' exists");
                    return response;
                }

                Console.WriteLine($"[{packageFile.Filename}]: {response.StatusDescription}");
                foreach (var header in response.Headers)
                {
                    Console.WriteLine($"[{packageFile.Filename}]: {header.Name}: {header.Value}");
                }

                return response;
            }

            return packageFiles.All(x => x.IsUploaded) ? 0 : 1;
        }

        static string FindWarning(IRestResponse response)
        {
            return response.Headers.FirstOrDefault(h =>
                h.Name.Equals("X-Nuget-Warning", StringComparison.OrdinalIgnoreCase))?.Value as string;
        }

        [Argument(0, Description = "Path to the package file")]
        public string[] GlobPatterns { get; set; }

        [Option("-r|--repository", Description = "Override current nupkg repository url. Format: owner/repository. E.g: jcansdale/gpr")]
        public string RepositoryUrl { get; set; }

        [Option("-v|--version", Description = "Override current nupkg version")]
        public string Version { get; set; }

        [Option("-c|--concurrency", Description = "The number of packages to upload simultaneously. Default value is 4.")]
        public int Concurrency { get; set; } = 4;

        [Option("--retries", Description = "The number of retries in case of intermittent connection issue. Default value is 3. Set to 0 if you want to disable automatic retry.")]
        public int Retries { get; set; } = 3;
    }

    [Command(Description = "View package details")]
    public class DetailsCommand : GprCommandBase
    {
        protected override async Task<int> OnExecuteAsyncImpl(CommandLineApplication app, CancellationToken cancellationToken)
        {
            var user = "GprTool";
            var token = GetAccessToken();
            var client = WithRestClient($"https://nuget.pkg.github.com/{Owner}/{Name}/{Version}.json");
            client.Authenticator = new HttpBasicAuthenticator(user, token);
            var request = new RestRequest(Method.GET);
            var response = await client.ExecuteAsync<IRestResponse>(request, cancellationToken: cancellationToken);

            if (response.StatusCode == HttpStatusCode.OK)
            {
                var doc = JsonDocument.Parse(response.Content);
                var json = JsonSerializer.Serialize(doc, new JsonSerializerOptions { WriteIndented = true });
                Console.WriteLine(json);
                return 0;
            }

            var nugetWarning = response.Headers.FirstOrDefault(h =>
                h.Name.Equals("X-Nuget-Warning", StringComparison.OrdinalIgnoreCase));
            if (nugetWarning != null)
            {
                Console.WriteLine(nugetWarning.Value);
                return 1;
            }

            Console.WriteLine(response.StatusDescription);
            foreach (var header in response.Headers)
            {
                Console.WriteLine($"{header.Name}: {header.Value}");
            }

            return 1;
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
        protected override async Task<int> OnExecuteAsyncImpl(CommandLineApplication app, CancellationToken cancellationToken)
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
                    Console.WriteLine(await File.ReadAllTextAsync(configFile, cancellationToken));
                }
                else
                {
                    Console.WriteLine("There is currently no file at this location.");
                }

                return 1;
            }

            NuGetUtilities.SetApiKey(configFile, ApiKey, source, Warning);

            return 0;
        }

        [Argument(0, Description = "Token / API key")]
        public string ApiKey { get; set; }

        [Argument(1, Description = "The name of the package source (defaults to 'github')")]
        public string PackageSource { get; set; }

        [Option("--config-file", Description = "The NuGet configuration file. If not specified, file the SpecialFolder.ApplicationData + NuGet/NuGet.Config is used")]
        string ConfigFile { get; set; }
    }

    [Command(Name = "encode", Description = "Encode PAT to prevent it from being automatically deleted by GitHub")]
    public class EncodeCommand : GprCommandBase
    {
        protected override Task<int> OnExecuteAsyncImpl(CommandLineApplication app, CancellationToken cancellationToken)
        {
            if (Token == null)
            {
                Console.WriteLine("No token was specified");
                return Task.FromResult(1);
            }

            Console.WriteLine("An encoded token can be included in a public repository without being automatically deleted by GitHub.");
            Console.WriteLine("These can be used in various package ecosystems like this:");

            var xmlEncoded = XmlEncode(Token);

            Console.WriteLine();
            Console.WriteLine("A NuGet `nuget.config` file:");
            Console.WriteLine(@$"<packageSourceCredentials>
  <github>
    <add key=""Username"" value=""PublicToken"" />
    <add key=""ClearTextPassword"" value=""{xmlEncoded}"" />
  </github>
</packageSourceCredentials>");

            Console.WriteLine();
            Console.WriteLine("A Maven `settings.xml` file:");
            Console.WriteLine(@$"<servers>
  <server>
    <id>github</id>
    <username>PublicToken</username>
    <password>{xmlEncoded}</password>
  </server>
</servers>");

            var unicodeEncode = UnicodeEncode(Token);

            Console.WriteLine();
            Console.WriteLine("An npm `.npmrc` file:");
            Console.WriteLine("@OWNER:registry=https://npm.pkg.github.com");
            Console.WriteLine($"//npm.pkg.github.com/:_authToken=\"{unicodeEncode}\"");
            Console.WriteLine();

            return Task.FromResult(0);
        }

        static string XmlEncode(string str)
        {
            return string.Concat(str.ToCharArray().Select(ch => $"&#{(int)ch};"));
        }

        static string UnicodeEncode(string str)
        {
            return string.Concat(str.ToCharArray().Select(ch => $"\\u{((int)ch).ToString("x4")}"));
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
        static long SetupCancelKeyPress = 1;

        protected static string AssemblyProduct => Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyProductAttribute>()?.Product;
        #if !IS_RUNNING_TESTS
        protected static string AssemblyInformationalVersion => ThisAssembly.AssemblyInformationalVersion;
        #else
        protected static string AssemblyInformationalVersion => "0.0.0";
        #endif

        [SuppressMessage("ReSharper", "UnusedMember.Global")]
        protected async Task<int> OnExecuteAsync(CommandLineApplication app, CancellationToken cancellationToken)
        {
            // Cancel this command's tasks before returning to CommandLineApplication's context.
            // Otherwise the application will hang forever.

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            if (Interlocked.Exchange(ref SetupCancelKeyPress, 0) == 1)
            {
                Console.CancelKeyPress += (sender, args) =>
                {
                    cts.Cancel();
                };
            }

            return await OnExecuteAsyncImpl(app, cts.Token);
        }

        [SuppressMessage("ReSharper", "UnusedMember.Global")]
        protected abstract Task<int> OnExecuteAsyncImpl(CommandLineApplication app, CancellationToken cancellationToken);

        protected static RestClient WithRestClient(string baseUrl, Action<RestClient> builderAction = null)
        {
            var restClient = new RestClient(baseUrl)
            {
                UserAgent = $"{AssemblyProduct}/{AssemblyInformationalVersion}"
            };
            builderAction?.Invoke(restClient);
            return restClient;
        }

        protected IConnection CreateConnection()
        {
            var productInformation = new ProductHeaderValue(AssemblyProduct, AssemblyInformationalVersion);

            var token = GetAccessToken();

            var connection = new Connection(productInformation, new Uri("https://api.github.com/graphql"), token);
            return connection;
        }

        public string GetAccessToken()
        {
            if (AccessToken is { } accessToken)
            {
                return accessToken.Trim();
            }

            if (FindGitHubToken() is { } gitHubToken)
            {
                return gitHubToken.Trim();
            }

            if (NuGetUtilities.FindTokenInNuGetConfig(Warning) is { } configToken)
            {
                return configToken.Trim();
            }

            if (FindReadPackagesToken() is { } readToken)
            {
                return readToken.Trim();
            }

            throw new ApplicationException("Couldn't find personal access token");
        }

        static string FindGitHubToken() => FindEnvironmentVariableToken("GITHUB_TOKEN");

        static string FindReadPackagesToken() => FindEnvironmentVariableToken("READ_PACKAGES_TOKEN");

        static string FindEnvironmentVariableToken(string name) =>
            Environment.GetEnvironmentVariable(name) is { } token && token != string.Empty ? token.Trim() : null;

        protected void Warning(string line) => Console.WriteLine(line);

        [Option("-k|--api-key", Description = "The access token to use")]
        public string AccessToken { get; set; }
    }
}
