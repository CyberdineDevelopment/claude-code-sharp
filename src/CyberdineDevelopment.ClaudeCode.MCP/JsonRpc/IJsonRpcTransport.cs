namespace CyberdineDevelopment.ClaudeCode.MCP.JsonRpc;

/// <summary>
/// Defines the contract for JSON-RPC transport mechanisms.
/// </summary>
public interface IJsonRpcTransport : IDisposable
{
    /// <summary>
    /// Gets a value indicating whether the transport is connected.
    /// </summary>
    bool IsConnected { get; }

    /// <summary>
    /// Connects the transport.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the connection operation.</returns>
    Task ConnectAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Disconnects the transport.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the disconnection operation.</returns>
    Task DisconnectAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a JSON-RPC message.
    /// </summary>
    /// <param name="message">The message to send.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the send operation.</returns>
    Task SendAsync(JsonRpcMessage message, CancellationToken cancellationToken = default);

    /// <summary>
    /// Receives JSON-RPC messages.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An async enumerable of received messages.</returns>
    IAsyncEnumerable<JsonRpcMessage> ReceiveAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Event fired when the connection status changes.
    /// </summary>
    event EventHandler<TransportConnectionEventArgs> ConnectionChanged;
}

/// <summary>
/// Event arguments for transport connection changes.
/// </summary>
public sealed class TransportConnectionEventArgs : EventArgs
{
    /// <summary>
    /// Gets a value indicating whether the transport is connected.
    /// </summary>
    public required bool IsConnected { get; init; }

    /// <summary>
    /// Gets error information if connection failed.
    /// </summary>
    public Exception? Error { get; init; }
}