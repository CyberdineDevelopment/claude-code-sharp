using System.CommandLine;

namespace CyberdineDevelopment.ClaudeCode.CLI.Commands;

/// <summary>
/// Command for managing configuration.
/// </summary>
public sealed class ConfigCommand : Command
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ConfigCommand"/> class.
    /// </summary>
    public ConfigCommand() : base("config", "Manage configuration")
    {
        var showCommand = new Command("show", "Show current configuration");
        showCommand.SetHandler(() =>
        {
            Console.WriteLine("Current Configuration:");
            Console.WriteLine("======================");
            Console.WriteLine("(This would display the current configuration)");
        });

        AddCommand(showCommand);
    }
}