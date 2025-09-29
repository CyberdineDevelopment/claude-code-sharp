using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CyberdineDevelopment.ClaudeCode.Abstractions;
using Microsoft.Extensions.Logging;

namespace CyberdineDevelopment.ClaudeCode.Anthropic;

/// <summary>
/// Anthropic client that uses subscription authentication instead of API keys.
/// </summary>
public sealed class SubscriptionAnthropicClient : IAnthropicClient
{
    private readonly HttpClient _httpClient;
    private readonly IAuthenticationService _authService;
    private readonly ILogger<SubscriptionAnthropicClient> _logger;
    private readonly JsonSerializerOptions _jsonOptions;
    private bool _disposed;

    // Claude.ai uses different endpoints for subscription access
    private static readonly Uri SubscriptionBaseUrl = new("https://claude.ai/api");

    /// <summary>
    /// Initializes a new instance of the <see cref="SubscriptionAnthropicClient"/> class.
    /// </summary>
    /// <param name="httpClient">The HTTP client.</param>
    /// <param name="authService">The authentication service.</param>
    /// <param name="logger">The logger.</param>
    public SubscriptionAnthropicClient(
        HttpClient httpClient,
        IAuthenticationService authService,
        ILogger<SubscriptionAnthropicClient> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _authService = authService ?? throw new ArgumentNullException(nameof(authService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        ConfigureHttpClient();
    }

    /// <inheritdoc />
    public async Task<ChatResponse> SendMessageAsync(ChatRequest request, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(request);

        _logger.LogDebug("Sending subscription chat request to model {Model}", request.Model);

        // Ensure we have valid authentication
        var accessToken = await _authService.GetAccessTokenAsync(cancellationToken).ConfigureAwait(false);

        // Update authorization header
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        // Convert to subscription API format (different from public API)
        var subscriptionRequest = MapToSubscriptionRequest(request);
        var json = JsonSerializer.Serialize(subscriptionRequest, _jsonOptions);

        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var response = await _httpClient.PostAsync("/organizations/unknown/chat_conversations", content, cancellationToken).ConfigureAwait(false);

        await EnsureSuccessStatusCodeAsync(response).ConfigureAwait(false);

        var responseJson = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var subscriptionResponse = JsonSerializer.Deserialize<SubscriptionChatResponse>(responseJson, _jsonOptions);

        if (subscriptionResponse == null)
        {
            throw new InvalidOperationException("Failed to deserialize subscription API response");
        }

        return MapFromSubscriptionResponse(subscriptionResponse);
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<ChatStreamChunk> SendMessageStreamAsync(
        ChatRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(request);

        _logger.LogDebug("Sending subscription streaming chat request to model {Model}", request.Model);

        // Ensure we have valid authentication
        var accessToken = await _authService.GetAccessTokenAsync(cancellationToken).ConfigureAwait(false);

        // Update authorization header
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var subscriptionRequest = MapToSubscriptionRequest(request);
        subscriptionRequest.Stream = true;

        var json = JsonSerializer.Serialize(subscriptionRequest, _jsonOptions);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");

        using var response = await _httpClient.PostAsync("/organizations/unknown/chat_conversations", content, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessStatusCodeAsync(response).ConfigureAwait(false);

        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var reader = new StreamReader(stream, Encoding.UTF8);

        string? line;
        while ((line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false)) != null)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                yield break;
            }

            if (string.IsNullOrWhiteSpace(line) || !line.StartsWith("data: ", StringComparison.Ordinal))
            {
                continue;
            }

            var data = line["data: ".Length..];

            if (data == "[DONE]")
            {
                yield break;
            }

            SubscriptionStreamChunk? chunk;
            try
            {
                chunk = JsonSerializer.Deserialize<SubscriptionStreamChunk>(data, _jsonOptions);
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Failed to parse subscription stream chunk: {Data}", data);
                continue;
            }

            if (chunk == null)
            {
                continue;
            }

            yield return MapFromSubscriptionStreamChunk(chunk);
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
        _httpClient.Dispose();
    }

    private void ConfigureHttpClient()
    {
        _httpClient.BaseAddress = SubscriptionBaseUrl;
        _httpClient.DefaultRequestHeaders.Clear();
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "CyberdineDevelopment.ClaudeCode/1.0.0");
        _httpClient.DefaultRequestHeaders.Add("Accept", "application/json");
    }

    private static async Task EnsureSuccessStatusCodeAsync(HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            throw new HttpRequestException(
                $"Subscription API request failed with status {response.StatusCode}: {errorContent}",
                null,
                response.StatusCode);
        }
    }

    private static SubscriptionChatRequest MapToSubscriptionRequest(ChatRequest request)
    {
        return new SubscriptionChatRequest
        {
            Model = request.Model,
            MaxTokens = request.MaxTokens ?? 4096,
            Temperature = request.Temperature,
            Messages = request.Messages.Select(MapToSubscriptionMessage).ToArray()
        };
    }

    private static SubscriptionMessage MapToSubscriptionMessage(ChatMessage message)
    {
        return new SubscriptionMessage
        {
            Role = message.Role,
            Content = message.Content.OfType<TextContent>().FirstOrDefault()?.Text ?? string.Empty
        };
    }

    private static ChatResponse MapFromSubscriptionResponse(SubscriptionChatResponse response)
    {
        return new ChatResponse
        {
            Id = response.Id ?? Guid.NewGuid().ToString(),
            Model = response.Model ?? "claude-3-5-sonnet-20241022",
            Role = "assistant",
            Content = new ContentBlock[] { new TextContent { Text = response.Text ?? string.Empty } },
            StopReason = response.StopReason,
            Usage = response.Usage != null ? new Usage
            {
                InputTokens = response.Usage.InputTokens,
                OutputTokens = response.Usage.OutputTokens
            } : null
        };
    }

    private static ChatStreamChunk MapFromSubscriptionStreamChunk(SubscriptionStreamChunk chunk)
    {
        ContentBlock? delta = null;

        if (!string.IsNullOrEmpty(chunk.Text))
        {
            delta = new TextContent { Text = chunk.Text };
        }

        return new ChatStreamChunk
        {
            Type = chunk.Type ?? "content_block_delta",
            Delta = delta,
            Usage = chunk.Usage != null ? new Usage
            {
                InputTokens = chunk.Usage.InputTokens,
                OutputTokens = chunk.Usage.OutputTokens
            } : null
        };
    }

    // Subscription API DTOs (different format from public API)
    private sealed record SubscriptionChatRequest
    {
        [JsonPropertyName("model")]
        public required string Model { get; init; }

        [JsonPropertyName("max_tokens")]
        public required int MaxTokens { get; init; }

        [JsonPropertyName("temperature")]
        public double? Temperature { get; init; }

        [JsonPropertyName("messages")]
        public required SubscriptionMessage[] Messages { get; init; }

        [JsonPropertyName("stream")]
        public bool Stream { get; set; }
    }

    private sealed record SubscriptionMessage
    {
        [JsonPropertyName("role")]
        public required string Role { get; init; }

        [JsonPropertyName("content")]
        public required string Content { get; init; }
    }

    private sealed record SubscriptionChatResponse
    {
        [JsonPropertyName("id")]
        public string? Id { get; init; }

        [JsonPropertyName("model")]
        public string? Model { get; init; }

        [JsonPropertyName("text")]
        public string? Text { get; init; }

        [JsonPropertyName("stop_reason")]
        public string? StopReason { get; init; }

        [JsonPropertyName("usage")]
        public SubscriptionUsage? Usage { get; init; }
    }

    private sealed record SubscriptionStreamChunk
    {
        [JsonPropertyName("type")]
        public string? Type { get; init; }

        [JsonPropertyName("text")]
        public string? Text { get; init; }

        [JsonPropertyName("usage")]
        public SubscriptionUsage? Usage { get; init; }
    }

    private sealed record SubscriptionUsage
    {
        [JsonPropertyName("input_tokens")]
        public required int InputTokens { get; init; }

        [JsonPropertyName("output_tokens")]
        public required int OutputTokens { get; init; }
    }
}