namespace CyberdineDevelopment.ClaudeCode.Abstractions;

/// <summary>
/// Defines the transport configuration for MCP communication.
/// </summary>
public abstract record McpTransport
{
    /// <summary>
    /// Gets the type of transport.
    /// </summary>
    public abstract McpTransportType Type { get; }
}

/// <summary>
/// Defines the types of MCP transport available.
/// </summary>
public enum McpTransportType
{
    /// <summary>
    /// Standard input/output transport.
    /// </summary>
    Stdio,

    /// <summary>
    /// HTTP-based transport.
    /// </summary>
    Http,

    /// <summary>
    /// WebSocket transport.
    /// </summary>
    WebSocket
}

/// <summary>
/// Standard input/output transport configuration.
/// </summary>
public sealed record McpStdioTransport : McpTransport
{
    /// <summary>
    /// Gets the command to execute to start the server.
    /// </summary>
    public required string Command { get; init; }

    /// <summary>
    /// Gets the arguments to pass to the command.
    /// </summary>
    public IReadOnlyList<string> Arguments { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Gets the working directory for the command.
    /// </summary>
    public string? WorkingDirectory { get; init; }

    /// <summary>
    /// Gets environment variables to set for the command.
    /// </summary>
    public Dictionary<string, string> Environment { get; init; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Gets the type of transport.
    /// </summary>
    public override McpTransportType Type => McpTransportType.Stdio;
}

/// <summary>
/// HTTP-based transport configuration.
/// </summary>
public sealed record McpHttpTransport : McpTransport
{
    /// <summary>
    /// Gets the base URL for the HTTP server.
    /// </summary>
    public required Uri BaseUrl { get; init; }

    /// <summary>
    /// Gets the HTTP headers to include in requests.
    /// </summary>
    public Dictionary<string, string> Headers { get; init; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Gets the type of transport.
    /// </summary>
    public override McpTransportType Type => McpTransportType.Http;
}

/// <summary>
/// WebSocket transport configuration.
/// </summary>
public sealed record McpWebSocketTransport : McpTransport
{
    /// <summary>
    /// Gets the WebSocket URL.
    /// </summary>
    public required Uri Url { get; init; }

    /// <summary>
    /// Gets the sub-protocols to request.
    /// </summary>
    public IReadOnlyList<string> SubProtocols { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Gets the type of transport.
    /// </summary>
    public override McpTransportType Type => McpTransportType.WebSocket;
}