namespace CyberdineDevelopment.ClaudeCode.Abstractions;

/// <summary>
/// Contains information about an MCP resource.
/// </summary>
public sealed record McpResourceInfo
{
    /// <summary>
    /// Gets the URI of the resource.
    /// </summary>
    public required Uri Uri { get; init; }

    /// <summary>
    /// Gets the name of the resource.
    /// </summary>
    public string? Name { get; init; }

    /// <summary>
    /// Gets the description of the resource.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Gets the MIME type of the resource.
    /// </summary>
    public string? MimeType { get; init; }

    /// <summary>
    /// Gets additional metadata about the resource.
    /// </summary>
    public Dictionary<string, object> Metadata { get; init; } = new(StringComparer.Ordinal);
}

/// <summary>
/// Represents the content of an MCP resource.
/// </summary>
public sealed record McpResourceContent
{
    /// <summary>
    /// Gets the URI of the resource.
    /// </summary>
    public required Uri Uri { get; init; }

    /// <summary>
    /// Gets the content of the resource.
    /// </summary>
    public IReadOnlyList<McpContent> Content { get; init; } = Array.Empty<McpContent>();

    /// <summary>
    /// Gets the MIME type of the resource.
    /// </summary>
    public string? MimeType { get; init; }

    /// <summary>
    /// Gets additional metadata about the resource.
    /// </summary>
    public Dictionary<string, object> Metadata { get; init; } = new(StringComparer.Ordinal);
}