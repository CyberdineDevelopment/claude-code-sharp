using System.CommandLine;
using CyberdineDevelopment.ClaudeCode.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CyberdineDevelopment.ClaudeCode.CLI.Commands;

/// <summary>
/// Authentication commands for login and logout.
/// </summary>
public static class AuthCommand
{
    /// <summary>
    /// Creates the auth command with login and logout subcommands.
    /// </summary>
    /// <returns>The configured auth command.</returns>
    public static Command Create()
    {
        var authCommand = new Command("auth", "Authentication commands");

        // Add login subcommand
        var loginCommand = new Command("login", "Login to Claude.ai with your subscription");
        loginCommand.SetHandler(HandleLoginAsync);
        authCommand.AddCommand(loginCommand);

        // Add logout subcommand
        var logoutCommand = new Command("logout", "Logout and clear stored credentials");
        logoutCommand.SetHandler(HandleLogoutAsync);
        authCommand.AddCommand(logoutCommand);

        // Add status subcommand
        var statusCommand = new Command("status", "Show current authentication status");
        statusCommand.SetHandler(HandleStatusAsync);
        authCommand.AddCommand(statusCommand);

        return authCommand;
    }

    private static async Task HandleLoginAsync()
    {
        try
        {
            var serviceProvider = Program.ServiceProvider
                ?? throw new InvalidOperationException("Service provider not initialized");

            var authService = serviceProvider.GetRequiredService<IAuthenticationService>();
            var logger = serviceProvider.GetRequiredService<ILogger<IAuthenticationService>>();

            Console.WriteLine("Starting login process...");

            var authState = await authService.LoginAsync();

            if (authState.IsAuthenticated && authState.User != null)
            {
                Console.WriteLine($"✓ Successfully logged in as {authState.User.Email}");
                if (!string.IsNullOrEmpty(authState.User.Plan))
                {
                    Console.WriteLine($"  Plan: {authState.User.Plan}");
                }
                if (!string.IsNullOrEmpty(authState.User.Name))
                {
                    Console.WriteLine($"  Name: {authState.User.Name}");
                }
            }
            else
            {
                Console.WriteLine("✗ Login failed");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"✗ Login failed: {ex.Message}");
        }
    }

    private static async Task HandleLogoutAsync()
    {
        try
        {
            var serviceProvider = Program.ServiceProvider
                ?? throw new InvalidOperationException("Service provider not initialized");

            var authService = serviceProvider.GetRequiredService<IAuthenticationService>();

            await authService.LogoutAsync();
            Console.WriteLine("✓ Successfully logged out");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"✗ Logout failed: {ex.Message}");
        }
    }

    private static async Task HandleStatusAsync()
    {
        try
        {
            var serviceProvider = Program.ServiceProvider
                ?? throw new InvalidOperationException("Service provider not initialized");

            var authService = serviceProvider.GetRequiredService<IAuthenticationService>();

            var authState = await authService.GetAuthenticationStateAsync();

            Console.WriteLine("Authentication Status:");
            Console.WriteLine($"  Authenticated: {(authState.IsAuthenticated ? "Yes" : "No")}");
            Console.WriteLine($"  Method: {authState.Method}");

            if (authState.IsAuthenticated && authState.User != null)
            {
                Console.WriteLine($"  User: {authState.User.Email}");
                if (!string.IsNullOrEmpty(authState.User.Plan))
                {
                    Console.WriteLine($"  Plan: {authState.User.Plan}");
                }
                if (!string.IsNullOrEmpty(authState.User.Name))
                {
                    Console.WriteLine($"  Name: {authState.User.Name}");
                }

                if (authState.Token != null)
                {
                    Console.WriteLine($"  Token Expires: {authState.Token.ExpiresAt:yyyy-MM-dd HH:mm:ss}");
                    Console.WriteLine($"  Needs Refresh: {(authState.Token.NeedsRefresh ? "Yes" : "No")}");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"✗ Failed to get status: {ex.Message}");
        }
    }
}