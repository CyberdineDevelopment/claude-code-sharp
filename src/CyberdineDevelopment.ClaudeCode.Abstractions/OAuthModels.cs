namespace CyberdineDevelopment.ClaudeCode.Abstractions;

/// <summary>
/// OAuth token information for subscription authentication.
/// </summary>
public sealed record OAuthToken
{
    /// <summary>
    /// Gets the access token.
    /// </summary>
    public required string AccessToken { get; init; }

    /// <summary>
    /// Gets the refresh token.
    /// </summary>
    public required string RefreshToken { get; init; }

    /// <summary>
    /// Gets the token type (usually "Bearer").
    /// </summary>
    public string TokenType { get; init; } = "Bearer";

    /// <summary>
    /// Gets the token expiration time.
    /// </summary>
    public DateTimeOffset ExpiresAt { get; init; }

    /// <summary>
    /// Gets the scope of the token.
    /// </summary>
    public string? Scope { get; init; }

    /// <summary>
    /// Gets a value indicating whether the token is expired.
    /// </summary>
    public bool IsExpired => DateTimeOffset.UtcNow >= ExpiresAt;

    /// <summary>
    /// Gets a value indicating whether the token needs refresh (expires within 5 minutes).
    /// </summary>
    public bool NeedsRefresh => DateTimeOffset.UtcNow.AddMinutes(5) >= ExpiresAt;
}

/// <summary>
/// User information from Claude.ai authentication.
/// </summary>
public sealed record ClaudeUser
{
    /// <summary>
    /// Gets the user ID.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// Gets the user's email address.
    /// </summary>
    public required string Email { get; init; }

    /// <summary>
    /// Gets the user's display name.
    /// </summary>
    public string? Name { get; init; }

    /// <summary>
    /// Gets the user's subscription plan.
    /// </summary>
    public string? Plan { get; init; }

    /// <summary>
    /// Gets the organization ID (for team accounts).
    /// </summary>
    public string? OrganizationId { get; init; }
}

/// <summary>
/// Authentication state for the current session.
/// </summary>
public sealed record AuthenticationState
{
    /// <summary>
    /// Gets a value indicating whether the user is authenticated.
    /// </summary>
    public required bool IsAuthenticated { get; init; }

    /// <summary>
    /// Gets the current user information.
    /// </summary>
    public ClaudeUser? User { get; init; }

    /// <summary>
    /// Gets the OAuth token.
    /// </summary>
    public OAuthToken? Token { get; init; }

    /// <summary>
    /// Gets the authentication method used.
    /// </summary>
    public AuthenticationMethod Method { get; init; }
}

/// <summary>
/// Available authentication methods.
/// </summary>
public enum AuthenticationMethod
{
    /// <summary>
    /// No authentication.
    /// </summary>
    None,

    /// <summary>
    /// API key authentication (paid credits).
    /// </summary>
    ApiKey,

    /// <summary>
    /// OAuth subscription authentication (Pro/Max plan).
    /// </summary>
    Subscription
}