using System.CommandLine;
using CyberdineDevelopment.ClaudeCode.CLI.Services;
using Microsoft.Extensions.DependencyInjection;

namespace CyberdineDevelopment.ClaudeCode.CLI.Commands;

/// <summary>
/// Command for interactive chat with Claude.
/// </summary>
public sealed class ChatCommand : Command
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ChatCommand"/> class.
    /// </summary>
    public ChatCommand() : base("chat", "Start an interactive chat session with Claude")
    {
        var messageOption = new Option<string>(
            ["--message", "-m"],
            "Send a single message instead of starting interactive mode");

        var modelOption = new Option<string>(
            ["--model"],
            "Override the default model to use");

        var serverOption = new Option<string>(
            ["--server", "-s"],
            "MCP server to connect to");

        AddOption(messageOption);
        AddOption(modelOption);
        AddOption(serverOption);

        this.SetHandler(async (string? message, string? model, string? server) =>
        {
            var chatService = Program.ServiceProvider.GetRequiredService<IChatService>();

            Console.WriteLine("Claude Code Chat");
            Console.WriteLine("================");
            Console.WriteLine();

            if (!string.IsNullOrEmpty(message))
            {
                await HandleSingleMessageAsync(chatService, message, model, server);
                return;
            }

            await HandleInteractiveChatAsync(chatService, model, server);
        }, messageOption, modelOption, serverOption);
    }

    private static async Task HandleSingleMessageAsync(IChatService chatService, string message, string? model, string? server)
    {
        try
        {
            Console.WriteLine($"User: {message}");
            Console.Write("Assistant: ");

            var response = await chatService.SendMessageAsync(message, model, server);
            Console.WriteLine(response);
            Console.WriteLine();
        }
        catch (HttpRequestException ex)
        {
            Console.WriteLine($"Error: Failed to connect to Claude API. {ex.Message}");
            Console.WriteLine("Please check your API key configuration.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
        }
    }

    private static async Task HandleInteractiveChatAsync(IChatService chatService, string? model, string? server)
    {
        Console.WriteLine("Interactive chat mode (type 'exit' to quit):");
        Console.WriteLine();

        while (true)
        {
            Console.Write("You: ");
            var userInput = Console.ReadLine();

            if (string.IsNullOrWhiteSpace(userInput) || userInput.Equals("exit", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine("Goodbye!");
                break;
            }

            try
            {
                Console.Write("Assistant: ");
                var response = await chatService.SendMessageAsync(userInput, model, server);
                Console.WriteLine(response);
                Console.WriteLine();
            }
            catch (HttpRequestException ex)
            {
                Console.WriteLine($"Error: Failed to connect to Claude API. {ex.Message}");
                Console.WriteLine("Please check your API key configuration.");
                break;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                Console.WriteLine();
            }
        }
    }
}