# Security Framework Architecture

## Overview
The security framework provides comprehensive permission-based access control for all tools, operations, and resources with defense-in-depth principles.

## Core Security Components

### 1. Security Context and Permissions (ClaudeCode.Security)

```csharp
namespace ClaudeCode.Security.Core;

/// <summary>
/// Security context for current session with permission checking
/// </summary>
public interface ISecurityContext
{
    string UserId { get; }
    string SessionId { get; }
    SecurityLevel SecurityLevel { get; }
    IReadOnlySet<string> Permissions { get; }
    IReadOnlyDictionary<string, object> Claims { get; }

    bool HasPermission(string permission);
    bool HasAnyPermission(params string[] permissions);
    bool HasAllPermissions(params string[] permissions);
    T? GetClaim<T>(string claimName);
}

/// <summary>
/// Permission-based security context implementation
/// </summary>
public sealed class SecurityContext : ISecurityContext
{
    public string UserId { get; }
    public string SessionId { get; }
    public SecurityLevel SecurityLevel { get; }
    public IReadOnlySet<string> Permissions { get; }
    public IReadOnlyDictionary<string, object> Claims { get; }

    public SecurityContext(
        string userId,
        string sessionId,
        SecurityLevel securityLevel,
        IReadOnlySet<string> permissions,
        IReadOnlyDictionary<string, object>? claims = null)
    {
        UserId = userId ?? throw new ArgumentNullException(nameof(userId));
        SessionId = sessionId ?? throw new ArgumentNullException(nameof(sessionId));
        SecurityLevel = securityLevel;
        Permissions = permissions ?? throw new ArgumentNullException(nameof(permissions));
        Claims = claims ?? new Dictionary<string, object>();
    }

    public bool HasPermission(string permission)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(permission);
        return Permissions.Contains(permission) || Permissions.Contains("*");
    }

    public bool HasAnyPermission(params string[] permissions)
    {
        ArgumentNullException.ThrowIfNull(permissions);
        return permissions.Any(HasPermission);
    }

    public bool HasAllPermissions(params string[] permissions)
    {
        ArgumentNullException.ThrowIfNull(permissions);
        return permissions.All(HasPermission);
    }

    public T? GetClaim<T>(string claimName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(claimName);
        return Claims.TryGetValue(claimName, out var value) && value is T typedValue ? typedValue : default;
    }
}

/// <summary>
/// Security level enumeration
/// </summary>
public enum SecurityLevel
{
    Restricted = 0,    // Minimal permissions, read-only operations
    Standard = 1,      // Standard user permissions
    Elevated = 2,      // Administrative permissions
    System = 3         // Full system access (for internal operations only)
}

/// <summary>
/// Security validation result
/// </summary>
public sealed class SecurityValidationResult
{
    public bool IsValid { get; private init; }
    public string? ErrorMessage { get; private init; }
    public IReadOnlyList<string> RequiredPermissions { get; private init; } = [];

    public static SecurityValidationResult Valid() => new() { IsValid = true };
    public static SecurityValidationResult Invalid(string errorMessage, IReadOnlyList<string>? requiredPermissions = null) =>
        new() { IsValid = false, ErrorMessage = errorMessage, RequiredPermissions = requiredPermissions ?? [] };
}

/// <summary>
/// Permission check result
/// </summary>
public sealed class PermissionResult
{
    public bool IsAllowed { get; private init; }
    public string? Reason { get; private init; }
    public IReadOnlyList<string> MissingPermissions { get; private init; } = [];

    public static PermissionResult Allow() => new() { IsAllowed = true };
    public static PermissionResult Deny(string reason, IReadOnlyList<string>? missingPermissions = null) =>
        new() { IsAllowed = false, Reason = reason, MissingPermissions = missingPermissions ?? [] };
}
```

### 2. Security Service (ClaudeCode.Security)

```csharp
namespace ClaudeCode.Security.Services;

/// <summary>
/// Core security service for validation and permission checking
/// </summary>
public interface ISecurityService
{
    Task<SecurityValidationResult> ValidateToolAsync(IMcpTool tool);
    Task<PermissionResult> CheckExecutionPermissionAsync(IMcpTool tool, ISecurityContext context);
    Task<SecurityValidationResult> ValidatePowerShellScriptAsync(string script, ISecurityContext context);
    Task<PermissionResult> CheckFileAccessPermissionAsync(string path, FileAccessType accessType, ISecurityContext context);
    Task<PermissionResult> CheckNetworkAccessPermissionAsync(string host, int port, ISecurityContext context);
    Task<ISecurityContext> CreateSecurityContextAsync(string userId, SecurityLevel securityLevel, IReadOnlyList<string>? additionalPermissions = null);
}

/// <summary>
/// Security service implementation with comprehensive validation
/// </summary>
public sealed class SecurityService : ISecurityService
{
    private readonly ISecurityPolicyProvider _policyProvider;
    private readonly IPermissionProvider _permissionProvider;
    private readonly ISecurityAuditor _auditor;
    private readonly ILogger<SecurityService> _logger;

    public SecurityService(
        ISecurityPolicyProvider policyProvider,
        IPermissionProvider permissionProvider,
        ISecurityAuditor auditor,
        ILogger<SecurityService> logger)
    {
        _policyProvider = policyProvider ?? throw new ArgumentNullException(nameof(policyProvider));
        _permissionProvider = permissionProvider ?? throw new ArgumentNullException(nameof(permissionProvider));
        _auditor = auditor ?? throw new ArgumentNullException(nameof(auditor));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<SecurityValidationResult> ValidateToolAsync(IMcpTool tool)
    {
        ArgumentNullException.ThrowIfNull(tool);

        try
        {
            var policy = await _policyProvider.GetToolPolicyAsync(tool.Name);

            // Check if tool is allowed to be registered
            if (!policy.IsAllowed)
            {
                return SecurityValidationResult.Invalid($"Tool '{tool.Name}' is not allowed by security policy");
            }

            // Validate tool capabilities against policy
            foreach (var capability in tool.Capabilities)
            {
                if (!policy.AllowedCapabilities.Contains(capability))
                {
                    return SecurityValidationResult.Invalid(
                        $"Tool '{tool.Name}' has disallowed capability '{capability}'",
                        [policy.RequiredPermissions.FirstOrDefault(p => p.Contains(capability.ToString().ToLowerInvariant()))]
                    );
                }
            }

            // Validate required permissions are reasonable
            var requiredPermissions = await GetToolRequiredPermissionsAsync(tool);
            foreach (var permission in requiredPermissions)
            {
                if (!await _permissionProvider.IsValidPermissionAsync(permission))
                {
                    return SecurityValidationResult.Invalid($"Tool '{tool.Name}' requires invalid permission '{permission}'");
                }
            }

            await _auditor.AuditToolValidationAsync(tool, SecurityValidationResult.Valid());
            return SecurityValidationResult.Valid();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating tool {ToolName}", tool.Name);
            return SecurityValidationResult.Invalid($"Validation error: {ex.Message}");
        }
    }

    public async Task<PermissionResult> CheckExecutionPermissionAsync(IMcpTool tool, ISecurityContext context)
    {
        ArgumentNullException.ThrowIfNull(tool);
        ArgumentNullException.ThrowIfNull(context);

        try
        {
            var requiredPermissions = await GetToolRequiredPermissionsAsync(tool);
            var missingPermissions = new List<string>();

            // Check each required permission
            foreach (var permission in requiredPermissions)
            {
                if (!context.HasPermission(permission))
                {
                    missingPermissions.Add(permission);
                }
            }

            if (missingPermissions.Count > 0)
            {
                var result = PermissionResult.Deny(
                    $"Missing required permissions for tool '{tool.Name}'",
                    missingPermissions
                );

                await _auditor.AuditPermissionCheckAsync(tool, context, result);
                return result;
            }

            // Check security level requirements
            var policy = await _policyProvider.GetToolPolicyAsync(tool.Name);
            if (context.SecurityLevel < policy.MinimumSecurityLevel)
            {
                var result = PermissionResult.Deny(
                    $"Insufficient security level for tool '{tool.Name}'. Required: {policy.MinimumSecurityLevel}, Current: {context.SecurityLevel}"
                );

                await _auditor.AuditPermissionCheckAsync(tool, context, result);
                return result;
            }

            var allowResult = PermissionResult.Allow();
            await _auditor.AuditPermissionCheckAsync(tool, context, allowResult);
            return allowResult;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking execution permission for tool {ToolName}", tool.Name);
            return PermissionResult.Deny($"Permission check error: {ex.Message}");
        }
    }

    public async Task<SecurityValidationResult> ValidatePowerShellScriptAsync(string script, ISecurityContext context)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(script);
        ArgumentNullException.ThrowIfNull(context);

        // Delegate to PowerShell-specific security validator
        var validator = new PowerShellSecurityValidator(_logger);
        return await validator.ValidateScriptAsync(script, context);
    }

    public async Task<PermissionResult> CheckFileAccessPermissionAsync(string path, FileAccessType accessType, ISecurityContext context)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(context);

        var requiredPermission = accessType switch
        {
            FileAccessType.Read => "filesystem.read",
            FileAccessType.Write => "filesystem.write",
            FileAccessType.Execute => "filesystem.execute",
            FileAccessType.Delete => "filesystem.delete",
            _ => throw new ArgumentException($"Unknown file access type: {accessType}")
        };

        if (!context.HasPermission(requiredPermission))
        {
            return PermissionResult.Deny($"Missing permission '{requiredPermission}' for file access", [requiredPermission]);
        }

        // Check path-specific permissions
        var normalizedPath = Path.GetFullPath(path);
        var policy = await _policyProvider.GetFileAccessPolicyAsync(normalizedPath);

        if (!policy.IsAllowed(accessType))
        {
            return PermissionResult.Deny($"File access to '{normalizedPath}' is not allowed by security policy");
        }

        return PermissionResult.Allow();
    }

    public async Task<PermissionResult> CheckNetworkAccessPermissionAsync(string host, int port, ISecurityContext context)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        ArgumentNullException.ThrowIfNull(context);

        if (!context.HasPermission("network.access"))
        {
            return PermissionResult.Deny("Missing permission 'network.access' for network operations", ["network.access"]);
        }

        var policy = await _policyProvider.GetNetworkAccessPolicyAsync(host, port);
        if (!policy.IsAllowed)
        {
            return PermissionResult.Deny($"Network access to '{host}:{port}' is not allowed by security policy");
        }

        return PermissionResult.Allow();
    }

    public async Task<ISecurityContext> CreateSecurityContextAsync(string userId, SecurityLevel securityLevel, IReadOnlyList<string>? additionalPermissions = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        var basePermissions = await _permissionProvider.GetBasePermissionsAsync(securityLevel);
        var allPermissions = new HashSet<string>(basePermissions, StringComparer.OrdinalIgnoreCase);

        if (additionalPermissions != null)
        {
            foreach (var permission in additionalPermissions)
            {
                allPermissions.Add(permission);
            }
        }

        var claims = new Dictionary<string, object>
        {
            ["created_at"] = DateTimeOffset.UtcNow,
            ["security_level"] = securityLevel.ToString()
        };

        return new SecurityContext(
            userId,
            Guid.NewGuid().ToString(),
            securityLevel,
            allPermissions,
            claims
        );
    }

    private async Task<IReadOnlyList<string>> GetToolRequiredPermissionsAsync(IMcpTool tool)
    {
        var permissions = new HashSet<string>();

        // Map capabilities to permissions
        foreach (var capability in tool.Capabilities)
        {
            var capabilityPermissions = await _permissionProvider.GetPermissionsForCapabilityAsync(capability);
            foreach (var permission in capabilityPermissions)
            {
                permissions.Add(permission);
            }
        }

        return permissions.ToList();
    }
}

/// <summary>
/// File access type enumeration
/// </summary>
public enum FileAccessType
{
    Read,
    Write,
    Execute,
    Delete
}
```

### 3. Security Policies and Permissions (ClaudeCode.Security)

```csharp
namespace ClaudeCode.Security.Policies;

/// <summary>
/// Security policy provider for tools and resources
/// </summary>
public interface ISecurityPolicyProvider
{
    Task<ToolSecurityPolicy> GetToolPolicyAsync(string toolName);
    Task<FileAccessPolicy> GetFileAccessPolicyAsync(string path);
    Task<NetworkAccessPolicy> GetNetworkAccessPolicyAsync(string host, int port);
}

/// <summary>
/// Permission provider for mapping capabilities to permissions
/// </summary>
public interface IPermissionProvider
{
    Task<IReadOnlyList<string>> GetBasePermissionsAsync(SecurityLevel securityLevel);
    Task<IReadOnlyList<string>> GetPermissionsForCapabilityAsync(McpToolCapability capability);
    Task<bool> IsValidPermissionAsync(string permission);
}

/// <summary>
/// Tool-specific security policy
/// </summary>
public sealed class ToolSecurityPolicy
{
    public required string ToolName { get; init; }
    public required bool IsAllowed { get; init; }
    public required SecurityLevel MinimumSecurityLevel { get; init; }
    public required IReadOnlySet<McpToolCapability> AllowedCapabilities { get; init; }
    public required IReadOnlyList<string> RequiredPermissions { get; init; }
    public IReadOnlyDictionary<string, object> Metadata { get; init; } = new Dictionary<string, object>();
}

/// <summary>
/// File access security policy
/// </summary>
public sealed class FileAccessPolicy
{
    public required string Path { get; init; }
    public required IReadOnlySet<FileAccessType> AllowedOperations { get; init; }
    public required SecurityLevel MinimumSecurityLevel { get; init; }

    public bool IsAllowed(FileAccessType accessType) => AllowedOperations.Contains(accessType);
}

/// <summary>
/// Network access security policy
/// </summary>
public sealed class NetworkAccessPolicy
{
    public required string Host { get; init; }
    public required int Port { get; init; }
    public required bool IsAllowed { get; init; }
    public required SecurityLevel MinimumSecurityLevel { get; init; }
    public IReadOnlyList<string> AllowedProtocols { get; init; } = [];
}

/// <summary>
/// Security audit service for logging security events
/// </summary>
public interface ISecurityAuditor
{
    Task AuditToolValidationAsync(IMcpTool tool, SecurityValidationResult result);
    Task AuditPermissionCheckAsync(IMcpTool tool, ISecurityContext context, PermissionResult result);
    Task AuditFileAccessAsync(string path, FileAccessType accessType, ISecurityContext context, PermissionResult result);
    Task AuditNetworkAccessAsync(string host, int port, ISecurityContext context, PermissionResult result);
}

/// <summary>
/// Default permission provider with standard permission mappings
/// </summary>
public sealed class DefaultPermissionProvider : IPermissionProvider
{
    private readonly ILogger<DefaultPermissionProvider> _logger;

    public DefaultPermissionProvider(ILogger<DefaultPermissionProvider> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task<IReadOnlyList<string>> GetBasePermissionsAsync(SecurityLevel securityLevel)
    {
        var permissions = securityLevel switch
        {
            SecurityLevel.Restricted => new[]
            {
                "filesystem.read",
                "tool.basic"
            },
            SecurityLevel.Standard => new[]
            {
                "filesystem.read",
                "filesystem.write",
                "powershell.execute",
                "tool.basic",
                "tool.advanced"
            },
            SecurityLevel.Elevated => new[]
            {
                "filesystem.read",
                "filesystem.write",
                "filesystem.execute",
                "filesystem.delete",
                "powershell.execute",
                "powershell.command.*",
                "network.access",
                "tool.basic",
                "tool.advanced",
                "tool.system"
            },
            SecurityLevel.System => new[] { "*" }, // All permissions
            _ => throw new ArgumentException($"Unknown security level: {securityLevel}")
        };

        return Task.FromResult<IReadOnlyList<string>>(permissions);
    }

    public Task<IReadOnlyList<string>> GetPermissionsForCapabilityAsync(McpToolCapability capability)
    {
        var permissions = capability switch
        {
            McpToolCapability.FileSystem => new[] { "filesystem.read", "filesystem.write" },
            McpToolCapability.Execute => new[] { "powershell.execute" },
            McpToolCapability.Network => new[] { "network.access" },
            McpToolCapability.Process => new[] { "process.create", "process.manage" },
            McpToolCapability.System => new[] { "system.access" },
            _ => Array.Empty<string>()
        };

        return Task.FromResult<IReadOnlyList<string>>(permissions);
    }

    public Task<bool> IsValidPermissionAsync(string permission)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(permission);

        // Basic permission format validation
        var validPatterns = new[]
        {
            "filesystem.*",
            "powershell.*",
            "network.*",
            "process.*",
            "system.*",
            "tool.*",
            "*"
        };

        return Task.FromResult(validPatterns.Any(pattern =>
            pattern == "*" ||
            permission.StartsWith(pattern.Replace("*", ""), StringComparison.OrdinalIgnoreCase) ||
            permission == pattern));
    }
}
```