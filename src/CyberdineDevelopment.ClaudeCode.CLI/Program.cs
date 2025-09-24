using System.CommandLine;
using CyberdineDevelopment.ClaudeCode.Abstractions;
using CyberdineDevelopment.ClaudeCode.Anthropic;
using CyberdineDevelopment.ClaudeCode.CLI.Commands;
using CyberdineDevelopment.ClaudeCode.CLI.Configuration;
using CyberdineDevelopment.ClaudeCode.CLI.Services;
using CyberdineDevelopment.ClaudeCode.MCP;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CyberdineDevelopment.ClaudeCode.CLI;

internal static class Program
{
    /// <summary>
    /// Application entry point.
    /// </summary>
    /// <param name="args">Command line arguments.</param>
    /// <returns>Exit code.</returns>
    public static async Task<int> Main(string[] args)
    {
        try
        {
            var rootCommand = BuildRootCommand();
            var builder = Host.CreateApplicationBuilder(args);

            ConfigureServices(builder.Services, builder.Configuration);

            using var host = builder.Build();
            await host.StartAsync().ConfigureAwait(false);

            return await rootCommand.InvokeAsync(args).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Fatal error: {ex.Message}");
            return 1;
        }
    }

    private static RootCommand BuildRootCommand()
    {
        var rootCommand = new RootCommand("Claude Code - Professional MCP-based Claude client")
        {
            Name = "claude-code"
        };

        // Add commands
        rootCommand.AddCommand(new ChatCommand());
        rootCommand.AddCommand(new ServerCommand());
        rootCommand.AddCommand(new ConfigCommand());

        return rootCommand;
    }

    private static void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        // Configuration
        services.Configure<ClaudeCodeConfiguration>(configuration.GetSection("ClaudeCode"));

        // Logging
        services.AddLogging(builder =>
        {
            builder.AddConsole();
        });

        // HTTP Client for Anthropic API
        services.AddHttpClient();
        services.AddTransient<IAnthropicClient>(serviceProvider =>
        {
            var httpClientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();
            var httpClient = httpClientFactory.CreateClient();
            var config = serviceProvider.GetRequiredService<IConfiguration>();
            var logger = serviceProvider.GetRequiredService<ILogger<AnthropicClient>>();

            var apiKey = config["ClaudeCode:Anthropic:ApiKey"]
                ?? throw new InvalidOperationException("Anthropic API key is not configured");

            return new AnthropicClient(httpClient, apiKey, logger);
        });

        // MCP Services
        services.AddTransient<IMcpClient>(serviceProvider =>
        {
            var logger = serviceProvider.GetRequiredService<ILogger<McpClient>>();
            var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
            return new McpClient(logger, loggerFactory);
        });
        services.AddSingleton<IMcpServerManager, McpServerManager>();

        // CLI Services
        services.AddTransient<IChatService, ChatService>();
        services.AddTransient<IConfigurationService, ConfigurationService>();
    }
}
