namespace CyberdineDevelopment.ClaudeCode.Abstractions;

/// <summary>
/// Contains information about an MCP server.
/// </summary>
public sealed record McpServerInfo
{
    /// <summary>
    /// Gets the unique identifier for the server.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// Gets the display name of the server.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Gets the description of what the server provides.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Gets the version of the server.
    /// </summary>
    public string? Version { get; init; }

    /// <summary>
    /// Gets the transport configuration for connecting to the server.
    /// </summary>
    public required McpTransport Transport { get; init; }

    /// <summary>
    /// Gets additional configuration settings for the server.
    /// </summary>
    public Dictionary<string, object> Settings { get; init; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Gets a value indicating whether the server should be started automatically.
    /// </summary>
    public bool AutoStart { get; init; } = true;

    /// <summary>
    /// Gets the timeout for connection attempts.
    /// </summary>
    public TimeSpan ConnectionTimeout { get; init; } = TimeSpan.FromSeconds(30);
}