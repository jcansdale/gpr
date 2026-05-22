using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace GprTool
{
    public class Program
    {
        public static string AssemblyProduct =>
            Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyProductAttribute>()?.Product ?? "gpr";

        public static string AssemblyInformationalVersion =>
            Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion?.Split('+')[0]
            ?? "0.0.0";

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
                return await ExecuteAsync(args);
            }
            catch (HelpRequestedException)
            {
                return 1;
            }
            catch (OperationCanceledException)
            {
                return 1;
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                return 1;
            }
        }

        static async Task<int> ExecuteAsync(string[] args)
        {
            using var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (_, eventArgs) =>
            {
                eventArgs.Cancel = true;
                cts.Cancel();
            };

            var tokens = new Queue<string>(args ?? Array.Empty<string>());
            if (tokens.Count == 0 || IsHelp(tokens.Peek()))
            {
                ShowHelp();
                return 1;
            }

            var commandName = tokens.Dequeue();
            var commandArgs = tokens.ToArray();

            return commandName switch
            {
                "files" => await ParseFilesCommand(commandArgs).ExecuteAsync(cts.Token),
                "delete" => await ParseDeleteCommand(commandArgs).ExecuteAsync(cts.Token),
                "list" => await ParseListCommand(commandArgs).ExecuteAsync(cts.Token),
                "push" => await ParsePushCommand(commandArgs).ExecuteAsync(cts.Token),
                "details" => await ParseDetailsCommand(commandArgs).ExecuteAsync(cts.Token),
                "setApiKey" => await ParseSetApiKeyCommand(commandArgs).ExecuteAsync(cts.Token),
                "encode" => await ParseEncodeCommand(commandArgs).ExecuteAsync(cts.Token),
                _ => ShowUnknownCommand(commandName)
            };
        }

        static int ShowUnknownCommand(string commandName)
        {
            Console.WriteLine($"Unknown command '{commandName}'.");
            Console.WriteLine();
            ShowHelp();
            return 1;
        }

        static bool IsHelp(string token) => token == "--help" || token == "-h" || token == "/?";

        static void ShowHelp()
        {
            Console.WriteLine("Usage: gpr [options] [command]");
            Console.WriteLine();
            Console.WriteLine("Options:");
            Console.WriteLine("  --help        Show help information");
            Console.WriteLine("  -k|--api-key  The access token to use");
            Console.WriteLine();
            Console.WriteLine("Commands:");
            Console.WriteLine("  delete        Delete package versions");
            Console.WriteLine("  details       View package details");
            Console.WriteLine("  encode        Encode PAT to prevent it from being automatically deleted by GitHub");
            Console.WriteLine("  files         List files for a package");
            Console.WriteLine("  list          List packages for user or org (viewer if not specified)");
            Console.WriteLine("  push          Publish a package");
            Console.WriteLine("  setApiKey     Set GitHub API key/personal access token");
            Console.WriteLine();
            Console.WriteLine("Run 'gpr [command] --help' for more information about a command.");
        }

        static FilesCommand ParseFilesCommand(string[] args)
        {
            var command = new FilesCommand();
            ParseCommonArguments(args, command, out var positionals, ShowFilesHelp);
            command.PackagesPath = positionals.FirstOrDefault();
            return command;
        }

        static DeleteCommand ParseDeleteCommand(string[] args)
        {
            var command = new DeleteCommand();
            ParseCommonArguments(args, command, out var positionals, ShowDeleteHelp, options =>
            {
                if (TryConsumeFlag(options, "--docker-clean-up"))
                {
                    command.DockerCleanUp = true;
                    return true;
                }

                if (TryConsumeFlag(options, "--force"))
                {
                    command.Force = true;
                    return true;
                }

                if (TryConsumeOptionValue(options, out var version, "-v", "--version"))
                {
                    command.Version = version;
                    return true;
                }

                return false;
            });

            command.PackagesPath = positionals.FirstOrDefault();
            return command;
        }

        static ListCommand ParseListCommand(string[] args)
        {
            var command = new ListCommand();
            ParseCommonArguments(args, command, out var positionals, ShowListHelp);
            command.PackageOwner = positionals.FirstOrDefault();
            return command;
        }

        static PushCommand ParsePushCommand(string[] args)
        {
            var command = new PushCommand();
            ParseCommonArguments(args, command, out var positionals, ShowPushHelp, options =>
            {
                if (TryConsumeOptionValue(options, out var repository, "-r", "--repository"))
                {
                    command.RepositoryUrl = repository;
                    return true;
                }

                if (TryConsumeOptionValue(options, out var version, "-v", "--version"))
                {
                    command.Version = version;
                    return true;
                }

                if (TryConsumeOptionValue(options, out var concurrency, "-c", "--concurrency"))
                {
                    if (!int.TryParse(concurrency, out var parsedConcurrency))
                    {
                        throw new ApplicationException($"Invalid value for --concurrency option: {concurrency}");
                    }

                    command.Concurrency = parsedConcurrency;
                    return true;
                }

                if (TryConsumeOptionValue(options, out var retries, "--retries"))
                {
                    if (!int.TryParse(retries, out var parsedRetries))
                    {
                        throw new ApplicationException($"Invalid value for --retries option: {retries}");
                    }

                    command.Retries = parsedRetries;
                    return true;
                }

                if (TryConsumeFlag(options, "--subdomain-isolation-off"))
                {
                    command.SubdomainIsolationOff = true;
                    return true;
                }

                return false;
            });

            command.GlobPatterns = positionals.ToArray();
            return command;
        }

        static DetailsCommand ParseDetailsCommand(string[] args)
        {
            var command = new DetailsCommand();
            ParseCommonArguments(args, command, out var positionals, ShowDetailsHelp);
            command.Owner = positionals.ElementAtOrDefault(0);
            command.Name = positionals.ElementAtOrDefault(1);
            command.Version = positionals.ElementAtOrDefault(2);
            return command;
        }

        static SetApiKeyCommand ParseSetApiKeyCommand(string[] args)
        {
            var command = new SetApiKeyCommand();
            ParseCommonArguments(args, command, out var positionals, ShowSetApiKeyHelp, options =>
            {
                if (TryConsumeOptionValue(options, out var configFile, "--config-file"))
                {
                    command.ConfigFile = configFile;
                    return true;
                }

                return false;
            });

            command.ApiKey = positionals.ElementAtOrDefault(0);
            command.PackageSource = positionals.ElementAtOrDefault(1);
            return command;
        }

        static EncodeCommand ParseEncodeCommand(string[] args)
        {
            var command = new EncodeCommand();
            ParseCommonArguments(args, command, out var positionals, ShowEncodeHelp);
            command.Token = positionals.FirstOrDefault();
            return command;
        }

        static void ParseCommonArguments(
            string[] args,
            GprCommandBase command,
            out List<string> positionals,
            Action showHelp,
            Func<List<string>, bool> parseCustomOption = null)
        {
            positionals = new List<string>();
            var options = new List<string>(args ?? Array.Empty<string>());

            if (options.Any(IsHelp))
            {
                showHelp();
                throw new HelpRequestedException();
            }

            while (options.Count > 0)
            {
                if (TryConsumeOptionValue(options, out var accessToken, "-k", "--api-key"))
                {
                    command.AccessToken = accessToken;
                    continue;
                }

                if (parseCustomOption != null && parseCustomOption(options))
                {
                    continue;
                }

                var current = options[0];
                if (current.StartsWith("-"))
                {
                    throw new ApplicationException($"Unknown option '{current}'.");
                }

                positionals.Add(current);
                options.RemoveAt(0);
            }
        }

        static bool TryConsumeFlag(List<string> options, params string[] names)
        {
            var index = options.FindIndex(token => names.Contains(token, StringComparer.Ordinal));
            if (index < 0)
            {
                return false;
            }

            options.RemoveAt(index);
            return true;
        }

        static bool TryConsumeOptionValue(List<string> options, out string value, params string[] names)
        {
            for (var index = 0; index < options.Count; index++)
            {
                if (!names.Contains(options[index], StringComparer.Ordinal))
                {
                    continue;
                }

                if (index + 1 >= options.Count)
                {
                    throw new ApplicationException($"Missing value for option '{options[index]}'.");
                }

                value = options[index + 1];
                options.RemoveAt(index + 1);
                options.RemoveAt(index);
                return true;
            }

            value = null;
            return false;
        }

        static void ShowFilesHelp() => Console.WriteLine("Usage: gpr files <owner|owner/repo|owner/repo/package> [-k|--api-key TOKEN]");
        static void ShowDeleteHelp() => Console.WriteLine("Usage: gpr delete <owner|owner/repo|owner/repo/package> [--docker-clean-up] [--force] [-v|--version VERSION] [-k|--api-key TOKEN]");
        static void ShowListHelp() => Console.WriteLine("Usage: gpr list <owner> [-k|--api-key TOKEN]");
        static void ShowPushHelp() => Console.WriteLine("Usage: gpr push <path|glob>... [-r|--repository OWNER/REPO] [-v|--version VERSION] [-c|--concurrency NUMBER] [--retries NUMBER] [--subdomain-isolation-off] [-k|--api-key TOKEN]");
        static void ShowDetailsHelp() => Console.WriteLine("Usage: gpr details <owner> <name> <version> [-k|--api-key TOKEN]");
        static void ShowSetApiKeyHelp() => Console.WriteLine("Usage: gpr setApiKey <token> [source] [--config-file PATH]");
        static void ShowEncodeHelp() => Console.WriteLine("Usage: gpr encode <token>");
    }

    public class FilesCommand : GprCommandBase
    {
        public string PackagesPath { get; set; }

        public override async Task<int> ExecuteAsync(CancellationToken cancellationToken)
        {
            if (PackagesPath is null)
            {
                Console.WriteLine("Please include a packages path");
                return 1;
            }

            var token = GetAccessToken();
            long publicStorage = 0;
            long privateStorage = 0;
            string after = null;

            while (true)
            {
                var (_, page) = await GraphQLUtilities.FindPackagesPage(token, PackagesPath, after, cancellationToken);
                if (page == null || page.Packages.Count == 0)
                {
                    Console.WriteLine("Couldn't find packages");
                    return 1;
                }

                foreach (var package in page.Packages)
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
                                    privateStorage += file.Size.Value;
                                }
                                else
                                {
                                    publicStorage += file.Size.Value;
                                }
                            }
                        }

                        if (version.Files.Count == 1)
                        {
                            var file = version.Files[0];
                            if (file.Name.Contains(version.Version, StringComparison.Ordinal))
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

                if (!page.HasNextPage)
                {
                    break;
                }

                after = page.EndCursor;
            }

            Console.WriteLine();
            Console.WriteLine($"Public storage used {publicStorage / (1024 * 1024)} MB");
            Console.WriteLine($"Private storage used {privateStorage / (1024 * 1024)} MB");
            return 0;
        }
    }

    public class DeleteCommand : GprCommandBase
    {
        public string PackagesPath { get; set; }
        public bool DockerCleanUp { get; set; }
        public bool Force { get; set; }
        public string Version { get; set; }

        public override async Task<int> ExecuteAsync(CancellationToken cancellationToken)
        {
            if (PackagesPath is null)
            {
                Console.WriteLine("Please include a packages path");
                return 1;
            }

            var token = GetAccessToken();
            var packagesDeleted = 0;
            string after = null;

            while (true)
            {
                var (_, page) = await GraphQLUtilities.FindPackagesPage(token, PackagesPath, after, cancellationToken);
                if (page == null || page.Packages.Count == 0)
                {
                    Console.WriteLine("Couldn't find packages");
                    return 1;
                }

                if (DockerCleanUp)
                {
                    foreach (var package in page.Packages)
                    {
                        if (package.Versions.Count == 1 && package.Versions[0] is { } version && version.Version == "docker-base-layer")
                        {
                            Console.WriteLine($"Cleaning up '{package.Name}'");
                            var success = await DeletePackageVersion(token, version.Id, cancellationToken);
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

                foreach (var package in page.Packages)
                {
                    Console.WriteLine(package.Name);
                    foreach (var packageVersion in package.Versions.Where(v => Version is null || v.Version == Version))
                    {
                        if (Force)
                        {
                            Console.WriteLine($"  Deleting '{packageVersion.Version}'");
                            await DeletePackageVersion(token, packageVersion.Id, cancellationToken);
                            packagesDeleted++;
                        }
                        else
                        {
                            Console.WriteLine($"  {packageVersion.Version}");
                        }
                    }
                }

                if (!page.HasNextPage)
                {
                    break;
                }

                after = page.EndCursor;
            }

            if (!Force)
            {
                Console.WriteLine();
                Console.WriteLine("To delete these package versions, use the --force option.");
            }

            return packagesDeleted >= 0 ? 0 : 1;
        }

        static async Task<bool> DeletePackageVersion(string token, string versionId, CancellationToken cancellationToken)
        {
            try
            {
                return await GraphQLUtilities.DeletePackageVersion(token, versionId, cancellationToken);
            }
            catch (ApplicationException e)
            {
                Console.WriteLine(e.Message);
                return false;
            }
        }
    }

    public class ListCommand : GprCommandBase
    {
        public string PackageOwner { get; set; }

        public override async Task<int> ExecuteAsync(CancellationToken cancellationToken)
        {
            if (PackageOwner is null)
            {
                Console.WriteLine("Please include a packages path");
                return 1;
            }

            var token = GetAccessToken();
            string after = null;

            while (true)
            {
                var (_, page) = await GraphQLUtilities.FindPackagesPage(token, PackageOwner, after, cancellationToken);
                if (page == null || page.Packages.Count == 0)
                {
                    Console.WriteLine("Couldn't find packages");
                    return 1;
                }

                var groups = page.Packages.GroupBy(p => p.RepositoryUrl);
                foreach (var group in groups.OrderBy(g => g.Key))
                {
                    Console.WriteLine(group.Key);
                    foreach (var package in group)
                    {
                        Console.WriteLine($"    {package.Name} ({package.PackageType}) [{string.Join(", ", package.Versions.Select(v => v.Version))}] ({package.DownloadsTotalCount} downloads)");
                    }
                }

                if (!page.HasNextPage)
                {
                    break;
                }

                after = page.EndCursor;
            }

            return 0;
        }
    }

    public class PushCommand : GprCommandBase
    {
        public string[] GlobPatterns { get; set; }
        public string RepositoryUrl { get; set; }
        public string Version { get; set; }
        public int Concurrency { get; set; } = 4;
        public int Retries { get; set; } = 3;
        public bool SubdomainIsolationOff { get; set; }

        public override async Task<int> ExecuteAsync(CancellationToken cancellationToken)
        {
            if (GlobPatterns is null || GlobPatterns.Length == 0)
            {
                Console.WriteLine("Please include a package path or glob pattern.");
                return 1;
            }

            PackageVersion packageVersion = null;
            if (Version != null && !PackageVersion.TryParse(Version, out packageVersion))
            {
                Console.WriteLine($"Invalid version: {Version}");
                return 1;
            }

            var currentDirectory = Directory.GetCurrentDirectory();
            var packageFiles = currentDirectory
                .GetFilesByGlobPatterns(GlobPatterns, out var glob)
                .Select(x => NuGetUtilities.BuildPackageFile(x, RepositoryUrl, !SubdomainIsolationOff))
                .ToList();

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
                    Console.WriteLine($"Package file was not found: {packageFile.FilenameAbsolutePath}");
                    return 1;
                }

                var built = RepositoryUrl == null
                    ? NuGetUtilities.BuildOwnerAndRepositoryFromUrlFromNupkg(packageFile, !SubdomainIsolationOff)
                    : NuGetUtilities.BuildOwnerAndRepositoryFromUrl(packageFile, RepositoryUrl, !SubdomainIsolationOff);

                if (!built)
                {
                    Console.WriteLine(RepositoryUrl == null
                        ? "Could not build owner and repository for provided package"
                        : "Could not build owner and repository for provided package and repository url");
                    return 1;
                }

                if (packageFile.Owner == null || packageFile.RepositoryName == null || packageFile.RepositoryUrl == null)
                {
                    Console.WriteLine(
                        $"Project is missing a valid <RepositoryUrl /> XML element value: {packageFile.RepositoryUrl}. " +
                        $"Package filename: {packageFile.FilenameAbsolutePath} " +
                        "Please use --repository option to set a valid upstream GitHub repository. " +
                        "Additional details are available at: https://docs.microsoft.com/en-us/dotnet/core/tools/csproj#repositoryurl");
                    return 1;
                }
            }

            var token = GetAccessToken();

            await packageFiles.ForEachAsync(
                (packageFile, packageCancellationToken) => UploadPackageAsync(packageFile, packageVersion, token, packageCancellationToken),
                (packageFile, exception) => { Console.WriteLine($"[{packageFile.Filename}]: {exception.Message}"); },
                cancellationToken,
                Math.Max(1, Concurrency));

            return packageFiles.All(x => x.IsUploaded) ? 0 : 1;
        }

        async Task UploadPackageAsync(PackageFile packageFile, PackageVersion packageVersion, string token, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (NuGetUtilities.ShouldRewriteNupkg(packageFile, packageVersion))
            {
                NuGetUtilities.RewriteNupkg(packageFile, packageVersion);
            }

            var manifest = NuGetUtilities.ReadNupkgManifest(packageFile.FilenameAbsolutePath);
            var packageBytes = File.ReadAllBytes(packageFile.FilenameAbsolutePath);

            Console.WriteLine($"[{packageFile.Filename}]: Repository url: {packageFile.RepositoryUrl}. Version: {manifest.Metadata.Version}. Size: {packageBytes.Length} bytes. ");

            using var response = await ExecuteWithRetryAsync(
                Math.Max(0, Retries),
                10,
                300,
                retryCancellationToken => UploadPackageAsyncImpl(packageFile, packageBytes, token, retryCancellationToken),
                cancellationToken);

            packageFile.IsUploaded = response.StatusCode == HttpStatusCode.OK;
        }

        static async Task<HttpResponseMessage> UploadPackageAsyncImpl(PackageFile packageFile, byte[] packageBytes, string token, CancellationToken cancellationToken)
        {
            using var client = CreateHttpClient();
            using var request = new HttpRequestMessage(HttpMethod.Put, $"https://{packageFile.NugetPackageEndpoint}/{packageFile.Owner}/");
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic",
                Convert.ToBase64String(Encoding.UTF8.GetBytes($"GprTool:{token}")));

            using var form = new MultipartFormDataContent();
            using var fileContent = new ByteArrayContent(packageBytes);
            fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            form.Add(fileContent, "package", packageFile.Filename);
            request.Content = form;

            Console.WriteLine($"[{packageFile.Filename}]: Uploading package.");

            var response = await client.SendAsync(request, cancellationToken);

            if (response.StatusCode == HttpStatusCode.OK)
            {
                Console.WriteLine($"[{packageFile.Filename}]: {await response.Content.ReadAsStringAsync()}");
                return response;
            }

            if (FindWarning(response) is { } warning)
            {
                Console.WriteLine($"[{packageFile.Filename}]: {warning}");
                return response;
            }

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                Console.WriteLine($"[{packageFile.Filename}]: {response.ReasonPhrase}");
                Console.WriteLine($"[{packageFile.Filename}]: Check that '{packageFile.RepositoryUrl}' exists");
                return response;
            }

            Console.WriteLine($"[{packageFile.Filename}]: {response.ReasonPhrase}");
            foreach (var header in response.Headers)
            {
                Console.WriteLine($"[{packageFile.Filename}]: {header.Key}: {string.Join(", ", header.Value)}");
            }

            return response;
        }

        static async Task<HttpResponseMessage> ExecuteWithRetryAsync(
            int retryNumber,
            int retrySleepSeconds,
            int timeoutSeconds,
            Func<CancellationToken, Task<HttpResponseMessage>> action,
            CancellationToken cancellationToken)
        {
            for (var attempt = 0; ; attempt++)
            {
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutCts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

                HttpResponseMessage response;
                try
                {
                    response = await action(timeoutCts.Token);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && attempt < retryNumber)
                {
                    await Task.Delay(TimeSpan.FromSeconds(retrySleepSeconds), cancellationToken);
                    continue;
                }

                if (!ShouldRetry(response) || attempt >= retryNumber)
                {
                    return response;
                }

                response.Dispose();
                await Task.Delay(TimeSpan.FromSeconds(retrySleepSeconds), cancellationToken);
            }
        }

        static bool ShouldRetry(HttpResponseMessage response)
        {
            return FindWarning(response) is null
                   && response.StatusCode != HttpStatusCode.NotFound
                   && response.StatusCode != HttpStatusCode.Unauthorized
                   && response.StatusCode != HttpStatusCode.Conflict
                   && response.StatusCode != HttpStatusCode.BadRequest
                   && response.StatusCode != HttpStatusCode.OK;
        }
    }

    public class DetailsCommand : GprCommandBase
    {
        public string Owner { get; set; }
        public string Name { get; set; }
        public string Version { get; set; }

        public override async Task<int> ExecuteAsync(CancellationToken cancellationToken)
        {
            using var client = CreateHttpClient();
            using var request = new HttpRequestMessage(HttpMethod.Get, $"https://nuget.pkg.github.com/{Owner}/{Name}/{Version}.json");
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic",
                Convert.ToBase64String(Encoding.UTF8.GetBytes($"GprTool:{GetAccessToken()}")));

            using var response = await client.SendAsync(request, cancellationToken);

            if (response.StatusCode == HttpStatusCode.OK)
            {
                var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                var json = JsonSerializer.Serialize(doc, new JsonSerializerOptions { WriteIndented = true });
                Console.WriteLine(json);
                return 0;
            }

            if (FindWarning(response) is { } warning)
            {
                Console.WriteLine(warning);
                return 1;
            }

            Console.WriteLine(response.ReasonPhrase);
            foreach (var header in response.Headers)
            {
                Console.WriteLine($"{header.Key}: {string.Join(", ", header.Value)}");
            }

            return 1;
        }
    }

    public class SetApiKeyCommand : GprCommandBase
    {
        public string ApiKey { get; set; }
        public string PackageSource { get; set; }
        public string ConfigFile { get; set; }

        public override async Task<int> ExecuteAsync(CancellationToken cancellationToken)
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
    }

    public class EncodeCommand : GprCommandBase
    {
        public string Token { get; set; }

        public override Task<int> ExecuteAsync(CancellationToken cancellationToken)
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
            Console.WriteLine("A Maven `pom.xml` file:");
            Console.WriteLine(@$"<repositories>
  <repository>
    <id>github-public</id>
    <url>https://public:{xmlEncoded}@maven.pkg.github.com/<OWNER>/*</url>
  </repository>
</repositories>");

            var unicodeEncode = UnicodeEncode(Token);

            Console.WriteLine();
            Console.WriteLine("An npm `.npmrc` file:");
            Console.WriteLine("@OWNER:registry=https://npm.pkg.github.com");
            Console.WriteLine($"//npm.pkg.github.com/:_authToken=\"{unicodeEncode}\"");
            Console.WriteLine();

            return Task.FromResult(0);
        }

        static string XmlEncode(string str) => $"&#{(int)str[0]};" + str.Substring(1);
        static string UnicodeEncode(string str) => $"\\u{((int)str[0]).ToString("x4")}" + str.Substring(1);
    }

    public abstract class GprCommandBase
    {
        public string AccessToken { get; set; }

        public abstract Task<int> ExecuteAsync(CancellationToken cancellationToken);

        public string GetAccessToken()
        {
            if (AccessToken is { } accessToken)
            {
                return accessToken.Trim();
            }

            if (FindEnvironmentVariableToken("GITHUB_TOKEN") is { } gitHubToken)
            {
                return gitHubToken.Trim();
            }

            if (NuGetUtilities.FindTokenInNuGetConfig(Warning) is { } configToken)
            {
                return configToken.Trim();
            }

            if (FindEnvironmentVariableToken("READ_PACKAGES_TOKEN") is { } readToken)
            {
                return readToken.Trim();
            }

            throw new ApplicationException("Couldn't find personal access token");
        }

        static string FindEnvironmentVariableToken(string name) =>
            Environment.GetEnvironmentVariable(name) is { } token && token != string.Empty ? token.Trim() : null;

        protected void Warning(string line) => Console.WriteLine(line);

        protected static HttpClient CreateHttpClient()
        {
            var client = new HttpClient();
            client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue(Program.AssemblyProduct, Program.AssemblyInformationalVersion));
            return client;
        }

        protected static string FindWarning(HttpResponseMessage response)
        {
            return response.Headers.TryGetValues("X-Nuget-Warning", out var values)
                ? values.FirstOrDefault()
                : null;
        }
    }

    sealed class HelpRequestedException : Exception
    {
    }
}
