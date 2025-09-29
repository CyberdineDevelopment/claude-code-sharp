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
    private static IServiceProvider? _serviceProvider;

    /// <summary>
    /// Gets the current service provider.
    /// </summary>
    public static IServiceProvider ServiceProvider =>
        _serviceProvider ?? throw new InvalidOperationException("Service provider not initialized");

    /// <summary>
    /// Application entry point.
    /// </summary>
    /// <param name="args">Command line arguments.</param>
    /// <returns>Exit code.</returns>
    public static async Task<int> Main(string[] args)
    {
        try
        {
            var builder = Host.CreateApplicationBuilder(args);
            ConfigureServices(builder.Services, builder.Configuration);

            using var host = builder.Build();
            _serviceProvider = host.Services;

            await host.StartAsync().ConfigureAwait(false);

            var rootCommand = BuildRootCommand();
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
        rootCommand.AddCommand(AuthCommand.Create());

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

        // Authentication Service
        services.AddSingleton<IAuthenticationService>(serviceProvider =>
        {
            var httpClientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();
            var httpClient = httpClientFactory.CreateClient();
            var logger = serviceProvider.GetRequiredService<ILogger<AuthenticationService>>();
            return new AuthenticationService(httpClient, logger);
        });

        // Anthropic Client Factory (chooses between API key or subscription)
        services.AddTransient<IAnthropicClient>(serviceProvider =>
        {
            var httpClientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();
            var config = serviceProvider.GetRequiredService<IConfiguration>();
            var authService = serviceProvider.GetRequiredService<IAuthenticationService>();

            // Check if user is authenticated with subscription
            var authState = authService.GetAuthenticationStateAsync().Result;
            if (authState.IsAuthenticated && authState.Method == AuthenticationMethod.Subscription)
            {
                var httpClient = httpClientFactory.CreateClient();
                var logger = serviceProvider.GetRequiredService<ILogger<SubscriptionAnthropicClient>>();
                return new SubscriptionAnthropicClient(httpClient, authService, logger);
            }

            // Fall back to API key authentication
            var apiHttpClient = httpClientFactory.CreateClient();
            var apiLogger = serviceProvider.GetRequiredService<ILogger<AnthropicClient>>();

            var apiKey = config["ClaudeCode:Anthropic:ApiKey"] ??
                         Environment.GetEnvironmentVariable("CLAUDECODE__ANTHROPIC__APIKEY") ??
                         Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY") ??
                         "dummy-key-for-testing"; // Allow creation but will fail on actual use

            return new AnthropicClient(apiHttpClient, apiKey, apiLogger);
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
