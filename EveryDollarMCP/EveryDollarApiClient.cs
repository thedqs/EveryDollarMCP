using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using EveryDollarMCP;
using EveryDollarMCP.Models;
using Microsoft.Playwright;

/// <summary>
/// HTTP client for the EveryDollar API. Uses cookie-based session auth
/// obtained via OAuth2 login through Ramsey Solutions identity provider.
/// Credentials are persisted locally with DPAPI encryption and restored on startup.
/// </summary>
public sealed class EveryDollarApiClient
{
    private const string BaseUrl = "https://www.everydollar.com/app/api";

    private readonly HttpClient _httpClient;
    private readonly CookieContainer _cookieContainer;
    private string? _csrfToken;
    private bool _isAuthenticated;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly JsonSerializerOptions JsonOptionsIgnoreNull = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public EveryDollarApiClient()
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

        // Attempt to restore cached credentials from encrypted local store
        TryRestoreCachedCredentials();
    }

    public bool IsAuthenticated => _isAuthenticated;

    /// <summary>
    /// Attempts to load previously saved credentials from the encrypted local store.
    /// </summary>
    private void TryRestoreCachedCredentials()
    {
        var cached = CredentialStore.Load();
        if (cached is null)
            return;

        _csrfToken = cached.CsrfToken;
        foreach (var cookiePart in cached.SessionCookie.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var eqIndex = cookiePart.IndexOf('=');
            if (eqIndex > 0)
            {
                var name = cookiePart[..eqIndex].Trim();
                var value = cookiePart[(eqIndex + 1)..].Trim();
                _cookieContainer.Add(new Uri("https://www.everydollar.com"), new System.Net.Cookie(name, value));
            }
        }
        _isAuthenticated = true;
    }

    /// <summary>
    /// Saves current credentials to the encrypted local store.
    /// </summary>
    private void PersistCredentials(string csrfToken, string sessionCookie, DateTime? expiresAtUtc = null)
    {
        // Default to 7-day expiry if no explicit expiration is provided
        var expiry = expiresAtUtc ?? DateTime.UtcNow.AddDays(7);
        CredentialStore.Save(csrfToken, sessionCookie, expiry);
    }

    /// <summary>
    /// Invalidates cached credentials when the server rejects them (401/403).
    /// </summary>
    private void InvalidateCachedCredentials()
    {
        _isAuthenticated = false;
        _csrfToken = null;
        CredentialStore.Clear();
    }

    /// <summary>
    /// Manually sets session cookies and CSRF token. Use when the browser console
    /// approach can't capture HttpOnly cookies.
    /// </summary>
    public string SetSessionManually(string csrfToken, string sessionCookie)
    {
        _csrfToken = csrfToken;

        // Parse cookie string (could be "name=value" or "name1=val1; name2=val2")
        foreach (var cookiePart in sessionCookie.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var eqIndex = cookiePart.IndexOf('=');
            if (eqIndex > 0)
            {
                var name = cookiePart[..eqIndex].Trim();
                var value = cookiePart[(eqIndex + 1)..].Trim();
                _cookieContainer.Add(new Uri("https://www.everydollar.com"), new System.Net.Cookie(name, value));
            }
        }

        _isAuthenticated = true;
        PersistCredentials(csrfToken, sessionCookie);
        return "Session configured manually and saved to encrypted local store. Try calling get_budget to verify.";
    }

    /// <summary>
    /// Opens a browser window for the user to log in, then extracts SESSION cookie and CSRF token automatically.
    /// </summary>
    public async Task<string> LoginViaBrowserAsync(string? preferredBrowser = null)
    {
        var playwright = await Playwright.CreateAsync();

        // Resolve which browser to use
        var (channel, browserName) = ResolveBrowser(preferredBrowser);

        // Use a persistent profile so passwords/cookies survive across sessions
        var userDataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "EveryDollarMCP", "browser-profile");
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

        await page.GotoAsync("https://www.everydollar.com/app/budget");

        // Wait for the user to complete login — we know we're in when the URL becomes the budget page.
        try
        {
            await page.WaitForURLAsync("**/app/budget**", new PageWaitForURLOptions { Timeout = 120_000 });
        }
        catch (TimeoutException)
        {
            await context.CloseAsync();
            playwright.Dispose();
            return "Error: Login timed out after 2 minutes. Please try again.";
        }

        // Small delay to let the app fully initialize (CSRF gets set after page load JS runs)
        await page.WaitForTimeoutAsync(2000);

        // Extract CSRF token from window._CSRF
        string? csrf = null;
        try
        {
            csrf = await page.EvaluateAsync<string>("window._CSRF");
        }
        catch
        {
            // CSRF not found yet, will retry
        }

        if (string.IsNullOrEmpty(csrf))
        {
            // Retry after a longer wait — the app may still be loading
            await page.WaitForTimeoutAsync(3000);
            try
            {
                csrf = await page.EvaluateAsync<string>("window._CSRF");
            }
            catch
            {
                await context.CloseAsync();
                playwright.Dispose();
                return "Error: Could not extract CSRF token from the page. The app may not have fully loaded.";
            }
        }

        if (string.IsNullOrEmpty(csrf))
        {
            await context.CloseAsync();
            playwright.Dispose();
            return "Error: CSRF token was empty. Try again or use SetSessionCookie manually.";
        }

        // Extract cookies — use full path since SESSION is scoped to /app
        var cookies = await context.CookiesAsync(["https://www.everydollar.com/app/budget"]);
        var sessionCookie = cookies.FirstOrDefault(c => c.Name == "SESSION");
        if (sessionCookie is null)
        {
            await context.CloseAsync();
            playwright.Dispose();
            return "Error: SESSION cookie not found. Login may not have completed.";
        }

        // Apply to our HTTP client
        _csrfToken = csrf;
        _cookieContainer.Add(new Uri("https://www.everydollar.com"), new System.Net.Cookie("SESSION", sessionCookie.Value));
        _isAuthenticated = true;

        // Persist credentials to encrypted local store
        var expiry = sessionCookie.Expires > 0
            ? DateTimeOffset.FromUnixTimeSeconds((long)sessionCookie.Expires).UtcDateTime
            : (DateTime?)null;
        PersistCredentials(csrf, $"SESSION={sessionCookie.Value}", expiry);

        await context.CloseAsync();
        playwright.Dispose();

        return $"Login successful via {browserName}! SESSION cookie and CSRF token captured and saved to encrypted local store.";
    }

    /// <summary>
    /// Resolves the Playwright channel to use. If a preference is given, uses that.
    /// Otherwise auto-detects installed browsers in priority order: Edge > Chrome.
    /// </summary>
    private static (string? channel, string name) ResolveBrowser(string? preference)
    {
        if (!string.IsNullOrEmpty(preference))
        {
            var pref = preference.Trim().ToLowerInvariant();
            return pref switch
            {
                "edge" or "msedge" => ("msedge", "Microsoft Edge"),
                "chrome" => ("chrome", "Google Chrome"),
                _ => (pref, pref) // pass through as raw channel name
            };
        }

        // Auto-detect: check for installed browsers
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

        // Fallback: try msedge and let Playwright resolve it
        return ("msedge", "Microsoft Edge (default)");
    }

    private void EnsureAuthenticated()
    {
        if (!_isAuthenticated)
            throw new InvalidOperationException("Not authenticated. Call login first.");
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string url)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Add("Accept", "application/json, text/plain, */*");
        if (_csrfToken is not null)
            request.Headers.Add("x-csrf-token", _csrfToken);
        return request;
    }

    /// <summary>
    /// Sends a request and returns the response body as a string.
    /// Throws HttpRequestException with the API error body on failure.
    /// Invalidates cached credentials on 401/403 responses.
    /// </summary>
    private async Task<string> SendAndReadAsync(HttpRequestMessage request)
    {
        var response = await _httpClient.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                InvalidateCachedCredentials();
            }
            throw new HttpRequestException(
                $"API returned {(int)response.StatusCode} {response.StatusCode}: {body[..Math.Min(500, body.Length)]}");
        }
        return body;
    }

    /// <summary>
    /// Sends a request and deserializes the response body as T.
    /// Throws HttpRequestException with the API error body on failure.
    /// </summary>
    private async Task<T?> SendAndDeserializeAsync<T>(HttpRequestMessage request)
    {
        var body = await SendAndReadAsync(request);
        return JsonSerializer.Deserialize<T>(body, JsonOptions);
    }

    /// <summary>
    /// Sends a request and throws HttpRequestException with the API error body on failure.
    /// Does not read or return the response body on success.
    /// Invalidates cached credentials on 401/403 responses.
    /// </summary>
    private async Task SendAsync(HttpRequestMessage request)
    {
        var response = await _httpClient.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                InvalidateCachedCredentials();
            }
            var body = await response.Content.ReadAsStringAsync();
            throw new HttpRequestException(
                $"API returned {(int)response.StatusCode} {response.StatusCode}: {body[..Math.Min(500, body.Length)]}");
        }
    }

    // --- Read Operations ---

    public async Task<Budget?> GetBudgetByDateAsync(string date)
    {
        EnsureAuthenticated();
        var request = CreateRequest(HttpMethod.Get, $"{BaseUrl}/budgets/search/getBudgetByDate?date={date}");
        var raw = await SendAndReadAsync(request);
        try
        {
            return JsonSerializer.Deserialize<Budget>(raw, JsonOptions);
        }
        catch (JsonException ex)
        {
            var pos = (int)ex.BytePositionInLine.GetValueOrDefault();
            var start = Math.Max(0, pos - 50);
            var end = Math.Min(raw.Length, pos + 50);
            throw new JsonException($"{ex.Message}\nJSON near position {pos}: ...{raw[start..end]}...", ex.Path, ex.LineNumber, ex.BytePositionInLine, ex);
        }
    }

    public async Task<BudgetExistence?> GetBudgetsAsync()
    {
        EnsureAuthenticated();
        var request = CreateRequest(HttpMethod.Get, $"{BaseUrl}/budgets");
        return await SendAndDeserializeAsync<BudgetExistence>(request);
    }

    public async Task<Budget?> GetBudgetByIdAsync(string budgetId)
    {
        EnsureAuthenticated();
        var request = CreateRequest(HttpMethod.Get, $"{BaseUrl}/budgets/{budgetId}");
        return await SendAndDeserializeAsync<Budget>(request);
    }

    public async Task<TransactionSearchResult?> GetTransactionsAsync(string? startDate = null, string? endDate = null)
    {
        EnsureAuthenticated();
        var url = $"{BaseUrl}/transactions/search/findByDateRange";
        var queryParams = new List<string>();
        if (startDate is not null) queryParams.Add($"startDate={startDate}");
        if (endDate is not null) queryParams.Add($"endDate={endDate}");
        if (queryParams.Count > 0) url += "?" + string.Join("&", queryParams);

        var request = CreateRequest(HttpMethod.Get, url);
        return await SendAndDeserializeAsync<TransactionSearchResult>(request);
    }

    public async Task<List<BankAccount>> GetAccountsAsync()
    {
        EnsureAuthenticated();
        var request = CreateRequest(HttpMethod.Get, $"{BaseUrl}/accounts");
        return await SendAndDeserializeAsync<List<BankAccount>>(request) ?? [];
    }

    public async Task<List<Subscription>> GetSubscriptionsAsync()
    {
        EnsureAuthenticated();
        var request = CreateRequest(HttpMethod.Get, $"{BaseUrl}/subscriptions");
        return await SendAndDeserializeAsync<List<Subscription>>(request) ?? [];
    }

    public async Task<UserPreferences?> GetUserPreferencesAsync()
    {
        EnsureAuthenticated();
        var request = CreateRequest(HttpMethod.Get, $"{BaseUrl}/user/preferences");
        return await SendAndDeserializeAsync<UserPreferences>(request);
    }

    public async Task<string> GetInsightsAsync(string startDate, string endDate)
    {
        EnsureAuthenticated();
        var request = CreateRequest(HttpMethod.Get,
            $"{BaseUrl}/insights/findBudgetInsightsByDateRange?startDate={startDate}&endDate={endDate}");
        return await SendAndReadAsync(request);
    }

    public async Task<string> GetBudgetItemHistoryAsync(string budgetId, string itemId, string? budgetIds = null)
    {
        EnsureAuthenticated();
        var url = $"{BaseUrl}/budgets/{budgetId}/items/{itemId}/history";
        if (budgetIds is not null) url += $"?budgetIds={budgetIds}";
        var request = CreateRequest(HttpMethod.Get, url);
        return await SendAndReadAsync(request);
    }

    // --- Write Operations ---

    public async Task<string> CreateBudgetItemAsync(string budgetId, CreateBudgetItemRequest item)
    {
        EnsureAuthenticated();
        var request = CreateRequest(HttpMethod.Post, $"{BaseUrl}/budgets/{budgetId}/items");
        request.Content = JsonContent.Create(item, options: JsonOptions);
        return await SendAndReadAsync(request);
    }

    public async Task<string> UpdateBudgetItemAsync(string budgetId, string itemId, UpdateBudgetItemRequest update)
    {
        EnsureAuthenticated();
        var request = CreateRequest(new HttpMethod("PATCH"), $"{BaseUrl}/budgets/{budgetId}/items/{itemId}");
        request.Content = JsonContent.Create(update, options: JsonOptionsIgnoreNull);
        return await SendAndReadAsync(request);
    }

    public async Task<string> UpdateBufferAsync(string budgetId, long bufferAmountCents)
    {
        EnsureAuthenticated();
        var request = CreateRequest(new HttpMethod("PATCH"), $"{BaseUrl}/budgets/{budgetId}");
        request.Content = JsonContent.Create(new { bufferAmountCents }, options: JsonOptions);
        return await SendAndReadAsync(request);
    }

    public async Task DeleteBudgetItemAsync(string budgetId, string itemId)
    {
        EnsureAuthenticated();
        var request = CreateRequest(HttpMethod.Delete, $"{BaseUrl}/budgets/{budgetId}/items/{itemId}");
        await SendAsync(request);
    }

    public async Task<string> ReorderBudgetItemsAsync(string budgetId, List<string> itemIds)
    {
        EnsureAuthenticated();
        var request = CreateRequest(HttpMethod.Put, $"{BaseUrl}/budgets/{budgetId}/items/reorder");
        request.Content = JsonContent.Create(itemIds, options: JsonOptions);
        return await SendAndReadAsync(request);
    }

    public async Task<string> CreateTransactionAsync(CreateTransactionRequest transaction)
    {
        EnsureAuthenticated();
        var request = CreateRequest(HttpMethod.Post, $"{BaseUrl}/transactions");
        request.Content = JsonContent.Create(transaction, options: JsonOptions);
        return await SendAndReadAsync(request);
    }

    public async Task<string> UpdateTransactionAsync(string transactionId, UpdateTransactionRequest update)
    {
        EnsureAuthenticated();
        var request = CreateRequest(HttpMethod.Put, $"{BaseUrl}/transactions/{transactionId}");
        request.Content = JsonContent.Create(update, options: JsonOptions);
        return await SendAndReadAsync(request);
    }

    public async Task DeleteTransactionsAsync(List<string> transactionIds)
    {
        EnsureAuthenticated();
        var request = CreateRequest(HttpMethod.Delete, $"{BaseUrl}/transactions");
        request.Content = JsonContent.Create(transactionIds, options: JsonOptions);
        await SendAsync(request);
    }

    public async Task RefreshBankAccountsAsync()
    {
        EnsureAuthenticated();
        var request = CreateRequest(HttpMethod.Post, $"{BaseUrl}/bank-accounts/refresh");
        await SendAsync(request);
    }

    // --- New Read Operations ---

    public async Task<Transaction?> GetTransactionAsync(string transactionId)
    {
        EnsureAuthenticated();
        var request = CreateRequest(HttpMethod.Get, $"{BaseUrl}/transactions/{transactionId}");
        return await SendAndDeserializeAsync<Transaction>(request);
    }

    public async Task<string> GetDebtSnowballAsync()
    {
        EnsureAuthenticated();
        var request = CreateRequest(HttpMethod.Get, $"{BaseUrl}/debt-snowball/snowball");
        return await SendAndReadAsync(request);
    }

    public async Task<string> GetGoalsAsync()
    {
        EnsureAuthenticated();
        var request = CreateRequest(HttpMethod.Get, $"{BaseUrl}/goals/most-recent");
        return await SendAndReadAsync(request);
    }

    // --- New Write Operations ---

    public async Task<string> CloneBudgetAsync(string sourceBudgetId, string targetDate)
    {
        EnsureAuthenticated();
        var request = CreateRequest(HttpMethod.Post, $"{BaseUrl}/budgets/{sourceBudgetId}/clone");
        request.Content = JsonContent.Create(new { date = targetDate }, options: JsonOptions);
        return await SendAndReadAsync(request);
    }

    public async Task<string> ResetBudgetAsync(string budgetId)
    {
        EnsureAuthenticated();
        var request = CreateRequest(HttpMethod.Post, $"{BaseUrl}/budgets/{budgetId}/reset");
        request.Content = JsonContent.Create<object?>(null, options: JsonOptions);
        return await SendAndReadAsync(request);
    }

    public async Task<string> ConvertToFundAsync(string budgetId, string itemId, long targetAmount = 0, long originalStartingBalance = 0)
    {
        EnsureAuthenticated();
        var request = CreateRequest(HttpMethod.Post, $"{BaseUrl}/budgets/{budgetId}/items/{itemId}/convertToSinkingFund");
        request.Content = JsonContent.Create(new { targetAmount, originalStartingBalance }, options: JsonOptions);
        return await SendAndReadAsync(request);
    }

    public async Task<string> RestoreTransactionsAsync(List<string> transactionIds)
    {
        EnsureAuthenticated();
        var request = CreateRequest(HttpMethod.Put, $"{BaseUrl}/transactions/restore");
        request.Content = JsonContent.Create(transactionIds, options: JsonOptions);
        return await SendAndReadAsync(request);
    }

    public async Task<string> UpdateUserPreferencesAsync(Dictionary<string, object> preferences)
    {
        EnsureAuthenticated();
        var request = CreateRequest(new HttpMethod("PATCH"), $"{BaseUrl}/user/preferences");
        request.Content = JsonContent.Create(preferences, options: JsonOptions);
        return await SendAndReadAsync(request);
    }

    // --- Group Management ---

    public async Task<string> CreateBudgetGroupAsync(string budgetId, string type, string label)
    {
        EnsureAuthenticated();
        var request = CreateRequest(HttpMethod.Post, $"{BaseUrl}/budgets/{budgetId}/groups");
        request.Content = JsonContent.Create(new { type, label }, options: JsonOptions);
        return await SendAndReadAsync(request);
    }

    public async Task DeleteBudgetGroupAsync(string budgetId, string groupId)
    {
        EnsureAuthenticated();
        var request = CreateRequest(HttpMethod.Delete, $"{BaseUrl}/budgets/{budgetId}/groups/{groupId}");
        await SendAsync(request);
    }

    public async Task<string> ReorderBudgetGroupsAsync(string budgetId, List<string> groupIds)
    {
        EnsureAuthenticated();
        var request = CreateRequest(HttpMethod.Post, $"{BaseUrl}/budgets/{budgetId}/groups/reorder");
        request.Content = JsonContent.Create(groupIds, options: JsonOptions);
        return await SendAndReadAsync(request);
    }
}
