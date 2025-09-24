namespace CyberdineDevelopment.ClaudeCode.Abstractions;

/// <summary>
/// Event arguments for MCP connection status changes.
/// </summary>
public sealed class McpConnectionEventArgs : EventArgs
{
    /// <summary>
    /// Gets the new connection status.
    /// </summary>
    public required bool IsConnected { get; init; }

    /// <summary>
    /// Gets information about the server if connected.
    /// </summary>
    public McpServerInfo? ServerInfo { get; init; }

    /// <summary>
    /// Gets error information if connection failed.
    /// </summary>
    public McpError? Error { get; init; }
}

/// <summary>
/// Event arguments for MCP messages.
/// </summary>
public sealed class McpMessageEventArgs : EventArgs
{
    /// <summary>
    /// Gets the direction of the message.
    /// </summary>
    public required McpMessageDirection Direction { get; init; }

    /// <summary>
    /// Gets the raw message content.
    /// </summary>
    public required string Message { get; init; }

    /// <summary>
    /// Gets the timestamp when the message was sent/received.
    /// </summary>
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Defines the direction of an MCP message.
/// </summary>
public enum McpMessageDirection
{
    /// <summary>
    /// Message sent to the server.
    /// </summary>
    Outbound,

    /// <summary>
    /// Message received from the server.
    /// </summary>
    Inbound
}