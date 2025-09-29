namespace CyberdineDevelopment.ClaudeCode.Abstractions;

/// <summary>
/// Service for managing authentication with Claude.ai subscriptions.
/// </summary>
public interface IAuthenticationService
{
    /// <summary>
    /// Gets the current authentication state.
    /// </summary>
    /// <returns>The current authentication state.</returns>
    Task<AuthenticationState> GetAuthenticationStateAsync();

    /// <summary>
    /// Initiates the login process for subscription authentication.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the login operation.</returns>
    Task<AuthenticationState> LoginAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Logs out the current user and clears stored credentials.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the logout operation.</returns>
    Task LogoutAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Refreshes the current OAuth token if needed.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The refreshed authentication state.</returns>
    Task<AuthenticationState> RefreshTokenAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a valid access token for API calls.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A valid access token.</returns>
    Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Event fired when authentication state changes.
    /// </summary>
    event EventHandler<AuthenticationState> AuthenticationStateChanged;
}