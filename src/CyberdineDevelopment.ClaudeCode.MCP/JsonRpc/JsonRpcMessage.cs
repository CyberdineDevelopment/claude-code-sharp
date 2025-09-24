using System.Text.Json.Serialization;

namespace CyberdineDevelopment.ClaudeCode.MCP.JsonRpc;

/// <summary>
/// Base class for JSON-RPC 2.0 messages.
/// </summary>
public abstract record JsonRpcMessage
{
    /// <summary>
    /// Gets the JSON-RPC version (always "2.0").
    /// </summary>
    [JsonPropertyName("jsonrpc")]
    public string JsonRpc { get; init; } = "2.0";
}

/// <summary>
/// Represents a JSON-RPC request message.
/// </summary>
public sealed record JsonRpcRequest : JsonRpcMessage
{
    /// <summary>
    /// Gets the unique identifier for the request.
    /// </summary>
    [JsonPropertyName("id")]
    public required object Id { get; init; }

    /// <summary>
    /// Gets the method name to call.
    /// </summary>
    [JsonPropertyName("method")]
    public required string Method { get; init; }

    /// <summary>
    /// Gets the parameters for the method call.
    /// </summary>
    [JsonPropertyName("params")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public object? Params { get; init; }
}

/// <summary>
/// Represents a JSON-RPC notification message.
/// </summary>
public sealed record JsonRpcNotification : JsonRpcMessage
{
    /// <summary>
    /// Gets the method name.
    /// </summary>
    [JsonPropertyName("method")]
    public required string Method { get; init; }

    /// <summary>
    /// Gets the parameters for the notification.
    /// </summary>
    [JsonPropertyName("params")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public object? Params { get; init; }
}

/// <summary>
/// Represents a JSON-RPC response message.
/// </summary>
public sealed record JsonRpcResponse : JsonRpcMessage
{
    /// <summary>
    /// Gets the unique identifier that matches the request.
    /// </summary>
    [JsonPropertyName("id")]
    public required object Id { get; init; }

    /// <summary>
    /// Gets the result of the method call (if successful).
    /// </summary>
    [JsonPropertyName("result")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public object? Result { get; init; }

    /// <summary>
    /// Gets error information (if the call failed).
    /// </summary>
    [JsonPropertyName("error")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonRpcError? Error { get; init; }
}

/// <summary>
/// Represents a JSON-RPC error.
/// </summary>
public sealed record JsonRpcError
{
    /// <summary>
    /// Gets the error code.
    /// </summary>
    [JsonPropertyName("code")]
    public required int Code { get; init; }

    /// <summary>
    /// Gets the error message.
    /// </summary>
    [JsonPropertyName("message")]
    public required string Message { get; init; }

    /// <summary>
    /// Gets additional error data.
    /// </summary>
    [JsonPropertyName("data")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public object? Data { get; init; }
}

/// <summary>
/// Standard JSON-RPC error codes.
/// </summary>
public static class JsonRpcErrorCodes
{
    /// <summary>
    /// Parse error - Invalid JSON was received by the server.
    /// </summary>
    public const int ParseError = -32700;

    /// <summary>
    /// Invalid request - The JSON sent is not a valid Request object.
    /// </summary>
    public const int InvalidRequest = -32600;

    /// <summary>
    /// Method not found - The method does not exist / is not available.
    /// </summary>
    public const int MethodNotFound = -32601;

    /// <summary>
    /// Invalid params - Invalid method parameter(s).
    /// </summary>
    public const int InvalidParams = -32602;

    /// <summary>
    /// Internal error - Internal JSON-RPC error.
    /// </summary>
    public const int InternalError = -32603;

    /// <summary>
    /// Server error range start.
    /// </summary>
    public const int ServerErrorStart = -32099;

    /// <summary>
    /// Server error range end.
    /// </summary>
    public const int ServerErrorEnd = -32000;
}