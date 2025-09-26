using CyberdineDevelopment.ClaudeCode.Abstractions;
using CyberdineDevelopment.ClaudeCode.CLI.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CyberdineDevelopment.ClaudeCode.CLI.Services;

/// <summary>
/// Service for chat interactions.
/// </summary>
public interface IChatService
{
    /// <summary>
    /// Sends a message and gets a response.
    /// </summary>
    /// <param name="message">The message to send.</param>
    /// <param name="model">The model to use (optional).</param>
    /// <param name="serverId">The MCP server to use (optional).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The response from Claude.</returns>
    Task<string> SendMessageAsync(string message, string? model = null, string? serverId = null, CancellationToken cancellationToken = default);
}

/// <summary>
/// Implementation of chat service.
/// </summary>
public sealed class ChatService : IChatService
{
    private readonly IAnthropicClient _anthropicClient;
    private readonly ClaudeCodeConfiguration _config;
    private readonly ILogger<ChatService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ChatService"/> class.
    /// </summary>
    /// <param name="anthropicClient">The Anthropic API client.</param>
    /// <param name="config">The configuration.</param>
    /// <param name="logger">The logger.</param>
    public ChatService(
        IAnthropicClient anthropicClient,
        IOptions<ClaudeCodeConfiguration> config,
        ILogger<ChatService> logger)
    {
        _anthropicClient = anthropicClient ?? throw new ArgumentNullException(nameof(anthropicClient));
        _config = config.Value ?? throw new ArgumentNullException(nameof(config));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<string> SendMessageAsync(string message, string? model = null, string? serverId = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        try
        {
            _logger.LogDebug("Sending message to Claude: {Message}", message);

            var request = new ChatRequest
            {
                Model = model ?? _config.DefaultModel,
                Messages = new[]
                {
                    new ChatMessage
                    {
                        Role = "user",
                        Content = new ContentBlock[] { new TextContent { Text = message } }
                    }
                },
                MaxTokens = _config.MaxTokens,
                Temperature = _config.Temperature
            };

            var response = await _anthropicClient.SendMessageAsync(request, cancellationToken);

            var textContent = response.Content.OfType<TextContent>().FirstOrDefault();
            if (textContent == null)
            {
                throw new InvalidOperationException("No text content received from Claude");
            }

            _logger.LogDebug("Received response from Claude: {TokensUsed} tokens used",
                response.Usage?.InputTokens + response.Usage?.OutputTokens);

            return textContent.Text;
        }
        catch (HttpRequestException ex) when (ex.Message.Contains("401") || ex.Message.Contains("Unauthorized"))
        {
            _logger.LogError("Invalid API key or unauthorized access");
            throw new InvalidOperationException("Invalid Anthropic API key. Please check your configuration.", ex);
        }
        catch (HttpRequestException ex) when (ex.Message.Contains("429"))
        {
            _logger.LogError("Rate limit exceeded");
            throw new InvalidOperationException("Rate limit exceeded. Please try again later.", ex);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "HTTP error communicating with Anthropic API");
            throw new InvalidOperationException($"Failed to communicate with Anthropic API: {ex.Message}", ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send message to Claude");
            throw;
        }
    }
}

/// <summary>
/// Service for configuration management.
/// </summary>
public interface IConfigurationService
{
    /// <summary>
    /// Gets the current configuration as a formatted string.
    /// </summary>
    /// <returns>The configuration as a string.</returns>
    Task<string> GetConfigurationAsync();
}

/// <summary>
/// Implementation of configuration service.
/// </summary>
public sealed class ConfigurationService : IConfigurationService
{
    /// <inheritdoc />
    public Task<string> GetConfigurationAsync()
    {
        return Task.FromResult("Configuration would be displayed here");
    }
}