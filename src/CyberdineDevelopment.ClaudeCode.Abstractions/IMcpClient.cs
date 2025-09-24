namespace CyberdineDevelopment.ClaudeCode.Abstractions;

/// <summary>
/// Defines the contract for MCP (Model Context Protocol) client operations.
/// </summary>
public interface IMcpClient : IDisposable
{
    /// <summary>
    /// Gets a value indicating whether the client is currently connected.
    /// </summary>
    bool IsConnected { get; }

    /// <summary>
    /// Connects to an MCP server.
    /// </summary>
    /// <param name="serverInfo">Information about the server to connect to.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the connection operation.</returns>
    Task ConnectAsync(McpServerInfo serverInfo, CancellationToken cancellationToken = default);

    /// <summary>
    /// Disconnects from the MCP server.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the disconnection operation.</returns>
    Task DisconnectAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists available tools from the connected server.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A collection of available tools.</returns>
    Task<IReadOnlyCollection<McpToolInfo>> ListToolsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Calls a tool on the connected server.
    /// </summary>
    /// <param name="toolName">The name of the tool to call.</param>
    /// <param name="arguments">Arguments to pass to the tool.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The result of the tool call.</returns>
    Task<McpToolResult> CallToolAsync(string toolName, object? arguments = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists available resources from the connected server.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A collection of available resources.</returns>
    Task<IReadOnlyCollection<McpResourceInfo>> ListResourcesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads a resource from the connected server.
    /// </summary>
    /// <param name="uri">The URI of the resource to read.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The resource content.</returns>
    Task<McpResourceContent> ReadResourceAsync(Uri uri, CancellationToken cancellationToken = default);

    /// <summary>
    /// Event fired when the connection status changes.
    /// </summary>
    event EventHandler<McpConnectionEventArgs> ConnectionChanged;

    /// <summary>
    /// Event fired when a message is received from the server.
    /// </summary>
    event EventHandler<McpMessageEventArgs> MessageReceived;
}