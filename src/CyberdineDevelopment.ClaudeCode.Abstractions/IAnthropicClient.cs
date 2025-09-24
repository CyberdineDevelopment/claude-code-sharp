namespace CyberdineDevelopment.ClaudeCode.Abstractions;

/// <summary>
/// Defines the contract for Anthropic API operations.
/// </summary>
public interface IAnthropicClient : IDisposable
{
    /// <summary>
    /// Sends a message to Claude and gets a response.
    /// </summary>
    /// <param name="request">The chat request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The response from Claude.</returns>
    Task<ChatResponse> SendMessageAsync(ChatRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a message to Claude and gets a streaming response.
    /// </summary>
    /// <param name="request">The chat request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An async enumerable of response chunks.</returns>
    IAsyncEnumerable<ChatStreamChunk> SendMessageStreamAsync(ChatRequest request, CancellationToken cancellationToken = default);
}

/// <summary>
/// Represents a chat request to Claude.
/// </summary>
public sealed record ChatRequest
{
    /// <summary>
    /// Gets the model to use for the request.
    /// </summary>
    public required string Model { get; init; }

    /// <summary>
    /// Gets the messages in the conversation.
    /// </summary>
    public required IReadOnlyList<ChatMessage> Messages { get; init; }

    /// <summary>
    /// Gets the maximum number of tokens to generate.
    /// </summary>
    public int? MaxTokens { get; init; }

    /// <summary>
    /// Gets the temperature for response generation.
    /// </summary>
    public double? Temperature { get; init; }

    /// <summary>
    /// Gets the system prompt.
    /// </summary>
    public string? System { get; init; }

    /// <summary>
    /// Gets the tools available to the model.
    /// </summary>
    public IReadOnlyList<Tool> Tools { get; init; } = Array.Empty<Tool>();

    /// <summary>
    /// Gets additional metadata for the request.
    /// </summary>
    public Dictionary<string, object> Metadata { get; init; } = new(StringComparer.Ordinal);
}

/// <summary>
/// Represents a message in a chat conversation.
/// </summary>
public sealed record ChatMessage
{
    /// <summary>
    /// Gets the role of the message sender.
    /// </summary>
    public required string Role { get; init; }

    /// <summary>
    /// Gets the content of the message.
    /// </summary>
    public required IReadOnlyList<ContentBlock> Content { get; init; }
}

/// <summary>
/// Represents a content block in a message.
/// </summary>
public abstract record ContentBlock
{
    /// <summary>
    /// Gets the type of content.
    /// </summary>
    public abstract string Type { get; }
}

/// <summary>
/// Represents text content in a message.
/// </summary>
public sealed record TextContent : ContentBlock
{
    /// <summary>
    /// Gets the text content.
    /// </summary>
    public required string Text { get; init; }

    /// <summary>
    /// Gets the type of content.
    /// </summary>
    public override string Type => "text";
}

/// <summary>
/// Represents image content in a message.
/// </summary>
public sealed record ImageContent : ContentBlock
{
    /// <summary>
    /// Gets the source of the image.
    /// </summary>
    public required ImageSource Source { get; init; }

    /// <summary>
    /// Gets the type of content.
    /// </summary>
    public override string Type => "image";
}

/// <summary>
/// Represents the source of an image.
/// </summary>
public abstract record ImageSource
{
    /// <summary>
    /// Gets the type of image source.
    /// </summary>
    public abstract string Type { get; }
}

/// <summary>
/// Represents a base64-encoded image source.
/// </summary>
public sealed record Base64ImageSource : ImageSource
{
    /// <summary>
    /// Gets the media type of the image.
    /// </summary>
    public required string MediaType { get; init; }

    /// <summary>
    /// Gets the base64-encoded image data.
    /// </summary>
    public required string Data { get; init; }

    /// <summary>
    /// Gets the type of image source.
    /// </summary>
    public override string Type => "base64";
}

/// <summary>
/// Represents a tool available to the model.
/// </summary>
public sealed record Tool
{
    /// <summary>
    /// Gets the name of the tool.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Gets the description of the tool.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Gets the input schema for the tool.
    /// </summary>
    public object? InputSchema { get; init; }
}

/// <summary>
/// Represents a response from Claude.
/// </summary>
public sealed record ChatResponse
{
    /// <summary>
    /// Gets the unique identifier for the response.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// Gets the model used for the response.
    /// </summary>
    public required string Model { get; init; }

    /// <summary>
    /// Gets the role of the response.
    /// </summary>
    public required string Role { get; init; }

    /// <summary>
    /// Gets the content of the response.
    /// </summary>
    public required IReadOnlyList<ContentBlock> Content { get; init; }

    /// <summary>
    /// Gets the reason the response stopped.
    /// </summary>
    public string? StopReason { get; init; }

    /// <summary>
    /// Gets usage statistics for the request.
    /// </summary>
    public Usage? Usage { get; init; }
}

/// <summary>
/// Represents a chunk in a streaming response.
/// </summary>
public sealed record ChatStreamChunk
{
    /// <summary>
    /// Gets the type of the chunk.
    /// </summary>
    public required string Type { get; init; }

    /// <summary>
    /// Gets the content delta for this chunk.
    /// </summary>
    public ContentBlock? Delta { get; init; }

    /// <summary>
    /// Gets usage information if this is the final chunk.
    /// </summary>
    public Usage? Usage { get; init; }
}

/// <summary>
/// Represents usage statistics for a request.
/// </summary>
public sealed record Usage
{
    /// <summary>
    /// Gets the number of input tokens.
    /// </summary>
    public required int InputTokens { get; init; }

    /// <summary>
    /// Gets the number of output tokens.
    /// </summary>
    public required int OutputTokens { get; init; }
}