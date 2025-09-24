namespace CyberdineDevelopment.ClaudeCode.CLI.Configuration;

/// <summary>
/// Configuration for the Claude Code application.
/// </summary>
public sealed class ClaudeCodeConfiguration
{
    /// <summary>
    /// Gets or sets the Anthropic API configuration.
    /// </summary>
    public AnthropicConfiguration Anthropic { get; set; } = new();

    /// <summary>
    /// Gets or sets the MCP server configurations.
    /// </summary>
    public Dictionary<string, McpServerConfiguration> Servers { get; set; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Gets or sets the default model to use.
    /// </summary>
    public string DefaultModel { get; set; } = "claude-3-5-sonnet-20241022";

    /// <summary>
    /// Gets or sets the maximum number of tokens for responses.
    /// </summary>
    public int MaxTokens { get; set; } = 4096;

    /// <summary>
    /// Gets or sets the temperature for response generation.
    /// </summary>
    public double Temperature { get; set; } = 0.7;
}

/// <summary>
/// Configuration for Anthropic API.
/// </summary>
public sealed class AnthropicConfiguration
{
    /// <summary>
    /// Gets or sets the API key.
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// Gets or sets the timeout for API requests.
    /// </summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromMinutes(2);
}

/// <summary>
/// Configuration for an MCP server.
/// </summary>
public sealed class McpServerConfiguration
{
    /// <summary>
    /// Gets or sets the server name.
    /// </summary>
    public required string Name { get; set; }

    /// <summary>
    /// Gets or sets the server description.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Gets or sets the command to start the server.
    /// </summary>
    public required string Command { get; set; }

    /// <summary>
    /// Gets or sets the arguments for the command.
    /// </summary>
    public List<string> Arguments { get; set; } = new();

    /// <summary>
    /// Gets or sets the working directory for the server.
    /// </summary>
    public string? WorkingDirectory { get; set; }

    /// <summary>
    /// Gets or sets environment variables for the server.
    /// </summary>
    public Dictionary<string, string> Environment { get; set; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Gets or sets a value indicating whether the server should start automatically.
    /// </summary>
    public bool AutoStart { get; set; } = true;
}