using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CyberdineDevelopment.ClaudeCode.Abstractions;
using Microsoft.Extensions.Logging;

namespace CyberdineDevelopment.ClaudeCode.Anthropic;

/// <summary>
/// Implementation of Anthropic API client for Claude interactions.
/// </summary>
public sealed class AnthropicClient : IAnthropicClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<AnthropicClient> _logger;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly string _apiKey;
    private bool _disposed;

    private static readonly Uri ApiBaseUrl = new("https://api.anthropic.com");

    /// <summary>
    /// Initializes a new instance of the <see cref="AnthropicClient"/> class.
    /// </summary>
    /// <param name="httpClient">The HTTP client to use for API calls.</param>
    /// <param name="apiKey">The Anthropic API key.</param>
    /// <param name="logger">The logger.</param>
    public AnthropicClient(HttpClient httpClient, string apiKey, ILogger<AnthropicClient> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _apiKey = !string.IsNullOrWhiteSpace(apiKey) ? apiKey : throw new ArgumentException("API key cannot be null or empty", nameof(apiKey));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Converters = { new ContentBlockConverter() }
        };

        ConfigureHttpClient();
    }

    /// <inheritdoc />
    public async Task<ChatResponse> SendMessageAsync(ChatRequest request, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(request);

        _logger.LogDebug("Sending chat request to model {Model}", request.Model);

        var apiRequest = MapToApiRequest(request);
        var json = JsonSerializer.Serialize(apiRequest, _jsonOptions);

        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var response = await _httpClient.PostAsync("/v1/messages", content, cancellationToken).ConfigureAwait(false);

        await EnsureSuccessStatusCodeAsync(response).ConfigureAwait(false);

        var responseJson = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var apiResponse = JsonSerializer.Deserialize<ApiChatResponse>(responseJson, _jsonOptions);

        if (apiResponse == null)
        {
            throw new InvalidOperationException("Failed to deserialize API response");
        }

        return MapFromApiResponse(apiResponse);
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<ChatStreamChunk> SendMessageStreamAsync(
        ChatRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(request);

        _logger.LogDebug("Sending streaming chat request to model {Model}", request.Model);

        var apiRequest = MapToApiRequest(request);
        apiRequest.Stream = true;

        var json = JsonSerializer.Serialize(apiRequest, _jsonOptions);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");

        using var response = await _httpClient.PostAsync("/v1/messages", content, cancellationToken).ConfigureAwait(false);
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

            ApiStreamChunk? chunk;
            try
            {
                chunk = JsonSerializer.Deserialize<ApiStreamChunk>(data, _jsonOptions);
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Failed to parse stream chunk: {Data}", data);
                continue;
            }

            if (chunk == null)
            {
                continue;
            }

            yield return MapFromApiStreamChunk(chunk);
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
        _httpClient.BaseAddress = ApiBaseUrl;
        _httpClient.DefaultRequestHeaders.Clear();
        _httpClient.DefaultRequestHeaders.Add("x-api-key", _apiKey);
        _httpClient.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");
        _httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("CyberdineDevelopment.ClaudeCode", "1.0.0"));
    }

    private static async Task EnsureSuccessStatusCodeAsync(HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            throw new HttpRequestException(
                $"API request failed with status {response.StatusCode}: {errorContent}",
                null,
                response.StatusCode);
        }
    }

    private ApiChatRequest MapToApiRequest(ChatRequest request)
    {
        return new ApiChatRequest
        {
            Model = request.Model,
            MaxTokens = request.MaxTokens ?? 4096,
            Temperature = request.Temperature,
            System = request.System,
            Messages = request.Messages.Select(MapToApiMessage).ToArray(),
            Tools = request.Tools.Count > 0 ? request.Tools.Select(MapToApiTool).ToArray() : null
        };
    }

    private static ApiMessage MapToApiMessage(ChatMessage message)
    {
        return new ApiMessage
        {
            Role = message.Role,
            Content = message.Content.Select(MapToApiContent).ToArray()
        };
    }

    private static object MapToApiContent(ContentBlock content)
    {
        return content switch
        {
            TextContent text => new ApiTextContent { Type = "text", Text = text.Text },
            ImageContent image => new ApiImageContent
            {
                Type = "image",
                Source = image.Source switch
                {
                    Base64ImageSource base64 => new ApiImageSource
                    {
                        Type = "base64",
                        MediaType = base64.MediaType,
                        Data = base64.Data
                    },
                    _ => throw new NotSupportedException($"Image source type {image.Source.Type} is not supported")
                }
            },
            _ => throw new NotSupportedException($"Content type {content.Type} is not supported")
        };
    }

    private static ApiTool MapToApiTool(Tool tool)
    {
        return new ApiTool
        {
            Name = tool.Name,
            Description = tool.Description,
            InputSchema = tool.InputSchema
        };
    }

    private static ChatResponse MapFromApiResponse(ApiChatResponse response)
    {
        return new ChatResponse
        {
            Id = response.Id,
            Model = response.Model,
            Role = response.Role,
            Content = response.Content.Select(MapFromApiContent).ToArray(),
            StopReason = response.StopReason,
            Usage = response.Usage != null ? new Usage
            {
                InputTokens = response.Usage.InputTokens,
                OutputTokens = response.Usage.OutputTokens
            } : null
        };
    }

    private static ContentBlock MapFromApiContent(object content)
    {
        return content switch
        {
            ApiTextContent text => new TextContent { Text = text.Text },
            _ => throw new NotSupportedException("Unsupported content type in API response")
        };
    }

    private static ChatStreamChunk MapFromApiStreamChunk(ApiStreamChunk chunk)
    {
        ContentBlock? delta = null;

        if (chunk.Delta != null)
        {
            delta = chunk.Delta switch
            {
                ApiTextContent text => new TextContent { Text = text.Text },
                _ => null
            };
        }

        return new ChatStreamChunk
        {
            Type = chunk.Type,
            Delta = delta,
            Usage = chunk.Usage != null ? new Usage
            {
                InputTokens = chunk.Usage.InputTokens,
                OutputTokens = chunk.Usage.OutputTokens
            } : null
        };
    }

    // API DTOs
    internal sealed record ApiChatRequest
    {
        [JsonPropertyName("model")]
        public required string Model { get; init; }

        [JsonPropertyName("max_tokens")]
        public required int MaxTokens { get; init; }

        [JsonPropertyName("temperature")]
        public double? Temperature { get; init; }

        [JsonPropertyName("system")]
        public string? System { get; init; }

        [JsonPropertyName("messages")]
        public required ApiMessage[] Messages { get; init; }

        [JsonPropertyName("tools")]
        public ApiTool[]? Tools { get; init; }

        [JsonPropertyName("stream")]
        public bool Stream { get; set; }
    }

    internal sealed record ApiMessage
    {
        [JsonPropertyName("role")]
        public required string Role { get; init; }

        [JsonPropertyName("content")]
        public required object[] Content { get; init; }
    }

    internal sealed record ApiTextContent
    {
        [JsonPropertyName("type")]
        public required string Type { get; init; }

        [JsonPropertyName("text")]
        public required string Text { get; init; }
    }

    internal sealed record ApiImageContent
    {
        [JsonPropertyName("type")]
        public required string Type { get; init; }

        [JsonPropertyName("source")]
        public required ApiImageSource Source { get; init; }
    }

    internal sealed record ApiImageSource
    {
        [JsonPropertyName("type")]
        public required string Type { get; init; }

        [JsonPropertyName("media_type")]
        public required string MediaType { get; init; }

        [JsonPropertyName("data")]
        public required string Data { get; init; }
    }

    internal sealed record ApiTool
    {
        [JsonPropertyName("name")]
        public required string Name { get; init; }

        [JsonPropertyName("description")]
        public string? Description { get; init; }

        [JsonPropertyName("input_schema")]
        public object? InputSchema { get; init; }
    }

    internal sealed record ApiChatResponse
    {
        [JsonPropertyName("id")]
        public required string Id { get; init; }

        [JsonPropertyName("model")]
        public required string Model { get; init; }

        [JsonPropertyName("role")]
        public required string Role { get; init; }

        [JsonPropertyName("content")]
        public required object[] Content { get; init; }

        [JsonPropertyName("stop_reason")]
        public string? StopReason { get; init; }

        [JsonPropertyName("usage")]
        public ApiUsage? Usage { get; init; }
    }

    internal sealed record ApiStreamChunk
    {
        [JsonPropertyName("type")]
        public required string Type { get; init; }

        [JsonPropertyName("delta")]
        public object? Delta { get; init; }

        [JsonPropertyName("usage")]
        public ApiUsage? Usage { get; init; }
    }

    internal sealed record ApiUsage
    {
        [JsonPropertyName("input_tokens")]
        public required int InputTokens { get; init; }

        [JsonPropertyName("output_tokens")]
        public required int OutputTokens { get; init; }
    }
}

/// <summary>
/// JSON converter for ContentBlock types.
/// </summary>
internal sealed class ContentBlockConverter : JsonConverter<object>
{
    public override object Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;

        if (root.TryGetProperty("type", out var typeElement))
        {
            return typeElement.GetString() switch
            {
                "text" => JsonSerializer.Deserialize<AnthropicClient.ApiTextContent>(root, options)!,
                "image" => JsonSerializer.Deserialize<AnthropicClient.ApiImageContent>(root, options)!,
                _ => root.Deserialize<object>(options)!
            };
        }

        return root.Deserialize<object>(options)!;
    }

    public override void Write(Utf8JsonWriter writer, object value, JsonSerializerOptions options)
    {
        JsonSerializer.Serialize(writer, value, value.GetType(), options);
    }
}