# PowerShell Host Integration Architecture

## Overview
The PowerShell integration replaces bash operations with cross-platform PowerShell execution, providing consistent shell operations across Windows, macOS, and Linux.

## Core PowerShell Components

### 1. PowerShell Host Service (ClaudeCode.PowerShell)

```csharp
namespace ClaudeCode.PowerShell.Hosting;

/// <summary>
/// PowerShell execution service with security and context management
/// </summary>
public interface IPowerShellHost
{
    Task<PowerShellResult> ExecuteScriptAsync(string script, PowerShellContext context, CancellationToken cancellationToken = default);
    Task<PowerShellResult> ExecuteCommandAsync(string command, Dictionary<string, object>? parameters = null, PowerShellContext? context = null, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> GetAvailableModulesAsync();
    Task LoadModuleAsync(string moduleName, CancellationToken cancellationToken = default);
}

/// <summary>
/// PowerShell execution context with security constraints
/// </summary>
public sealed class PowerShellContext
{
    public required string SessionId { get; init; }
    public required string WorkingDirectory { get; init; }
    public required ISecurityContext SecurityContext { get; init; }
    public required PowerShellExecutionPolicy ExecutionPolicy { get; init; }
    public IReadOnlyDictionary<string, object> Variables { get; init; } = new Dictionary<string, object>();
    public IReadOnlyList<string> AllowedCommands { get; init; } = [];
    public IReadOnlyList<string> BlockedCommands { get; init; } = [];
    public TimeSpan Timeout { get; init; } = TimeSpan.FromMinutes(5);
}

/// <summary>
/// PowerShell script execution result
/// </summary>
public sealed class PowerShellResult
{
    public bool IsSuccess { get; private init; }
    public IReadOnlyList<PSObject> Output { get; private init; } = [];
    public IReadOnlyList<string> Errors { get; private init; } = [];
    public IReadOnlyList<string> Warnings { get; private init; } = [];
    public string? ErrorMessage { get; private init; }
    public Exception? Exception { get; private init; }

    public static PowerShellResult Success(IReadOnlyList<PSObject> output, IReadOnlyList<string> warnings = null) =>
        new() { IsSuccess = true, Output = output, Warnings = warnings ?? [] };

    public static PowerShellResult Failure(string errorMessage, IReadOnlyList<string> errors, Exception? exception = null) =>
        new() { IsSuccess = false, ErrorMessage = errorMessage, Errors = errors, Exception = exception };
}

/// <summary>
/// Secure PowerShell host implementation
/// </summary>
public sealed class PowerShellHost : IPowerShellHost, IDisposable
{
    private readonly PowerShell _powerShell;
    private readonly ISecurityService _securityService;
    private readonly ILogger<PowerShellHost> _logger;
    private readonly SemaphoreSlim _executionSemaphore;
    private bool _disposed;

    public PowerShellHost(ISecurityService securityService, ILogger<PowerShellHost> logger)
    {
        _securityService = securityService ?? throw new ArgumentNullException(nameof(securityService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _executionSemaphore = new SemaphoreSlim(1, 1);

        // Create PowerShell instance with restricted execution policy
        _powerShell = PowerShell.Create();
        ConfigurePowerShellSecurity();
    }

    public async Task<PowerShellResult> ExecuteScriptAsync(string script, PowerShellContext context, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(script);
        ArgumentNullException.ThrowIfNull(context);

        if (_disposed)
            throw new ObjectDisposedException(nameof(PowerShellHost));

        await _executionSemaphore.WaitAsync(cancellationToken);
        try
        {
            // Security validation
            var securityResult = await _securityService.ValidatePowerShellScriptAsync(script, context.SecurityContext);
            if (!securityResult.IsValid)
            {
                return PowerShellResult.Failure($"Script security validation failed: {securityResult.ErrorMessage}", []);
            }

            _logger.LogInformation("Executing PowerShell script for session {SessionId}", context.SessionId);

            // Set up execution environment
            SetupExecutionContext(context);

            // Execute script with timeout
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(context.Timeout);

            _powerShell.AddScript(script);
            var results = await Task.Run(() => _powerShell.Invoke(), cts.Token);

            var errors = _powerShell.Streams.Error.Select(e => e.ToString()).ToList();
            var warnings = _powerShell.Streams.Warning.Select(w => w.ToString()).ToList();

            if (_powerShell.HadErrors)
            {
                return PowerShellResult.Failure("PowerShell script execution failed", errors);
            }

            return PowerShellResult.Success(results, warnings);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return PowerShellResult.Failure("Script execution was cancelled", []);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing PowerShell script");
            return PowerShellResult.Failure($"Execution error: {ex.Message}", [], ex);
        }
        finally
        {
            _powerShell.Commands.Clear();
            _powerShell.Streams.ClearStreams();
            _executionSemaphore.Release();
        }
    }

    public async Task<PowerShellResult> ExecuteCommandAsync(string command, Dictionary<string, object>? parameters = null, PowerShellContext? context = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command);

        var script = BuildCommandScript(command, parameters);
        context ??= CreateDefaultContext();

        return await ExecuteScriptAsync(script, context, cancellationToken);
    }

    private void ConfigurePowerShellSecurity()
    {
        // Set restricted execution policy
        _powerShell.AddCommand("Set-ExecutionPolicy")
                  .AddParameter("ExecutionPolicy", "Restricted")
                  .AddParameter("Scope", "Process")
                  .AddParameter("Force", true);
        _powerShell.Invoke();
        _powerShell.Commands.Clear();

        // Block dangerous commands by default
        var blockedCommands = new[]
        {
            "Invoke-Expression", "Invoke-Command", "Start-Process",
            "Remove-Item", "Format-Table", "Out-File", "Set-Content"
        };

        foreach (var cmd in blockedCommands)
        {
            _powerShell.AddScript($"Remove-Item Function:\\{cmd} -Force -ErrorAction SilentlyContinue");
        }
        _powerShell.Invoke();
        _powerShell.Commands.Clear();
    }

    private void SetupExecutionContext(PowerShellContext context)
    {
        // Set working directory
        _powerShell.AddCommand("Set-Location").AddParameter("Path", context.WorkingDirectory);
        _powerShell.Invoke();
        _powerShell.Commands.Clear();

        // Set variables
        foreach (var variable in context.Variables)
        {
            _powerShell.AddCommand("Set-Variable")
                      .AddParameter("Name", variable.Key)
                      .AddParameter("Value", variable.Value);
            _powerShell.Invoke();
            _powerShell.Commands.Clear();
        }
    }

    private static string BuildCommandScript(string command, Dictionary<string, object>? parameters)
    {
        if (parameters == null || parameters.Count == 0)
            return command;

        var scriptBuilder = new StringBuilder(command);
        foreach (var param in parameters)
        {
            scriptBuilder.Append($" -{param.Key} {param.Value}");
        }

        return scriptBuilder.ToString();
    }

    private PowerShellContext CreateDefaultContext() => new()
    {
        SessionId = Guid.NewGuid().ToString(),
        WorkingDirectory = Directory.GetCurrentDirectory(),
        SecurityContext = new DefaultSecurityContext(),
        ExecutionPolicy = PowerShellExecutionPolicy.Restricted
    };

    public void Dispose()
    {
        if (_disposed)
            return;

        _powerShell?.Dispose();
        _executionSemaphore?.Dispose();
        _disposed = true;
    }
}
```

### 2. PowerShell-Based MCP Tools

```csharp
namespace ClaudeCode.MCP.Tools.PowerShell;

/// <summary>
/// PowerShell script execution MCP tool
/// </summary>
public sealed class PowerShellScriptTool : McpTool<PowerShellScriptRequest, PowerShellScriptResponse>
{
    private readonly IPowerShellHost _powerShellHost;

    public PowerShellScriptTool(IPowerShellHost powerShellHost)
    {
        _powerShellHost = powerShellHost ?? throw new ArgumentNullException(nameof(powerShellHost));
    }

    public override string Name => "powershell-script";
    public override string Description => "Execute PowerShell scripts with security constraints";
    public override IReadOnlyList<McpToolCapability> Capabilities =>
        [McpToolCapability.Execute, McpToolCapability.FileSystem, McpToolCapability.Process];

    public override IReadOnlyList<McpParameter> Parameters =>
    [
        new McpParameter("script", "PowerShell script to execute", McpParameterType.String, true),
        new McpParameter("workingDirectory", "Working directory for execution", McpParameterType.String, false),
        new McpParameter("timeout", "Execution timeout in seconds", McpParameterType.Integer, false)
    ];

    protected override async Task<PowerShellScriptRequest> DeserializeRequestAsync(string jsonRequest, CancellationToken cancellationToken)
    {
        return JsonSerializer.Deserialize<PowerShellScriptRequest>(jsonRequest) ??
               throw new InvalidOperationException("Failed to deserialize PowerShell script request");
    }

    protected override async Task<PowerShellScriptResponse> ExecuteAsync(PowerShellScriptRequest request, McpContext context, CancellationToken cancellationToken)
    {
        var psContext = new PowerShellContext
        {
            SessionId = context.SessionId,
            WorkingDirectory = request.WorkingDirectory ?? context.ProjectContext.RootPath,
            SecurityContext = context.SecurityContext,
            ExecutionPolicy = PowerShellExecutionPolicy.RemoteSigned,
            Timeout = request.Timeout.HasValue ? TimeSpan.FromSeconds(request.Timeout.Value) : TimeSpan.FromMinutes(5)
        };

        var result = await _powerShellHost.ExecuteScriptAsync(request.Script, psContext, cancellationToken);

        return new PowerShellScriptResponse
        {
            Success = result.IsSuccess,
            Output = result.Output.Select(o => o.ToString()).ToList(),
            Errors = result.Errors.ToList(),
            Warnings = result.Warnings.ToList(),
            ErrorMessage = result.ErrorMessage
        };
    }
}

/// <summary>
/// File system operations via PowerShell
/// </summary>
public sealed class PowerShellFileSystemTool : McpTool<FileSystemRequest, FileSystemResponse>
{
    private readonly IPowerShellHost _powerShellHost;

    public PowerShellFileSystemTool(IPowerShellHost powerShellHost)
    {
        _powerShellHost = powerShellHost ?? throw new ArgumentNullException(nameof(powerShellHost));
    }

    public override string Name => "filesystem-powershell";
    public override string Description => "File system operations using PowerShell";
    public override IReadOnlyList<McpToolCapability> Capabilities => [McpToolCapability.FileSystem];

    public override IReadOnlyList<McpParameter> Parameters =>
    [
        new McpParameter("operation", "File system operation (list, read, write, delete, copy, move)", McpParameterType.String, true),
        new McpParameter("path", "File or directory path", McpParameterType.String, true),
        new McpParameter("content", "Content for write operations", McpParameterType.String, false),
        new McpParameter("destination", "Destination path for copy/move operations", McpParameterType.String, false)
    ];

    protected override async Task<FileSystemRequest> DeserializeRequestAsync(string jsonRequest, CancellationToken cancellationToken)
    {
        return JsonSerializer.Deserialize<FileSystemRequest>(jsonRequest) ??
               throw new InvalidOperationException("Failed to deserialize file system request");
    }

    protected override async Task<FileSystemResponse> ExecuteAsync(FileSystemRequest request, McpContext context, CancellationToken cancellationToken)
    {
        var script = BuildFileSystemScript(request);

        var psContext = new PowerShellContext
        {
            SessionId = context.SessionId,
            WorkingDirectory = context.ProjectContext.RootPath,
            SecurityContext = context.SecurityContext,
            ExecutionPolicy = PowerShellExecutionPolicy.RemoteSigned
        };

        var result = await _powerShellHost.ExecuteScriptAsync(script, psContext, cancellationToken);

        return new FileSystemResponse
        {
            Success = result.IsSuccess,
            Result = result.Output.FirstOrDefault()?.ToString(),
            ErrorMessage = result.ErrorMessage
        };
    }

    private static string BuildFileSystemScript(FileSystemRequest request) => request.Operation.ToLowerInvariant() switch
    {
        "list" => $"Get-ChildItem -Path '{request.Path}' | ConvertTo-Json",
        "read" => $"Get-Content -Path '{request.Path}' -Raw",
        "write" => $"Set-Content -Path '{request.Path}' -Value @'\n{request.Content}\n'@ -NoNewline",
        "delete" => $"Remove-Item -Path '{request.Path}' -Force",
        "copy" => $"Copy-Item -Path '{request.Path}' -Destination '{request.Destination}' -Force",
        "move" => $"Move-Item -Path '{request.Path}' -Destination '{request.Destination}' -Force",
        _ => throw new ArgumentException($"Unsupported file system operation: {request.Operation}")
    };
}
```

### 3. PowerShell Security Integration

```csharp
namespace ClaudeCode.PowerShell.Security;

/// <summary>
/// PowerShell-specific security validation
/// </summary>
public interface IPowerShellSecurityValidator
{
    Task<SecurityValidationResult> ValidateScriptAsync(string script, ISecurityContext context);
    Task<SecurityValidationResult> ValidateCommandAsync(string command, ISecurityContext context);
}

/// <summary>
/// PowerShell security validator implementation
/// </summary>
public sealed class PowerShellSecurityValidator : IPowerShellSecurityValidator
{
    private readonly ILogger<PowerShellSecurityValidator> _logger;
    private readonly IReadOnlySet<string> _dangerousCommands;
    private readonly IReadOnlySet<string> _dangerousPatterns;

    public PowerShellSecurityValidator(ILogger<PowerShellSecurityValidator> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _dangerousCommands = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Invoke-Expression", "Invoke-Command", "Start-Process", "New-Object",
            "Add-Type", "Invoke-WebRequest", "Invoke-RestMethod", "Enter-PSSession",
            "New-PSSession", "Import-Module", "Install-Module"
        };

        _dangerousPatterns = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "DownloadString", "DownloadFile", "WebClient", "System.Net",
            "Reflection.Assembly", "Runtime.InteropServices", "Marshal",
            "PointerType", "GetMethod", "Invoke"
        };
    }

    public Task<SecurityValidationResult> ValidateScriptAsync(string script, ISecurityContext context)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(script);
        ArgumentNullException.ThrowIfNull(context);

        // Check for dangerous commands
        foreach (var command in _dangerousCommands)
        {
            if (script.Contains(command, StringComparison.OrdinalIgnoreCase))
            {
                if (!context.HasPermission($"powershell.command.{command.ToLowerInvariant()}"))
                {
                    return Task.FromResult(SecurityValidationResult.Deny($"Dangerous command '{command}' not allowed"));
                }
            }
        }

        // Check for dangerous patterns
        foreach (var pattern in _dangerousPatterns)
        {
            if (script.Contains(pattern, StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(SecurityValidationResult.Deny($"Dangerous pattern '{pattern}' detected"));
            }
        }

        // Additional validations based on security context
        if (!context.HasPermission("powershell.filesystem.write") && ContainsWriteOperations(script))
        {
            return Task.FromResult(SecurityValidationResult.Deny("File system write operations not allowed"));
        }

        if (!context.HasPermission("powershell.network.access") && ContainsNetworkOperations(script))
        {
            return Task.FromResult(SecurityValidationResult.Deny("Network operations not allowed"));
        }

        return Task.FromResult(SecurityValidationResult.Allow());
    }

    public Task<SecurityValidationResult> ValidateCommandAsync(string command, ISecurityContext context)
    {
        // Extract command name and validate
        var commandName = ExtractCommandName(command);
        return ValidateScriptAsync(commandName, context);
    }

    private static bool ContainsWriteOperations(string script)
    {
        var writeOperations = new[] { "Set-Content", "Add-Content", "Out-File", "New-Item", "Copy-Item", "Move-Item", "Remove-Item" };
        return writeOperations.Any(op => script.Contains(op, StringComparison.OrdinalIgnoreCase));
    }

    private static bool ContainsNetworkOperations(string script)
    {
        var networkOperations = new[] { "Invoke-WebRequest", "Invoke-RestMethod", "Test-NetConnection", "New-NetTCPSession" };
        return networkOperations.Any(op => script.Contains(op, StringComparison.OrdinalIgnoreCase));
    }

    private static string ExtractCommandName(string command)
    {
        var parts = command.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length > 0 ? parts[0] : command;
    }
}
```