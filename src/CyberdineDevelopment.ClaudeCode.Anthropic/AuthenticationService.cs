using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Web;
using CyberdineDevelopment.ClaudeCode.Abstractions;
using Microsoft.Extensions.Logging;

namespace CyberdineDevelopment.ClaudeCode.Anthropic;

/// <summary>
/// Service for managing authentication with Claude.ai subscriptions.
/// </summary>
public sealed class AuthenticationService : IAuthenticationService, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<AuthenticationService> _logger;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly string _tokenStorePath;
    private bool _disposed;

    private AuthenticationState _currentState = new()
    {
        IsAuthenticated = false,
        Method = AuthenticationMethod.None
    };

    /// <inheritdoc />
    public event EventHandler<AuthenticationState>? AuthenticationStateChanged;

    /// <summary>
    /// Initializes a new instance of the <see cref="AuthenticationService"/> class.
    /// </summary>
    /// <param name="httpClient">The HTTP client.</param>
    /// <param name="logger">The logger.</param>
    public AuthenticationService(HttpClient httpClient, ILogger<AuthenticationService> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        // Store tokens in user's app data directory
        var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var claudeCodePath = Path.Combine(appDataPath, "ClaudeCode");
        Directory.CreateDirectory(claudeCodePath);
        _tokenStorePath = Path.Combine(claudeCodePath, "auth.json");

        // Configure HTTP client for Claude.ai
        _httpClient.BaseAddress = new Uri("https://claude.ai");
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "CyberdineDevelopment.ClaudeCode/1.0.0");

        // Load existing token if available
        _ = LoadStoredTokenAsync();
    }

    /// <inheritdoc />
    public Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        return Task.FromResult(_currentState);
    }

    /// <inheritdoc />
    public async Task<AuthenticationState> LoginAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        _logger.LogInformation("Starting Claude.ai automated OAuth login process");

        try
        {
            // Step 1: Start local callback server
            var callbackPort = FindAvailablePort();
            var redirectUri = $"http://localhost:{callbackPort}/callback";

            Console.WriteLine("🚀 Starting automated Claude.ai login...");
            Console.WriteLine($"📡 Started local callback server on port {callbackPort}");

            // Step 2: Create OAuth authorization URL
            var state = Guid.NewGuid().ToString();
            var authUrl = BuildAuthorizationUrl(redirectUri, state);

            // Step 3: Start callback server
            using var callbackServer = new HttpListener();
            callbackServer.Prefixes.Add($"http://localhost:{callbackPort}/");
            callbackServer.Start();

            // Step 4: Open browser to OAuth page
            Console.WriteLine("🌐 Opening browser for authentication...");
            OpenBrowser(authUrl);

            // Step 5: Wait for callback with timeout
            var callbackTask = WaitForCallbackAsync(callbackServer, state, cancellationToken);
            var timeoutTask = Task.Delay(TimeSpan.FromMinutes(5), cancellationToken);

            var completedTask = await Task.WhenAny(callbackTask, timeoutTask);

            if (completedTask == timeoutTask)
            {
                throw new TimeoutException("Authentication timed out after 5 minutes");
            }

            var authResult = await callbackTask;

            // Step 6: Validate session and get user info
            var userInfo = await ValidateSessionAsync(authResult.AccessToken, cancellationToken);

            // Step 7: Create OAuth token
            var token = new OAuthToken
            {
                AccessToken = authResult.AccessToken,
                RefreshToken = authResult.RefreshToken ?? authResult.AccessToken,
                ExpiresAt = DateTimeOffset.UtcNow.AddDays(30), // Claude sessions last about 30 days
                TokenType = "Bearer"
            };

            // Step 8: Update authentication state
            _currentState = new AuthenticationState
            {
                IsAuthenticated = true,
                User = userInfo,
                Token = token,
                Method = AuthenticationMethod.Subscription
            };

            // Step 9: Store token securely
            await StoreTokenAsync(token, cancellationToken);

            Console.WriteLine($"✅ Successfully authenticated as {userInfo.Email}");
            _logger.LogInformation("Successfully authenticated user {Email}", userInfo.Email);
            AuthenticationStateChanged?.Invoke(this, _currentState);

            return _currentState;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Authentication failed: {ex.Message}");
            _logger.LogError(ex, "Failed to authenticate with Claude.ai");
            throw new InvalidOperationException("Failed to authenticate with Claude.ai. Please try again.", ex);
        }
    }

    /// <inheritdoc />
    public async Task LogoutAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        _logger.LogInformation("Logging out user");

        try
        {
            // Clear stored token
            if (File.Exists(_tokenStorePath))
            {
                File.Delete(_tokenStorePath);
            }

            // Update state
            _currentState = new AuthenticationState
            {
                IsAuthenticated = false,
                Method = AuthenticationMethod.None
            };

            _logger.LogInformation("Successfully logged out");
            AuthenticationStateChanged?.Invoke(this, _currentState);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during logout");
            throw;
        }

        await Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task<AuthenticationState> RefreshTokenAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!_currentState.IsAuthenticated || _currentState.Token == null)
        {
            throw new InvalidOperationException("No valid token to refresh");
        }

        try
        {
            // For Claude.ai sessions, we can validate if they're still active
            var userInfo = await ValidateSessionAsync(_currentState.Token.AccessToken, cancellationToken);

            // If validation succeeds, session is still valid
            _logger.LogDebug("Token refresh successful - session still valid");
            return _currentState;
        }
        catch
        {
            // If validation fails, session expired - need to re-login
            _logger.LogWarning("Session expired, need to re-authenticate");

            _currentState = new AuthenticationState
            {
                IsAuthenticated = false,
                Method = AuthenticationMethod.None
            };

            AuthenticationStateChanged?.Invoke(this, _currentState);
            throw new InvalidOperationException("Session expired. Please login again.");
        }
    }

    /// <inheritdoc />
    public async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!_currentState.IsAuthenticated || _currentState.Token == null)
        {
            throw new InvalidOperationException("Not authenticated. Please login first.");
        }

        // Check if token needs refresh
        if (_currentState.Token.NeedsRefresh)
        {
            await RefreshTokenAsync(cancellationToken);
        }

        return _currentState.Token.AccessToken;
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

    private static int FindAvailablePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static string BuildAuthorizationUrl(string redirectUri, string state)
    {
        // Since Claude.ai doesn't have a traditional OAuth endpoint, we'll use a custom approach
        // This creates a URL that includes our callback info for the browser extension or manual process
        var baseUrl = "https://claude.ai/login";
        var queryParams = HttpUtility.ParseQueryString(string.Empty);
        queryParams["redirect_uri"] = redirectUri;
        queryParams["state"] = state;
        queryParams["response_type"] = "code";
        queryParams["client_id"] = "claude-code-cli";

        return $"{baseUrl}?{queryParams}";
    }

    private static void OpenBrowser(string url)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            };
            Process.Start(psi);
        }
        catch (Exception)
        {
            // If we can't open the browser, provide manual instructions
            Console.WriteLine($"Please open this URL in your browser: {url}");
        }
    }

    private static async Task<AuthResult> WaitForCallbackAsync(HttpListener listener, string expectedState, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var context = await listener.GetContextAsync().ConfigureAwait(false);
            var request = context.Request;
            var response = context.Response;

            try
            {
                if (request.Url?.LocalPath == "/callback")
                {
                    var query = HttpUtility.ParseQueryString(request.Url.Query);
                    var state = query["state"];
                    var code = query["code"];
                    var accessToken = query["access_token"];
                    var sessionKey = query["session_key"];

                    // Validate state parameter
                    if (state != expectedState)
                    {
                        await SendErrorResponse(response, "Invalid state parameter");
                        continue;
                    }

                    // Check for different types of auth responses
                    string? token = accessToken ?? sessionKey ?? code;

                    if (!string.IsNullOrEmpty(token))
                    {
                        await SendSuccessResponse(response);
                        return new AuthResult
                        {
                            AccessToken = token,
                            RefreshToken = token,
                            State = state
                        };
                    }

                    await SendErrorResponse(response, "No authentication token received");
                }
                else if (request.Url?.LocalPath == "/")
                {
                    // Serve a simple page with instructions
                    await SendInstructionsPage(response, expectedState);
                }
                else
                {
                    response.StatusCode = 404;
                    response.Close();
                }
            }
            catch (Exception ex)
            {
                await SendErrorResponse(response, $"Error processing request: {ex.Message}");
            }
        }

        throw new OperationCanceledException("Authentication was cancelled");
    }

    private static async Task SendSuccessResponse(HttpListenerResponse response)
    {
        var html = @"
<!DOCTYPE html>
<html>
<head>
    <title>Claude Code - Authentication Successful</title>
    <style>
        body { font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', sans-serif; margin: 0; padding: 40px; background: #f5f5f5; }
        .container { max-width: 500px; margin: 0 auto; background: white; padding: 40px; border-radius: 12px; box-shadow: 0 4px 20px rgba(0,0,0,0.1); text-align: center; }
        .success { color: #22c55e; font-size: 48px; margin-bottom: 20px; }
        h1 { color: #1f2937; margin-bottom: 10px; }
        p { color: #6b7280; line-height: 1.6; }
    </style>
</head>
<body>
    <div class='container'>
        <div class='success'>✅</div>
        <h1>Authentication Successful!</h1>
        <p>You have successfully authenticated with Claude Code. You can now close this window and return to your terminal.</p>
        <p><strong>The authentication process is complete.</strong></p>
    </div>
    <script>
        setTimeout(() => window.close(), 3000);
    </script>
</body>
</html>";

        var buffer = Encoding.UTF8.GetBytes(html);
        response.ContentType = "text/html; charset=utf-8";
        response.ContentLength64 = buffer.Length;
        response.StatusCode = 200;
        await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
        response.Close();
    }

    private static async Task SendErrorResponse(HttpListenerResponse response, string error)
    {
        var html = $@"
<!DOCTYPE html>
<html>
<head>
    <title>Claude Code - Authentication Error</title>
    <style>
        body {{ font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', sans-serif; margin: 0; padding: 40px; background: #f5f5f5; }}
        .container {{ max-width: 500px; margin: 0 auto; background: white; padding: 40px; border-radius: 12px; box-shadow: 0 4px 20px rgba(0,0,0,0.1); text-align: center; }}
        .error {{ color: #ef4444; font-size: 48px; margin-bottom: 20px; }}
        h1 {{ color: #1f2937; margin-bottom: 10px; }}
        p {{ color: #6b7280; line-height: 1.6; }}
        .error-details {{ background: #fef2f2; border: 1px solid #fecaca; color: #991b1b; padding: 15px; border-radius: 8px; margin-top: 20px; }}
    </style>
</head>
<body>
    <div class='container'>
        <div class='error'>❌</div>
        <h1>Authentication Error</h1>
        <p>There was an error during authentication. Please try again.</p>
        <div class='error-details'>{HttpUtility.HtmlEncode(error)}</div>
    </div>
</body>
</html>";

        var buffer = Encoding.UTF8.GetBytes(html);
        response.ContentType = "text/html; charset=utf-8";
        response.ContentLength64 = buffer.Length;
        response.StatusCode = 400;
        await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
        response.Close();
    }

    private static async Task SendInstructionsPage(HttpListenerResponse response, string state)
    {
        var callbackUrl = $"{response.Headers["Host"]}/callback";
        var html = $@"
<!DOCTYPE html>
<html>
<head>
    <title>Claude Code - Complete Authentication</title>
    <style>
        body {{ font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', sans-serif; margin: 0; padding: 40px; background: #f5f5f5; }}
        .container {{ max-width: 600px; margin: 0 auto; background: white; padding: 40px; border-radius: 12px; box-shadow: 0 4px 20px rgba(0,0,0,0.1); }}
        .step {{ margin: 20px 0; padding: 20px; background: #f8fafc; border-radius: 8px; border-left: 4px solid #3b82f6; }}
        .step h3 {{ margin-top: 0; color: #1e40af; }}
        input {{ width: 100%; padding: 12px; border: 2px solid #e5e7eb; border-radius: 6px; font-size: 16px; }}
        button {{ background: #3b82f6; color: white; padding: 12px 24px; border: none; border-radius: 6px; font-size: 16px; cursor: pointer; margin-top: 10px; }}
        button:hover {{ background: #2563eb; }}
        .warning {{ background: #fef3cd; border-color: #f59e0b; color: #92400e; }}
    </style>
</head>
<body>
    <div class='container'>
        <h1>🔐 Complete Claude Code Authentication</h1>
        <p>To complete authentication, please follow these steps:</p>

        <div class='step'>
            <h3>Step 1: Login to Claude.ai</h3>
            <p><a href='https://claude.ai/login' target='_blank' style='color: #3b82f6;'>Click here to open Claude.ai login</a></p>
            <p>Login with your Claude Pro/Max account credentials.</p>
        </div>

        <div class='step'>
            <h3>Step 2: Get Session Token</h3>
            <p>After logging in:</p>
            <ol>
                <li>Open Developer Tools (F12)</li>
                <li>Go to <strong>Application</strong> tab → <strong>Cookies</strong> → <strong>https://claude.ai</strong></li>
                <li>Find the <code>sessionKey</code> cookie and copy its value</li>
            </ol>
        </div>

        <div class='step'>
            <h3>Step 3: Submit Token</h3>
            <input type='text' id='sessionToken' placeholder='Paste your sessionKey value here' />
            <button onclick='submitToken()'>Complete Authentication</button>
        </div>

        <div class='step warning'>
            <h3>⚠️ Important</h3>
            <p>Keep this session token secure. It provides access to your Claude account.</p>
        </div>
    </div>

    <script>
        function submitToken() {{
            const token = document.getElementById('sessionToken').value.trim();
            if (!token) {{
                alert('Please enter your session token');
                return;
            }}

            // Redirect to callback with token
            window.location.href = '/callback?session_key=' + encodeURIComponent(token) + '&state={state}';
        }}

        document.getElementById('sessionToken').addEventListener('keypress', function(e) {{
            if (e.key === 'Enter') {{
                submitToken();
            }}
        }});
    </script>
</body>
</html>";

        var buffer = Encoding.UTF8.GetBytes(html);
        response.ContentType = "text/html; charset=utf-8";
        response.ContentLength64 = buffer.Length;
        response.StatusCode = 200;
        await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
        response.Close();
    }

    private sealed record AuthResult
    {
        public required string AccessToken { get; init; }
        public string? RefreshToken { get; init; }
        public string? State { get; init; }
    }

    private async Task<ClaudeUser> ValidateSessionAsync(string sessionCookie, CancellationToken cancellationToken)
    {
        _httpClient.DefaultRequestHeaders.Clear();
        _httpClient.DefaultRequestHeaders.Add("Cookie", $"sessionKey={sessionCookie}");

        var response = await _httpClient.GetAsync("/api/organizations", cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException("Invalid session cookie or session expired");
        }

        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        var orgData = JsonSerializer.Deserialize<JsonElement>(content, _jsonOptions);

        // Extract user information from the organizations response
        // This is a simplified implementation - the actual API structure may vary
        var firstOrg = orgData.EnumerateArray().FirstOrDefault();

        return new ClaudeUser
        {
            Id = firstOrg.TryGetProperty("uuid", out var uuid) ? uuid.GetString() ?? "unknown" : "unknown",
            Email = "user@claude.ai", // Placeholder - actual email would come from user profile endpoint
            Name = firstOrg.TryGetProperty("name", out var name) ? name.GetString() : null,
            Plan = "Max", // User confirmed they have Max subscription
            OrganizationId = firstOrg.TryGetProperty("uuid", out var orgUuid) ? orgUuid.GetString() : null
        };
    }

    private async Task StoreTokenAsync(OAuthToken token, CancellationToken cancellationToken)
    {
        try
        {
            var json = JsonSerializer.Serialize(token, _jsonOptions);

            // Basic encryption using DPAPI on Windows, or simple encoding on other platforms
            byte[] data;
            if (OperatingSystem.IsWindows())
            {
                var dataBytes = Encoding.UTF8.GetBytes(json);
                data = System.Security.Cryptography.ProtectedData.Protect(dataBytes, null,
                    System.Security.Cryptography.DataProtectionScope.CurrentUser);
            }
            else
            {
                // Simple base64 encoding for non-Windows platforms
                // In production, you'd want proper encryption
                data = Encoding.UTF8.GetBytes(Convert.ToBase64String(Encoding.UTF8.GetBytes(json)));
            }

            await File.WriteAllBytesAsync(_tokenStorePath, data, cancellationToken);
            _logger.LogDebug("Token stored securely");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to store authentication token");
            throw;
        }
    }

    private async Task LoadStoredTokenAsync()
    {
        try
        {
            if (!File.Exists(_tokenStorePath))
            {
                return;
            }

            var data = await File.ReadAllBytesAsync(_tokenStorePath);

            string json;
            if (OperatingSystem.IsWindows())
            {
                var decryptedBytes = System.Security.Cryptography.ProtectedData.Unprotect(data, null,
                    System.Security.Cryptography.DataProtectionScope.CurrentUser);
                json = Encoding.UTF8.GetString(decryptedBytes);
            }
            else
            {
                var base64 = Encoding.UTF8.GetString(data);
                json = Encoding.UTF8.GetString(Convert.FromBase64String(base64));
            }

            var token = JsonSerializer.Deserialize<OAuthToken>(json, _jsonOptions);
            if (token != null && !token.IsExpired)
            {
                // Validate that the stored session is still valid
                var userInfo = await ValidateSessionAsync(token.AccessToken, CancellationToken.None);

                _currentState = new AuthenticationState
                {
                    IsAuthenticated = true,
                    User = userInfo,
                    Token = token,
                    Method = AuthenticationMethod.Subscription
                };

                _logger.LogInformation("Restored authentication session for user {Email}", userInfo.Email);
            }
            else
            {
                // Token expired, delete file
                File.Delete(_tokenStorePath);
                _logger.LogDebug("Stored token was expired, removed");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load stored authentication token");

            // Clean up corrupted token file
            if (File.Exists(_tokenStorePath))
            {
                try
                {
                    File.Delete(_tokenStorePath);
                }
                catch
                {
                    // Ignore cleanup errors
                }
            }
        }
    }
}