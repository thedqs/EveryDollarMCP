using System.Net;
using System.Text;
using System.Text.Json;
using AskRamseyMCP.Models;
using Microsoft.Playwright;

/// <summary>
/// HTTP client for the Ask Ramsey chat AI API.
/// Supports two modes:
///   - Anonymous: Uses the public entry-point API (no login required, limited to one question per session)
///   - Authenticated: Uses SESSION cookie + CSRF token for full chat (history, follow-ups)
/// </summary>
public sealed class AskRamseyApiClient
{
    private const string BaseUrl = "https://www.ramseysolutions.com/askramsey/api";
    private const string EntryPointUrl = "https://www.ramseysolutions.com/forms/free-first-party/ask-ramsey-entry-web-app/api/chat";

    private readonly HttpClient _httpClient;
    private readonly CookieContainer _cookieContainer;
    private string? _csrfToken;
    private bool _isAuthenticated;
    private string? _chatSessionId;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public AskRamseyApiClient()
    {
        _cookieContainer = new CookieContainer();
        var handler = new HttpClientHandler
        {
            CookieContainer = _cookieContainer,
            AllowAutoRedirect = false,
            UseCookies = true
        };
        _httpClient = new HttpClient(handler);
        _httpClient.DefaultRequestHeaders.Add("User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/147.0.0.0 Safari/537.36");
    }

    public bool IsAuthenticated => _isAuthenticated;
    public string? ChatSessionId => _chatSessionId;

    /// <summary>
    /// Manually sets session cookies and CSRF token.
    /// </summary>
    public string SetSessionManually(string csrfToken, string sessionCookie)
    {
        if (string.IsNullOrWhiteSpace(csrfToken))
            return "Error: CSRF token cannot be empty.";
        if (string.IsNullOrWhiteSpace(sessionCookie))
            return "Error: Session cookie cannot be empty.";

        _csrfToken = csrfToken;
        var added = 0;

        foreach (var cookiePart in sessionCookie.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var eqIndex = cookiePart.IndexOf('=');
            if (eqIndex > 0)
            {
                var name = cookiePart[..eqIndex].Trim();
                var value = cookiePart[(eqIndex + 1)..].Trim();
                try
                {
                    _cookieContainer.Add(new Uri("https://www.ramseysolutions.com"), new System.Net.Cookie(name, value));
                    added++;
                }
                catch (CookieException)
                {
                    // Skip malformed cookie parts
                }
            }
        }

        if (added == 0)
            return "Error: No valid cookies found in the provided string. Expected format: 'SESSION=abc123'.";

        _isAuthenticated = true;
        return "Session configured manually. Try calling ask_ramsey to verify.";
    }

    /// <summary>
    /// Opens a browser window for the user to log in to Ramsey Solutions,
    /// then extracts session cookies and CSRF token automatically.
    /// </summary>
    public async Task<string> LoginViaBrowserAsync(string? preferredBrowser = null)
    {
        var playwright = await Playwright.CreateAsync();

        var (channel, browserName) = ResolveBrowser(preferredBrowser);

        var userDataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AskRamseyMCP", "browser-profile");
        Directory.CreateDirectory(userDataDir);

        IBrowserContext context;
        try
        {
            context = await playwright.Chromium.LaunchPersistentContextAsync(userDataDir, new BrowserTypeLaunchPersistentContextOptions
            {
                Headless = false,
                Channel = channel
            });
        }
        catch (PlaywrightException ex)
        {
            playwright.Dispose();
            return $"Error: Could not launch {browserName}. {ex.Message}";
        }

        var page = context.Pages.Count > 0 ? context.Pages[0] : await context.NewPageAsync();

        // Navigate to the login URL to start OAuth flow
        await page.GotoAsync("https://www.ramseysolutions.com/askramsey/login?redirectTo=%2F");

        // Wait for OAuth to complete and redirect back to /askramsey (but not /askramsey/login)
        try
        {
            await page.WaitForFunctionAsync("""
                () => window.location.pathname.startsWith('/askramsey') && !window.location.pathname.includes('/login')
            """, null, new PageWaitForFunctionOptions { Timeout = 120_000, PollingInterval = 500 });
        }
        catch (TimeoutException)
        {
            await context.CloseAsync();
            playwright.Dispose();
            return "Error: Login timed out after 2 minutes. Please try again.";
        }

        // Wait for the app to fully initialize after login
        await page.WaitForTimeoutAsync(2000);

        // Extract CSRF token from window._csrf (set by the app's JS after page load)
        string? csrf = null;
        try
        {
            csrf = await page.EvaluateAsync<string>("window._csrf");
        }
        catch { }

        if (string.IsNullOrEmpty(csrf))
        {
            // Retry after a longer wait — the app may still be loading
            await page.WaitForTimeoutAsync(3000);
            try
            {
                csrf = await page.EvaluateAsync<string>("window._csrf");
            }
            catch
            {
                await context.CloseAsync();
                playwright.Dispose();
                return "Error: Could not extract CSRF token (window._csrf) from the page. The app may not have fully loaded.";
            }
        }

        if (string.IsNullOrEmpty(csrf))
        {
            await context.CloseAsync();
            playwright.Dispose();
            return "Error: CSRF token was empty. Try again or use set_session_cookie manually.";
        }

        // Extract cookies — use full path since SESSION is scoped to /askramsey
        var cookies = await context.CookiesAsync(["https://www.ramseysolutions.com/askramsey"]);
        var sessionCookie = cookies.FirstOrDefault(c => c.Name == "SESSION");
        if (sessionCookie is null)
        {
            await context.CloseAsync();
            playwright.Dispose();
            return "Error: SESSION cookie not found. Login may not have completed.";
        }

        _cookieContainer.Add(new Uri("https://www.ramseysolutions.com"),
            new System.Net.Cookie("SESSION", sessionCookie.Value, sessionCookie.Path, sessionCookie.Domain.TrimStart('.')));

        _csrfToken = csrf;
        _isAuthenticated = true;

        // Try to extract existing chat session ID from the URL
        var currentUrl2 = page.Url;
        if (currentUrl2.Contains("/askramsey/chat/"))
        {
            var parts = currentUrl2.Split("/askramsey/chat/");
            if (parts.Length > 1)
            {
                var sessionId = parts[1].Split('?')[0].Split('#')[0];
                if (!string.IsNullOrEmpty(sessionId))
                    _chatSessionId = sessionId;
            }
        }

        await context.CloseAsync();
        playwright.Dispose();

        return $"Login successful via {browserName}! SESSION cookie and CSRF token captured.";
    }

    /// <summary>
    /// Sends a question to the Ask Ramsey AI using the anonymous entry-point API.
    /// No login required. Posts the question, then reads the SSE stream and fetches sources.
    /// </summary>
    public async Task<(string response, List<RagSource> sources, string chatSessionId)> AskAnonymousAsync(string question)
    {
        // 1. POST to the public entry-point — returns a chat session ID as plain text
        var entryRequest = new HttpRequestMessage(HttpMethod.Post, EntryPointUrl);
        var anonymousId = Guid.NewGuid().ToString();
        var entryBody = new EntryPointChatRequest(question, "HOMEPAGE", "rscom", "", anonymousId);
        entryRequest.Content = new StringContent(
            JsonSerializer.Serialize(entryBody, JsonOptions), Encoding.UTF8, "application/json");
        entryRequest.Headers.Add("Accept", "application/json, text/plain, */*");

        var entryResponse = await _httpClient.SendAsync(entryRequest);
        var entryBody2 = await entryResponse.Content.ReadAsStringAsync();
        if (!entryResponse.IsSuccessStatusCode)
            throw new HttpRequestException($"Anonymous chat API returned {(int)entryResponse.StatusCode}: {entryBody2[..Math.Min(500, entryBody2.Length)]}");
        var chatSessionId = entryBody2.Trim();
        _chatSessionId = chatSessionId;

        // 2. GET the chat session to find the message ID
        var messageId = await GetLastMessageIdAsync(chatSessionId);

        // 3. Read the SSE stream
        var fullText = await ReadStreamAsync(messageId);

        // 4. Fetch sources
        var sources = await GetSourcesAsync(messageId);

        return (fullText, sources, chatSessionId);
    }

    /// <summary>
    /// Sends a question to the Ask Ramsey chat AI using authenticated session.
    /// Requires login first. Supports conversation continuation and chat history.
    /// </summary>
    public async Task<(string response, List<RagSource> sources)> AskAuthenticatedAsync(string question, string? chatSessionId = null)
    {
        EnsureAuthenticated();

        var existingSessionId = chatSessionId ?? _chatSessionId;
        string messageId;

        if (existingSessionId is not null)
        {
            // Follow-up in an existing conversation: POST /chat/message
            var requestBody = new ChatMessageRequest(existingSessionId, question);
            var json = JsonSerializer.Serialize(requestBody, JsonOptions);

            var request = CreateRequest(HttpMethod.Post, $"{BaseUrl}/chat/message");
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.SendAsync(request);
            var responseBody = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException($"Chat message API returned {(int)response.StatusCode}: {responseBody[..Math.Min(500, responseBody.Length)]}");

            var chatResponse = JsonSerializer.Deserialize<ChatResponse>(responseBody, JsonOptions);
            messageId = chatResponse?.Messages?.LastOrDefault()?.Id
                ?? throw new InvalidOperationException("No message ID returned from chat API.");
        }
        else
        {
            // New conversation: POST /chat → returns session ID as plain text
            var requestBody = new NewChatRequest(question);
            var json = JsonSerializer.Serialize(requestBody, JsonOptions);

            var request = CreateRequest(HttpMethod.Post, $"{BaseUrl}/chat");
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.SendAsync(request);
            var responseBody = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException($"Chat API returned {(int)response.StatusCode}: {responseBody[..Math.Min(500, responseBody.Length)]}");

            var newSessionId = responseBody.Trim();
            _chatSessionId = newSessionId;

            // GET the chat session to find the message ID
            messageId = await GetLastMessageIdAsync(newSessionId);
        }

        // Read the SSE stream to get the full response
        var fullText = await ReadStreamAsync(messageId);

        // Fetch sources
        var sources = await GetSourcesAsync(messageId);

        return (fullText, sources);
    }

    /// <summary>
    /// Reads the SSE event stream for a message and returns the assembled text.
    /// This endpoint does not require authentication.
    /// </summary>
    private async Task<string> ReadStreamAsync(string messageId)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}/chat/message/{Uri.EscapeDataString(messageId)}/stream");
        request.Headers.Add("Accept", "text/event-stream");
        request.Headers.Add("Cache-Control", "no-cache");

        var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Stream API returned {(int)response.StatusCode} for message {messageId}.");

        var sb = new StringBuilder();

        using var stream = await response.Content.ReadAsStreamAsync();
        using var reader = new StreamReader(stream);

        string? eventType = null;

        while (await reader.ReadLineAsync() is { } line)
        {
            if (line.StartsWith("event:"))
            {
                eventType = line["event:".Length..].Trim();
                continue;
            }

            if (line.StartsWith("data:"))
            {
                var data = line["data:".Length..];

                switch (eventType)
                {
                    case "thinking":
                        // Server sends thinking progress events; skip them
                        break;

                    case "token":
                        try
                        {
                            var token = JsonSerializer.Deserialize<TokenEvent>(data, JsonOptions);
                            if (token is not null)
                                sb.Append(token.Text);
                        }
                        catch { }
                        break;

                    case "done":
                        // Stream complete
                        break;
                }

                eventType = null;
                continue;
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Fetches the sources (citations) for a given message.
    /// This endpoint does not require authentication.
    /// </summary>
    public async Task<List<RagSource>> GetSourcesAsync(string messageId)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}/chat/message/{Uri.EscapeDataString(messageId)}/sources");
        request.Headers.Add("Accept", "application/json, text/plain, */*");
        var response = await _httpClient.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
            return [];

        try
        {
            var sources = JsonSerializer.Deserialize<SourcesResponse>(body, JsonOptions);
            return sources?.RagSources ?? [];
        }
        catch
        {
            return [];
        }
    }

    /// <summary>
    /// Fetches the user's chat history. Requires authentication.
    /// </summary>
    public async Task<string> GetChatHistoryRawAsync()
    {
        EnsureAuthenticated();

        var request = CreateRequest(HttpMethod.Get, $"{BaseUrl}/chat/history");
        var response = await _httpClient.SendAsync(request);

        // 303 means the server doesn't recognize our session — treat as auth failure
        if (response.StatusCode == HttpStatusCode.RedirectMethod ||
            response.StatusCode == HttpStatusCode.Redirect ||
            response.StatusCode == HttpStatusCode.MovedPermanently)
        {
            throw new HttpRequestException(
                "Session is not valid — the server redirected to login. Try logging in again.");
        }

        var body = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Chat history API returned {(int)response.StatusCode}: {body[..Math.Min(500, body.Length)]}");

        return body;
    }

    /// <summary>
    /// Deletes a chat session from history. Requires authentication.
    /// </summary>
    public async Task DeleteChatAsync(string chatSessionId)
    {
        EnsureAuthenticated();

        var request = CreateRequest(HttpMethod.Delete, $"{BaseUrl}/chat/{Uri.EscapeDataString(chatSessionId)}");
        var response = await _httpClient.SendAsync(request);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            throw new HttpRequestException($"Delete chat API returned {(int)response.StatusCode}: {body[..Math.Min(500, body.Length)]}");
        }

        // Clear current session if it was the one deleted
        if (_chatSessionId == chatSessionId)
            _chatSessionId = null;
    }

    /// <summary>
    /// Starts a new chat session (just generates a new session ID).
    /// </summary>
    public string StartNewSession()
    {
        _chatSessionId = null;
        return "Session cleared. Next question will start a new conversation.";
    }

    private static (string? channel, string name) ResolveBrowser(string? preference)
    {
        if (!string.IsNullOrEmpty(preference))
        {
            var pref = preference.Trim().ToLowerInvariant();
            return pref switch
            {
                "edge" or "msedge" => ("msedge", "Microsoft Edge"),
                "chrome" => ("chrome", "Google Chrome"),
                _ => (pref, pref)
            };
        }

        var candidates = new (string exePath, string channel, string name)[]
        {
            (Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), @"Microsoft\Edge\Application\msedge.exe"), "msedge", "Microsoft Edge"),
            (Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"Microsoft\Edge\Application\msedge.exe"), "msedge", "Microsoft Edge"),
            (Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), @"Google\Chrome\Application\chrome.exe"), "chrome", "Google Chrome"),
            (Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"Google\Chrome\Application\chrome.exe"), "chrome", "Google Chrome"),
        };

        foreach (var (exePath, channel, name) in candidates)
        {
            if (File.Exists(exePath))
                return (channel, name);
        }

        return ("msedge", "Microsoft Edge (default)");
    }

    private void EnsureAuthenticated()
    {
        if (!_isAuthenticated)
            throw new InvalidOperationException("Not authenticated. Call login first.");
    }

    /// <summary>
    /// GETs a chat session and returns the last message ID.
    /// Used by both anonymous and authenticated flows after creating a conversation.
    /// </summary>
    private async Task<string> GetLastMessageIdAsync(string chatSessionId)
    {
        var request = CreateRequest(HttpMethod.Get, $"{BaseUrl}/chat/{Uri.EscapeDataString(chatSessionId)}");
        var response = await _httpClient.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Chat session API returned {(int)response.StatusCode}: {body[..Math.Min(500, body.Length)]}");

        var chatResponse = JsonSerializer.Deserialize<ChatResponse>(body, JsonOptions);
        return chatResponse?.Messages?.LastOrDefault()?.Id
            ?? throw new InvalidOperationException($"No message ID found in chat session {chatSessionId}.");
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string url)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Add("Accept", "application/json, text/plain, */*");
        request.Headers.Add("Origin", "https://www.ramseysolutions.com");
        request.Headers.Add("Referer", "https://www.ramseysolutions.com/askramsey");
        if (_csrfToken is not null)
            request.Headers.Add("x-csrf-token", _csrfToken);
        return request;
    }
}
