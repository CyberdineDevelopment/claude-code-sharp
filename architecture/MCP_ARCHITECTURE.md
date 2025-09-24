# MCP (Model Context Protocol) Architecture

## Overview
The MCP architecture provides extensible tool discovery, registration, and execution with security-first design principles.

## Core MCP Components

### 1. MCP Protocol Models (ClaudeCode.Core)

```csharp
namespace ClaudeCode.Core.MCP;

/// <summary>
/// Base interface for all MCP tools
/// </summary>
public interface IMcpTool
{
    string Name { get; }
    string Description { get; }
    string Version { get; }
    IReadOnlyList<McpToolCapability> Capabilities { get; }
    IReadOnlyList<McpParameter> Parameters { get; }
    Task<McpResult> ExecuteAsync(McpContext context, CancellationToken cancellationToken = default);
}

/// <summary>
/// Strongly-typed MCP tool base class
/// </summary>
public abstract class McpTool<TRequest, TResponse> : IMcpTool
    where TRequest : class
    where TResponse : class
{
    public abstract string Name { get; }
    public abstract string Description { get; }
    public virtual string Version => "1.0.0";
    public abstract IReadOnlyList<McpToolCapability> Capabilities { get; }
    public abstract IReadOnlyList<McpParameter> Parameters { get; }

    public async Task<McpResult> ExecuteAsync(McpContext context, CancellationToken cancellationToken = default)
    {
        var request = await DeserializeRequestAsync(context.Request, cancellationToken);
        var response = await ExecuteAsync(request, context, cancellationToken);
        return McpResult.Success(response);
    }

    protected abstract Task<TRequest> DeserializeRequestAsync(string jsonRequest, CancellationToken cancellationToken);
    protected abstract Task<TResponse> ExecuteAsync(TRequest request, McpContext context, CancellationToken cancellationToken);
}

/// <summary>
/// MCP execution context with security and session information
/// </summary>
public sealed class McpContext
{
    public required string SessionId { get; init; }
    public required string Request { get; init; }
    public required ISecurityContext SecurityContext { get; init; }
    public required IProjectContext ProjectContext { get; init; }
    public IReadOnlyDictionary<string, object> Metadata { get; init; } = new Dictionary<string, object>();
}

/// <summary>
/// MCP tool execution result
/// </summary>
public sealed class McpResult
{
    public bool IsSuccess { get; private init; }
    public object? Data { get; private init; }
    public string? ErrorMessage { get; private init; }
    public Exception? Exception { get; private init; }

    public static McpResult Success<T>(T data) => new() { IsSuccess = true, Data = data };
    public static McpResult Failure(string errorMessage, Exception? exception = null) =>
        new() { IsSuccess = false, ErrorMessage = errorMessage, Exception = exception };
}
```

### 2. Tool Discovery and Registration (ClaudeCode.MCP)

```csharp
namespace ClaudeCode.MCP.Discovery;

/// <summary>
/// MCP tool registry with discovery capabilities
/// </summary>
public interface IMcpToolRegistry
{
    Task RegisterToolAsync<T>() where T : class, IMcpTool;
    Task RegisterToolAsync(IMcpTool tool);
    Task<IMcpTool?> GetToolAsync(string name);
    Task<IReadOnlyList<IMcpTool>> GetAllToolsAsync();
    Task<IReadOnlyList<IMcpTool>> GetToolsByCapabilityAsync(McpToolCapability capability);
    Task UnregisterToolAsync(string name);
}

/// <summary>
/// Tool discovery service for loading tools from assemblies and plugins
/// </summary>
public interface IMcpToolDiscovery
{
    Task<IReadOnlyList<IMcpTool>> DiscoverToolsAsync(string assemblyPath);
    Task<IReadOnlyList<IMcpTool>> DiscoverToolsInDirectoryAsync(string directoryPath);
    Task<IReadOnlyList<IMcpTool>> DiscoverBuiltInToolsAsync();
}

/// <summary>
/// MCP tool registry implementation with caching and security
/// </summary>
public sealed class McpToolRegistry : IMcpToolRegistry
{
    private readonly ConcurrentDictionary<string, IMcpTool> _tools = new(StringComparer.OrdinalIgnoreCase);
    private readonly ISecurityService _securityService;
    private readonly ILogger<McpToolRegistry> _logger;

    public McpToolRegistry(ISecurityService securityService, ILogger<McpToolRegistry> logger)
    {
        _securityService = securityService ?? throw new ArgumentNullException(nameof(securityService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task RegisterToolAsync<T>() where T : class, IMcpTool
    {
        var tool = Activator.CreateInstance<T>();
        await RegisterToolAsync(tool);
    }

    public async Task RegisterToolAsync(IMcpTool tool)
    {
        ArgumentNullException.ThrowIfNull(tool);

        // Validate tool security permissions
        var validationResult = await _securityService.ValidateToolAsync(tool);
        if (!validationResult.IsValid)
        {
            _logger.LogWarning("Tool {ToolName} failed security validation: {Reason}",
                tool.Name, validationResult.ErrorMessage);
            throw new SecurityException($"Tool {tool.Name} failed security validation: {validationResult.ErrorMessage}");
        }

        _tools.AddOrUpdate(tool.Name, tool, (_, _) => tool);
        _logger.LogInformation("Registered MCP tool: {ToolName} v{Version}", tool.Name, tool.Version);
    }

    public Task<IMcpTool?> GetToolAsync(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return Task.FromResult(_tools.TryGetValue(name, out var tool) ? tool : null);
    }

    public Task<IReadOnlyList<IMcpTool>> GetAllToolsAsync()
    {
        return Task.FromResult<IReadOnlyList<IMcpTool>>(_tools.Values.ToList());
    }

    public async Task<IReadOnlyList<IMcpTool>> GetToolsByCapabilityAsync(McpToolCapability capability)
    {
        var allTools = await GetAllToolsAsync();
        return allTools.Where(t => t.Capabilities.Contains(capability)).ToList();
    }

    public Task UnregisterToolAsync(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        _tools.TryRemove(name, out _);
        _logger.LogInformation("Unregistered MCP tool: {ToolName}", name);
        return Task.CompletedTask;
    }
}
```

### 3. Tool Execution Engine (ClaudeCode.MCP)

```csharp
namespace ClaudeCode.MCP.Execution;

/// <summary>
/// MCP tool execution engine with security and context management
/// </summary>
public interface IMcpExecutionEngine
{
    Task<McpResult> ExecuteToolAsync(string toolName, McpContext context, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<IMcpTool>> GetAvailableToolsAsync(ISecurityContext securityContext);
}

/// <summary>
/// Secure MCP execution engine implementation
/// </summary>
public sealed class McpExecutionEngine : IMcpExecutionEngine
{
    private readonly IMcpToolRegistry _toolRegistry;
    private readonly ISecurityService _securityService;
    private readonly ILogger<McpExecutionEngine> _logger;

    public McpExecutionEngine(
        IMcpToolRegistry toolRegistry,
        ISecurityService securityService,
        ILogger<McpExecutionEngine> logger)
    {
        _toolRegistry = toolRegistry ?? throw new ArgumentNullException(nameof(toolRegistry));
        _securityService = securityService ?? throw new ArgumentNullException(nameof(securityService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<McpResult> ExecuteToolAsync(string toolName, McpContext context, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toolName);
        ArgumentNullException.ThrowIfNull(context);

        try
        {
            var tool = await _toolRegistry.GetToolAsync(toolName);
            if (tool == null)
            {
                return McpResult.Failure($"Tool '{toolName}' not found");
            }

            // Check execution permissions
            var permissionCheck = await _securityService.CheckExecutionPermissionAsync(tool, context.SecurityContext);
            if (!permissionCheck.IsAllowed)
            {
                return McpResult.Failure($"Access denied for tool '{toolName}': {permissionCheck.Reason}");
            }

            _logger.LogInformation("Executing MCP tool: {ToolName} for session {SessionId}", toolName, context.SessionId);

            var result = await tool.ExecuteAsync(context, cancellationToken);

            _logger.LogInformation("MCP tool {ToolName} completed with success: {Success}", toolName, result.IsSuccess);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing MCP tool {ToolName}", toolName);
            return McpResult.Failure($"Execution error: {ex.Message}", ex);
        }
    }

    public async Task<IReadOnlyList<IMcpTool>> GetAvailableToolsAsync(ISecurityContext securityContext)
    {
        var allTools = await _toolRegistry.GetAllToolsAsync();
        var availableTools = new List<IMcpTool>();

        foreach (var tool in allTools)
        {
            var permissionCheck = await _securityService.CheckExecutionPermissionAsync(tool, securityContext);
            if (permissionCheck.IsAllowed)
            {
                availableTools.Add(tool);
            }
        }

        return availableTools;
    }
}
```