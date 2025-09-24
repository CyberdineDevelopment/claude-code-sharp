using System.Collections.Concurrent;
using System.Text.Json;
using CyberdineDevelopment.ClaudeCode.Abstractions;
using CyberdineDevelopment.ClaudeCode.MCP.JsonRpc;
using Microsoft.Extensions.Logging;

namespace CyberdineDevelopment.ClaudeCode.MCP;

/// <summary>
/// Implementation of MCP (Model Context Protocol) client.
/// </summary>
public sealed class McpClient : IMcpClient
{
    private readonly ILogger<McpClient> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly ConcurrentDictionary<object, TaskCompletionSource<JsonRpcResponse>> _pendingRequests;
    private IJsonRpcTransport? _transport;
    private volatile bool _disposed;
    private int _nextRequestId;
    private CancellationTokenSource? _receiveCts;

    /// <summary>
    /// Initializes a new instance of the <see cref="McpClient"/> class.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="loggerFactory">The logger factory.</param>
    public McpClient(ILogger<McpClient> logger, ILoggerFactory loggerFactory)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
        _pendingRequests = new ConcurrentDictionary<object, TaskCompletionSource<JsonRpcResponse>>();

        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
    }

    /// <inheritdoc />
    public bool IsConnected => _transport?.IsConnected == true;

    /// <inheritdoc />
    public event EventHandler<McpConnectionEventArgs>? ConnectionChanged;

    /// <inheritdoc />
    public event EventHandler<McpMessageEventArgs>? MessageReceived;

    /// <inheritdoc />
    public async Task ConnectAsync(McpServerInfo serverInfo, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(serverInfo);

        if (IsConnected)
        {
            return;
        }

        try
        {
            _logger.LogInformation("Connecting to MCP server: {ServerId}", serverInfo.Id);

            // Create transport based on server configuration
            _transport = CreateTransport(serverInfo.Transport);
            _transport.ConnectionChanged += OnTransportConnectionChanged;

            // Connect the transport
            await _transport.ConnectAsync(cancellationToken).ConfigureAwait(false);

            // Start receiving messages
            _receiveCts = new CancellationTokenSource();
            _ = Task.Run(() => ReceiveMessagesAsync(_receiveCts.Token), _receiveCts.Token);

            // Perform MCP initialization handshake
            await InitializeAsync(serverInfo, cancellationToken).ConfigureAwait(false);

            _logger.LogInformation("Successfully connected to MCP server: {ServerId}", serverInfo.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to connect to MCP server: {ServerId}", serverInfo.Id);
            await DisconnectAsync(cancellationToken).ConfigureAwait(false);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        if (!IsConnected)
        {
            return;
        }

        try
        {
            _logger.LogInformation("Disconnecting from MCP server");

            _receiveCts?.Cancel();

            if (_transport != null)
            {
                _transport.ConnectionChanged -= OnTransportConnectionChanged;
                await _transport.DisconnectAsync(cancellationToken).ConfigureAwait(false);
                _transport.Dispose();
                _transport = null;
            }

            // Complete any pending requests with cancellation
            var pendingRequests = _pendingRequests.Values.ToArray();
            _pendingRequests.Clear();

            foreach (var tcs in pendingRequests)
            {
                tcs.TrySetCanceled(cancellationToken);
            }

            _logger.LogInformation("Disconnected from MCP server");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error during disconnect");
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyCollection<McpToolInfo>> ListToolsAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!IsConnected)
        {
            throw new InvalidOperationException("Client is not connected");
        }

        var response = await SendRequestAsync("tools/list", null, cancellationToken).ConfigureAwait(false);

        if (response.Result is JsonElement resultElement)
        {
            var tools = new List<McpToolInfo>();

            if (resultElement.TryGetProperty("tools", out var toolsElement) && toolsElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var toolElement in toolsElement.EnumerateArray())
                {
                    if (toolElement.TryGetProperty("name", out var nameElement) &&
                        nameElement.ValueKind == JsonValueKind.String)
                    {
                        var tool = new McpToolInfo
                        {
                            Name = nameElement.GetString()!
                        };

                        if (toolElement.TryGetProperty("description", out var descElement) &&
                            descElement.ValueKind == JsonValueKind.String)
                        {
                            tool = tool with { Description = descElement.GetString() };
                        }

                        if (toolElement.TryGetProperty("inputSchema", out var schemaElement))
                        {
                            tool = tool with { InputSchema = schemaElement };
                        }

                        tools.Add(tool);
                    }
                }
            }

            return tools;
        }

        return Array.Empty<McpToolInfo>();
    }

    /// <inheritdoc />
    public async Task<McpToolResult> CallToolAsync(string toolName, object? arguments = null, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(toolName);

        if (!IsConnected)
        {
            throw new InvalidOperationException("Client is not connected");
        }

        var request = new
        {
            name = toolName,
            arguments = arguments ?? new { }
        };

        var response = await SendRequestAsync("tools/call", request, cancellationToken).ConfigureAwait(false);

        if (response.Error != null)
        {
            return new McpToolResult
            {
                IsSuccess = false,
                Error = new McpError
                {
                    Code = response.Error.Code,
                    Message = response.Error.Message,
                    Data = response.Error.Data
                }
            };
        }

        var content = new List<McpContent>();

        if (response.Result is JsonElement resultElement)
        {
            if (resultElement.TryGetProperty("content", out var contentElement) && contentElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var contentItem in contentElement.EnumerateArray())
                {
                    if (contentItem.TryGetProperty("type", out var typeElement) &&
                        typeElement.ValueKind == JsonValueKind.String)
                    {
                        var type = typeElement.GetString();

                        if (type == "text" &&
                            contentItem.TryGetProperty("text", out var textElement) &&
                            textElement.ValueKind == JsonValueKind.String)
                        {
                            content.Add(new McpTextContent
                            {
                                Text = textElement.GetString()!
                            });
                        }
                        else if (type == "image" &&
                                contentItem.TryGetProperty("data", out var dataElement) &&
                                contentItem.TryGetProperty("mimeType", out var mimeElement) &&
                                dataElement.ValueKind == JsonValueKind.String &&
                                mimeElement.ValueKind == JsonValueKind.String)
                        {
                            content.Add(new McpImageContent
                            {
                                Data = dataElement.GetString()!,
                                MimeType = mimeElement.GetString()!
                            });
                        }
                    }
                }
            }
        }

        return new McpToolResult
        {
            IsSuccess = true,
            Content = content
        };
    }

    /// <inheritdoc />
    public async Task<IReadOnlyCollection<McpResourceInfo>> ListResourcesAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!IsConnected)
        {
            throw new InvalidOperationException("Client is not connected");
        }

        var response = await SendRequestAsync("resources/list", null, cancellationToken).ConfigureAwait(false);

        if (response.Result is JsonElement resultElement)
        {
            var resources = new List<McpResourceInfo>();

            if (resultElement.TryGetProperty("resources", out var resourcesElement) && resourcesElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var resourceElement in resourcesElement.EnumerateArray())
                {
                    if (resourceElement.TryGetProperty("uri", out var uriElement) &&
                        uriElement.ValueKind == JsonValueKind.String &&
                        Uri.TryCreate(uriElement.GetString(), UriKind.Absolute, out var uri))
                    {
                        var resource = new McpResourceInfo
                        {
                            Uri = uri
                        };

                        if (resourceElement.TryGetProperty("name", out var nameElement) &&
                            nameElement.ValueKind == JsonValueKind.String)
                        {
                            resource = resource with { Name = nameElement.GetString() };
                        }

                        if (resourceElement.TryGetProperty("description", out var descElement) &&
                            descElement.ValueKind == JsonValueKind.String)
                        {
                            resource = resource with { Description = descElement.GetString() };
                        }

                        if (resourceElement.TryGetProperty("mimeType", out var mimeElement) &&
                            mimeElement.ValueKind == JsonValueKind.String)
                        {
                            resource = resource with { MimeType = mimeElement.GetString() };
                        }

                        resources.Add(resource);
                    }
                }
            }

            return resources;
        }

        return Array.Empty<McpResourceInfo>();
    }

    /// <inheritdoc />
    public async Task<McpResourceContent> ReadResourceAsync(Uri uri, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(uri);

        if (!IsConnected)
        {
            throw new InvalidOperationException("Client is not connected");
        }

        var request = new { uri = uri.ToString() };
        var response = await SendRequestAsync("resources/read", request, cancellationToken).ConfigureAwait(false);

        if (response.Error != null)
        {
            throw new InvalidOperationException($"Failed to read resource: {response.Error.Message}");
        }

        var content = new List<McpContent>();

        if (response.Result is JsonElement resultElement)
        {
            if (resultElement.TryGetProperty("contents", out var contentsElement) && contentsElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var contentItem in contentsElement.EnumerateArray())
                {
                    if (contentItem.TryGetProperty("type", out var typeElement) &&
                        typeElement.ValueKind == JsonValueKind.String)
                    {
                        var type = typeElement.GetString();

                        if (type == "text" &&
                            contentItem.TryGetProperty("text", out var textElement) &&
                            textElement.ValueKind == JsonValueKind.String)
                        {
                            content.Add(new McpTextContent
                            {
                                Text = textElement.GetString()!
                            });
                        }
                    }
                }
            }
        }

        return new McpResourceContent
        {
            Uri = uri,
            Content = content
        };
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
    }

    private IJsonRpcTransport CreateTransport(McpTransport transport)
    {
        return transport switch
        {
            McpStdioTransport stdioTransport => new StdioJsonRpcTransport(stdioTransport,
                _loggerFactory.CreateLogger<StdioJsonRpcTransport>()),
            _ => throw new NotSupportedException($"Transport type {transport.Type} is not supported")
        };
    }

    private async Task InitializeAsync(McpServerInfo serverInfo, CancellationToken cancellationToken)
    {
        var initRequest = new
        {
            protocolVersion = "2024-11-05",
            capabilities = new
            {
                tools = new { },
                resources = new { }
            },
            clientInfo = new
            {
                name = "CyberdineDevelopment.ClaudeCode",
                version = "1.0.0"
            }
        };

        var response = await SendRequestAsync("initialize", initRequest, cancellationToken).ConfigureAwait(false);

        if (response.Error != null)
        {
            throw new InvalidOperationException($"MCP initialization failed: {response.Error.Message}");
        }

        // Send initialized notification
        var notification = new JsonRpcNotification
        {
            Method = "notifications/initialized"
        };

        await _transport!.SendAsync(notification, cancellationToken).ConfigureAwait(false);
    }

    private async Task<JsonRpcResponse> SendRequestAsync(string method, object? parameters, CancellationToken cancellationToken)
    {
        var requestId = Interlocked.Increment(ref _nextRequestId);
        var tcs = new TaskCompletionSource<JsonRpcResponse>(TaskCreationOptions.RunContinuationsAsynchronously);

        _pendingRequests.TryAdd(requestId, tcs);

        try
        {
            var request = new JsonRpcRequest
            {
                Id = requestId,
                Method = method,
                Params = parameters
            };

            await _transport!.SendAsync(request, cancellationToken).ConfigureAwait(false);

            OnMessageSent(JsonSerializer.Serialize(request, _jsonOptions));

            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            using var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            var registration = combinedCts.Token.Register(() => tcs.TrySetCanceled(combinedCts.Token));

            try
            {
                return await tcs.Task.ConfigureAwait(false);
            }
            finally
            {
                registration.Dispose();
            }
        }
        finally
        {
            _pendingRequests.TryRemove(requestId, out _);
        }
    }

    private async Task ReceiveMessagesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var message in _transport!.ReceiveAsync(cancellationToken).ConfigureAwait(false))
            {
                OnMessageReceived(JsonSerializer.Serialize(message, _jsonOptions));

                switch (message)
                {
                    case JsonRpcResponse response:
                        if (_pendingRequests.TryRemove(response.Id, out var tcs))
                        {
                            tcs.SetResult(response);
                        }
                        break;

                    case JsonRpcRequest request:
                        // Handle incoming requests (not currently supported)
                        _logger.LogWarning("Received unexpected request: {Method}", request.Method);
                        break;

                    case JsonRpcNotification notification:
                        // Handle notifications
                        _logger.LogDebug("Received notification: {Method}", notification.Method);
                        break;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Expected cancellation
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in message receive loop");
        }
    }

    private void OnTransportConnectionChanged(object? sender, TransportConnectionEventArgs e)
    {
        ConnectionChanged?.Invoke(this, new McpConnectionEventArgs
        {
            IsConnected = e.IsConnected,
            Error = e.Error != null ? new McpError
            {
                Code = -1,
                Message = e.Error.Message
            } : null
        });
    }

    private void OnMessageSent(string message)
    {
        MessageReceived?.Invoke(this, new McpMessageEventArgs
        {
            Direction = McpMessageDirection.Outbound,
            Message = message
        });
    }

    private void OnMessageReceived(string message)
    {
        MessageReceived?.Invoke(this, new McpMessageEventArgs
        {
            Direction = McpMessageDirection.Inbound,
            Message = message
        });
    }
}