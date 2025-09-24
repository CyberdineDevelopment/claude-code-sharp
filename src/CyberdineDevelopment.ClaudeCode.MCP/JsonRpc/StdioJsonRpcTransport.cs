using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using CyberdineDevelopment.ClaudeCode.Abstractions;
using Microsoft.Extensions.Logging;

namespace CyberdineDevelopment.ClaudeCode.MCP.JsonRpc;

/// <summary>
/// JSON-RPC transport that communicates via standard input/output with a process.
/// </summary>
public sealed class StdioJsonRpcTransport : IJsonRpcTransport
{
    private readonly ILogger<StdioJsonRpcTransport> _logger;
    private readonly McpStdioTransport _config;
    private readonly JsonSerializerOptions _jsonOptions;
    private Process? _process;
    private StreamWriter? _writer;
    private StreamReader? _reader;
    private volatile bool _disposed;
    private CancellationTokenSource? _receiveCts;

    /// <summary>
    /// Initializes a new instance of the <see cref="StdioJsonRpcTransport"/> class.
    /// </summary>
    /// <param name="config">The stdio transport configuration.</param>
    /// <param name="logger">The logger.</param>
    public StdioJsonRpcTransport(McpStdioTransport config, ILogger<StdioJsonRpcTransport> logger)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };
    }

    /// <inheritdoc />
    public bool IsConnected => _process?.HasExited == false;

    /// <inheritdoc />
    public event EventHandler<TransportConnectionEventArgs>? ConnectionChanged;

    /// <inheritdoc />
    public Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (IsConnected)
        {
            return Task.CompletedTask;
        }

        try
        {
            _logger.LogDebug("Starting process: {Command} {Arguments}", _config.Command, string.Join(" ", _config.Arguments));

            var startInfo = new ProcessStartInfo
            {
                FileName = _config.Command,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = _config.WorkingDirectory
            };

            foreach (var arg in _config.Arguments)
            {
                startInfo.ArgumentList.Add(arg);
            }

            foreach (var envVar in _config.Environment)
            {
                startInfo.Environment[envVar.Key] = envVar.Value;
            }

            _process = Process.Start(startInfo)
                ?? throw new InvalidOperationException($"Failed to start process: {_config.Command}");

            _writer = _process.StandardInput;
            _reader = _process.StandardOutput;
            _receiveCts = new CancellationTokenSource();

            _process.EnableRaisingEvents = true;
            _process.Exited += OnProcessExited;

            _logger.LogInformation("Process started successfully: PID {ProcessId}", _process.Id);
            OnConnectionChanged(true, null);
            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start process: {Command}", _config.Command);
            OnConnectionChanged(false, ex);
            throw;
        }
    }

    /// <inheritdoc />
    public Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        if (!IsConnected)
        {
            return Task.CompletedTask;
        }

        try
        {
            _receiveCts?.Cancel();

            _writer?.Close();
            _reader?.Close();

            if (_process?.HasExited == false)
            {
                _logger.LogDebug("Terminating process: PID {ProcessId}", _process.Id);

                // Give the process a chance to exit gracefully
                if (!_process.WaitForExit(5000))
                {
                    _process.Kill(entireProcessTree: true);
                }
            }

            OnConnectionChanged(false, null);
            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error during disconnect");
            OnConnectionChanged(false, ex);
            return Task.CompletedTask;
        }
    }

    /// <inheritdoc />
    public async Task SendAsync(JsonRpcMessage message, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!IsConnected)
        {
            throw new InvalidOperationException("Transport is not connected");
        }

        try
        {
            var json = JsonSerializer.Serialize(message, _jsonOptions);
            _logger.LogTrace("Sending JSON-RPC message: {Json}", json);

            await _writer!.WriteLineAsync(json.AsMemory(), cancellationToken).ConfigureAwait(false);
            await _writer.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send JSON-RPC message");
            throw;
        }
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<JsonRpcMessage> ReceiveAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!IsConnected)
        {
            yield break;
        }

        var combinedToken = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, _receiveCts?.Token ?? CancellationToken.None).Token;

        while (!combinedToken.IsCancellationRequested && IsConnected)
        {
            string? line;
            try
            {
                line = await _reader!.ReadLineAsync(combinedToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Expected cancellation
                yield break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reading from stream");
                OnConnectionChanged(false, ex);
                yield break;
            }

            if (line == null)
            {
                // End of stream
                break;
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var message = ParseMessage(line);
            if (message != null)
            {
                yield return message;
            }
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        DisconnectAsync().GetAwaiter().GetResult();

        _receiveCts?.Dispose();
        _process?.Dispose();
        _writer?.Dispose();
        _reader?.Dispose();
    }

    private void OnProcessExited(object? sender, EventArgs e)
    {
        _logger.LogInformation("Process exited: PID {ProcessId}, Exit Code {ExitCode}",
            _process?.Id, _process?.ExitCode);

        OnConnectionChanged(false, null);
    }

    private void OnConnectionChanged(bool isConnected, Exception? error)
    {
        ConnectionChanged?.Invoke(this, new TransportConnectionEventArgs
        {
            IsConnected = isConnected,
            Error = error
        });
    }

    private JsonRpcMessage? ParseMessage(string line)
    {
        try
        {
            _logger.LogTrace("Received JSON-RPC message: {Json}", line);

            // Try to deserialize as different message types
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;

            if (root.TryGetProperty("id", out var idProperty))
            {
                if (root.TryGetProperty("method", out _))
                {
                    // Request
                    return JsonSerializer.Deserialize<JsonRpcRequest>(line, _jsonOptions);
                }
                else
                {
                    // Response
                    return JsonSerializer.Deserialize<JsonRpcResponse>(line, _jsonOptions);
                }
            }
            else if (root.TryGetProperty("method", out _))
            {
                // Notification
                return JsonSerializer.Deserialize<JsonRpcNotification>(line, _jsonOptions);
            }

            _logger.LogWarning("Unable to deserialize JSON-RPC message: {Json}", line);
            return null;
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse JSON-RPC message: {Json}", line);
            return null;
        }
    }

    private static bool TryGetRequestId(string json, out object requestId)
    {
        requestId = null!;

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.TryGetProperty("id", out var idElement))
            {
                requestId = idElement.ValueKind switch
                {
                    JsonValueKind.String => idElement.GetString()!,
                    JsonValueKind.Number => idElement.GetInt32(),
                    _ => null!
                };
                return requestId != null;
            }
        }
        catch
        {
            // Ignore parsing errors
        }

        return false;
    }
}