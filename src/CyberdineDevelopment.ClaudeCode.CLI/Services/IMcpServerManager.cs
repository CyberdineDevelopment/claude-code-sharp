using CyberdineDevelopment.ClaudeCode.Abstractions;

namespace CyberdineDevelopment.ClaudeCode.CLI.Services;

/// <summary>
/// Manages MCP server lifecycle and connections.
/// </summary>
public interface IMcpServerManager : IDisposable
{
    /// <summary>
    /// Gets all configured servers.
    /// </summary>
    /// <returns>A collection of server information.</returns>
    Task<IReadOnlyCollection<McpServerInfo>> GetServersAsync();

    /// <summary>
    /// Gets a client for the specified server.
    /// </summary>
    /// <param name="serverId">The server identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The MCP client for the server.</returns>
    Task<IMcpClient> GetClientAsync(string serverId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Starts a server if it's not already running.
    /// </summary>
    /// <param name="serverId">The server identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the start operation.</returns>
    Task StartServerAsync(string serverId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops a running server.
    /// </summary>
    /// <param name="serverId">The server identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the stop operation.</returns>
    Task StopServerAsync(string serverId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the status of all servers.
    /// </summary>
    /// <returns>A dictionary of server statuses.</returns>
    Task<Dictionary<string, bool>> GetServerStatusesAsync();
}