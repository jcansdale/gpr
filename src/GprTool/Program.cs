using System;
using System.Threading.Tasks;
using McMaster.Extensions.CommandLineUtils;

namespace GprTool
{
    [Command("gpr")]
    [Subcommand(
        typeof(ListCommand)
    )]
    public class Program : GprCommandBase
    {
        public static Task Main(string[] args) =>
            CommandLineApplication.ExecuteAsync<Program>(args);

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
        protected override Task OnExecute(CommandLineApplication app)
        {
            Console.WriteLine($"Hello, {Owner}!");
            return Task.CompletedTask;
        }

        [Argument(0, Description = "The owner (user or org) of the packages to list")]
        public string Owner { get; set; } = "you";
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

        [Option("-k|--api-key", Description = "The access token to use")]
        public string AccessToken { get; }
    }
}
