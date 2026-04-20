using System.ComponentModel;
using System.Text;
using AskRamseyMCP.Models;
using ModelContextProtocol.Server;

[McpServerToolType]
public static class AuthTools
{
    [McpServerTool, Description("""
Log in to Ramsey Solutions by opening a browser window. The tool captures the session automatically once login completes. Times out after 2 minutes. Auto-detects Edge or Chrome; specify browser to override.

LOGIN IS OPTIONAL but beneficial. There are two modes:

ANONYMOUS MODE (no login):
- Ask any general Ramsey-principles question immediately
- Each question starts a fresh session (no conversation history)
- The Ramsey AI does not know anything about the user's financial situation
- Good for general questions about Baby Steps, budgeting concepts, debt payoff strategies

AUTHENTICATED MODE (after login):
- Supports multi-turn conversation within a session (follow-up questions)
- Chat history is preserved across sessions
- The Ramsey AI does NOT have direct access to the user's EveryDollar budget or financial data — include relevant numbers in your question

Recommend login when: the user wants follow-up questions or conversation history. Skip login when: the user just wants a quick general answer about Ramsey principles.
""")]
    public static async Task<string> Login(
        AskRamseyApiClient client,
        [Description("Optional: 'edge' or 'chrome'. Auto-detects if omitted.")] string? browser = null)
    {
        try
        {
            return await client.LoginViaBrowserAsync(browser);
        }
        catch (Exception ex)
        {
            return $"Error: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}";
        }
    }

    [McpServerTool, Description("Manually set authentication credentials instead of using browser login. Open the Ask Ramsey chat in your browser, then: 1) Get the x-csrf-token header value from DevTools > Network tab (look at any API request), 2) Get cookies from DevTools > Application > Cookies. This enables authenticated mode (see Login tool description for benefits).")]
    public static string SetSessionCookie(
        AskRamseyApiClient client,
        [Description("CSRF token from the x-csrf-token request header in DevTools Network tab")] string csrfToken,
        [Description("Cookie string from DevTools, e.g. 'SESSION=abc123' or full cookie header value")] string cookies)
    {
        try
        {
            return client.SetSessionManually(csrfToken, cookies);
        }
        catch (Exception ex)
        {
            return $"Error: {ex.GetType().Name}: {ex.Message}";
        }
    }
}

[McpServerToolType]
public static class ChatTools
{
    [McpServerTool, Description("""
Ask the Ramsey Solutions AI a question about personal finance, budgeting, debt payoff, Baby Steps, investing, or any topic covered by Ramsey principles. Returns the AI's response along with cited sources.

MODES:
- Without login: Works immediately in anonymous mode. Each call is a standalone question (no conversation continuity). Good for general Ramsey-principles questions.
- With login: Supports conversation continuation within a session, and chat history is preserved. Note: the Ramsey AI does NOT have direct access to the user's EveryDollar budget — include relevant financial details in your question.

HOW TO STRUCTURE QUESTIONS FOR BEST RESULTS:
The Ramsey AI responds best to specific, factual, budget-focused questions. Follow these guidelines:

ALWAYS PROVIDE:
- The relevant facts (dollar amounts, income, savings, debt)
- The Baby Step the user believes they are on
- The proposed action or concern
- One clear, specific question

DO:
- Use exact dollar amounts when possible
- Separate liquid savings from retirement accounts
- Include all non-mortgage debt
- Include known irregular expenses (tuition, insurance renewals, holidays)
- State the Baby Step the user thinks they are on

DO NOT:
- Send vague requests like "What do you think about my finances?"
- Assume the claimed Baby Step is correct — let the AI verify
- Combine emergency fund money with sinking fund totals
- Ask broad philosophical questions when a concrete budget decision is needed

TEMPLATE FOR BEST RESULTS:
For general questions, include:
  Goal | Monthly take-home income | Current liquid savings | Non-mortgage debt total |
  Mortgage/rent | Monthly essential expenses | Upcoming irregular expenses |
  Current sinking funds | Claimed Baby Step | Specific question

For quick checks, include:
  Facts | Claimed Baby Step | Proposed action | Ramsey rule to verify | Exact question

EXAMPLE (good): "We have $14,000 in savings, no non-mortgage debt, $2,100 mortgage, $10,000 monthly income, $6,800 monthly expenses. We think we're on Baby Step 3. Can we start a $300/month vacation sinking fund, or does that delay the emergency fund?"

EXAMPLE (bad): "Should we save for a vacation?"

CHECKS TO CONSIDER BEFORE ASKING:
- Does this plan follow the Baby Step order?
- Is there any non-mortgage debt still present?
- Is the emergency fund at the correct level for the current step?
- Is this a real sinking fund need or should it wait?
- Does this fit inside a zero-based budget?
- Are known upcoming expenses already being planned for?
""")]
    public static async Task<string> AskRamsey(
        AskRamseyApiClient client,
        [Description("Your question for the Ask Ramsey AI. For best results, include specific dollar amounts, the user's claimed Baby Step, and a clear question. See tool description for templates.")] string question,
        [Description("Optional: chat session ID to continue an existing conversation. Only works in authenticated mode (after login). Omit to start a new conversation.")] string? chatSessionId = null)
    {
        try
        {
            string response;
            List<RagSource> sources;
            string mode;

            if (client.IsAuthenticated)
            {
                (response, sources) = await client.AskAuthenticatedAsync(question, chatSessionId);
                mode = "authenticated";
            }
            else
            {
                string sessionId;
                (response, sources, sessionId) = await client.AskAnonymousAsync(question);
                mode = "anonymous";
            }

            var sb = new StringBuilder();
            sb.AppendLine("**Ask Ramsey Response:**");
            sb.AppendLine();
            sb.AppendLine(response);

            if (sources.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("---");
                sb.AppendLine("**Sources:**");
                foreach (var source in sources)
                {
                    sb.AppendLine($"- [{source.Title}]({source.Url}) ({source.Type})");
                }
            }

            sb.AppendLine();
            sb.AppendLine($"_Session: {client.ChatSessionId} | Mode: {mode}_");

            return sb.ToString();
        }
        catch (Exception ex)
        {
            return $"Error: {ex.GetType().Name}: {ex.Message}";
        }
    }

    [McpServerTool, Description("Retrieve the user's Ask Ramsey chat history. Requires authentication (login first). Returns previous chat sessions with questions, responses, and sources. Use this to review past conversations or find a session ID to continue.")]
    public static async Task<string> GetChatHistory(AskRamseyApiClient client)
    {
        try
        {
            var historyJson = await client.GetChatHistoryRawAsync();

            // Pretty-print the JSON for readability
            try
            {
                var parsed = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(historyJson);
                return System.Text.Json.JsonSerializer.Serialize(parsed, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            }
            catch
            {
                return historyJson;
            }
        }
        catch (Exception ex)
        {
            return $"Error: {ex.GetType().Name}: {ex.Message}";
        }
    }

    [McpServerTool, Description("Start a new Ask Ramsey chat session. In authenticated mode, this creates a fresh conversation so follow-up questions won't reference prior context. In anonymous mode, each AskRamsey call already starts a new session automatically, so this is not needed.")]
    public static string StartNewSession(AskRamseyApiClient client)
    {
        try
        {
            return client.StartNewSession();
        }
        catch (Exception ex)
        {
            return $"Error: {ex.GetType().Name}: {ex.Message}";
        }
    }

    [McpServerTool, Description("Delete a chat session from Ask Ramsey history. Requires authentication (login first). Use get_chat_history to find session IDs.")]
    public static async Task<string> DeleteChat(
        AskRamseyApiClient client,
        [Description("The chat session ID to delete. Get this from chat history or from a previous ask_ramsey response.")] string chatSessionId)
    {
        try
        {
            await client.DeleteChatAsync(chatSessionId);
            return $"Chat session {chatSessionId} deleted.";
        }
        catch (Exception ex)
        {
            return $"Error: {ex.GetType().Name}: {ex.Message}";
        }
    }
}
