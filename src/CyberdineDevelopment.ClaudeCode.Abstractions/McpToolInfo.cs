using System.Text.Json;

namespace CyberdineDevelopment.ClaudeCode.Abstractions;

/// <summary>
/// Contains information about an MCP tool.
/// </summary>
public sealed record McpToolInfo
{
    /// <summary>
    /// Gets the name of the tool.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Gets the description of what the tool does.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Gets the JSON schema for the tool's input parameters.
    /// </summary>
    public JsonElement? InputSchema { get; init; }

    /// <summary>
    /// Gets additional metadata about the tool.
    /// </summary>
    public Dictionary<string, object> Metadata { get; init; } = new(StringComparer.Ordinal);
}

/// <summary>
/// Represents the result of calling an MCP tool.
/// </summary>
public sealed record McpToolResult
{
    /// <summary>
    /// Gets a value indicating whether the tool call was successful.
    /// </summary>
    public required bool IsSuccess { get; init; }

    /// <summary>
    /// Gets the content returned by the tool.
    /// </summary>
    public IReadOnlyList<McpContent> Content { get; init; } = Array.Empty<McpContent>();

    /// <summary>
    /// Gets error information if the call failed.
    /// </summary>
    public McpError? Error { get; init; }

    /// <summary>
    /// Gets additional metadata about the result.
    /// </summary>
    public Dictionary<string, object> Metadata { get; init; } = new(StringComparer.Ordinal);
}

/// <summary>
/// Represents content returned by an MCP operation.
/// </summary>
public abstract record McpContent
{
    /// <summary>
    /// Gets the type of content.
    /// </summary>
    public abstract string Type { get; }
}

/// <summary>
/// Represents text content.
/// </summary>
public sealed record McpTextContent : McpContent
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
/// Represents image content.
/// </summary>
public sealed record McpImageContent : McpContent
{
    /// <summary>
    /// Gets the image data (base64 encoded).
    /// </summary>
    public required string Data { get; init; }

    /// <summary>
    /// Gets the MIME type of the image.
    /// </summary>
    public required string MimeType { get; init; }

    /// <summary>
    /// Gets the type of content.
    /// </summary>
    public override string Type => "image";
}

/// <summary>
/// Represents error information from an MCP operation.
/// </summary>
public sealed record McpError
{
    /// <summary>
    /// Gets the error code.
    /// </summary>
    public required int Code { get; init; }

    /// <summary>
    /// Gets the error message.
    /// </summary>
    public required string Message { get; init; }

    /// <summary>
    /// Gets additional error data.
    /// </summary>
    public object? Data { get; init; }
}