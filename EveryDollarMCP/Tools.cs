using System.ComponentModel;
using System.Text.Json;
using EveryDollarMCP.Models;
using ModelContextProtocol.Server;

[McpServerToolType]
public static class AuthTools
{
    [McpServerTool, Description("Log in to EveryDollar by opening a browser window. You'll see a browser open — log in normally (or let saved credentials auto-fill). The tool captures the session automatically once login completes. Times out after 2 minutes. Auto-detects Edge or Chrome; specify browser to override. Credentials are encrypted and cached locally — you won't need to re-login unless the session expires.")]
    public static async Task<string> Login(
        EveryDollarApiClient client,
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

    [McpServerTool, Description("Set up authentication by providing a CSRF token and session cookie from the EveryDollar web app. Open EveryDollar in your browser, then: 1) Get CSRF token from browser console: window._CSRF, 2) Get SESSION cookie from DevTools > Application > Cookies. Credentials are encrypted and cached locally for future sessions.")]
    public static string SetSessionCookie(
        EveryDollarApiClient client,
        [Description("CSRF token from browser console: window._CSRF")] string csrfToken,
        [Description("Cookie string from DevTools, e.g. 'SESSION=abc123'")] string cookies)
    {
        return client.SetSessionManually(csrfToken, cookies);
    }
}

[McpServerToolType]
public static class BudgetReadTools
{
    [McpServerTool, Description("Get the budget for a specific month. Returns all budget groups and line items with amounts in cents. Date should be the first of the month (YYYY-MM-01). The 'Buffer' value is checking account cash at month start used for cashflow timing — it is NOT unallocated income or surplus. Budget surplus = Total Income - Total Expenses.")]
    public static async Task<string> GetBudget(
        EveryDollarApiClient client,
        [Description("First day of the month to get budget for, e.g. '2026-04-01'")] string date)
    {
        try
        {
            var budget = await client.GetBudgetByDateAsync(date);
            if (budget is null) return "No budget found for that date.";

        var lines = new List<string>
        {
            $"Budget: {budget.Date} (ID: {budget.Id})",
            $"Buffer: ${budget.BufferAmountCents / 100.0:F2} (checking account cash at month start for cashflow timing — NOT unallocated income or budget surplus)",
            ""
        };

        foreach (var group in budget.Groups)
        {
            lines.Add($"=== {group.Label} ({group.Type}) === (Group ID: {group.Id})");
            foreach (var item in group.BudgetItems)
            {
                var spent = item.Allocations.Sum(a => a.AmountCents);
                var fundTag = item.Type == "sinking_fund" ? " [fund]" : "";
                var targetTag = item.Type == "sinking_fund" && item.TargetAmount.HasValue ? $" target=${item.TargetAmount.Value / 100.0:F2}" : "";
                var carryOver = item.CarryOverBalance != 0 ? $" carry=${item.CarryOverBalance / 100.0:F2}" : "";
                var favTag = item.IsFavorite ? " ★" : "";
                lines.Add($"  {item.Label}{fundTag}{favTag}: budgeted ${item.AmountBudgeted / 100.0:F2}, spent/received ${spent / 100.0:F2}{targetTag}{carryOver} (ID: {item.Id})");
                var extras = new List<string>();
                if (item.Note is not null) extras.Add($"note=\"{item.Note}\"");
                if (item.DueDateString is not null) extras.Add($"due={item.DueDateString}");
                string[] dayNames = ["Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat"];
                if (item.WeeklyFillDay.HasValue)
                    extras.Add($"fill=weekly {dayNames[item.WeeklyFillDay.Value]}");
                else if (item.MonthlyFillDays is { Count: > 0 })
                    extras.Add($"fill days={string.Join(",", item.MonthlyFillDays)}");
                if (extras.Count > 0) lines.Add($"    [{string.Join(" | ", extras)}]");
            }
            lines.Add("");
        }

        return string.Join("\n", lines);
        }
        catch (Exception ex)
        {
            return $"Error getting budget: {ex.GetType().Name}: {ex.Message}";
        }
    }

    [McpServerTool, Description("List all budgets that exist, organized by year and month. Returns budget IDs for each month.")]
    public static async Task<string> ListBudgets(EveryDollarApiClient client)
    {
        try
        {
        var existence = await client.GetBudgetsAsync();
        if (existence is null) return "No budgets found.";

        var lines = new List<string>();
        foreach (var (year, months) in existence.Budgets.OrderByDescending(kv => kv.Key))
        {
            lines.Add($"Year {year}:");
            foreach (var (month, id) in months.OrderByDescending(kv => int.Parse(kv.Key)))
            {
                lines.Add($"  Month {month}: {id}");
            }
        }
        return string.Join("\n", lines);
        }
        catch (Exception ex)
        {
            return $"Error: {ex.Message}";
        }
    }

    [McpServerTool, Description("Get the spending history for a specific budget line item across previous months. Requires previousBudgetIds to specify which months to query.")]
    public static async Task<string> GetBudgetItemHistory(
        EveryDollarApiClient client,
        [Description("The budget ID that contains the item")] string budgetId,
        [Description("The budget item ID (numeric part or full URN)")] string itemId,
        [Description("Required: comma-separated list of previous budget IDs to include in history")] string previousBudgetIds)
    {
        try
        {
            return await client.GetBudgetItemHistoryAsync(budgetId, itemId, previousBudgetIds);
        }
        catch (Exception ex)
        {
            return $"Error getting budget item history: {ex.GetType().Name}: {ex.Message}";
        }
    }
}

[McpServerToolType]
public static class TransactionTools
{
    [McpServerTool, Description("Get full details of a single transaction including note, checkNumber, description, and bank account info.")]
    public static async Task<string> GetTransaction(
        EveryDollarApiClient client,
        [Description("Transaction ID")] string transactionId)
    {
        try
        {
        var t = await client.GetTransactionAsync(transactionId);
        if (t is null) return "Transaction not found.";

        var lines = new List<string>
        {
            $"Transaction: {t.Id}",
            $"  Amount: {(t.Amount >= 0 ? "+" : "-")}${Math.Abs(t.Amount) / 100.0:F2}",
            $"  Date: {t.Date}",
            $"  Merchant: {t.Merchant}",
        };
        if (t.Description is not null) lines.Add($"  Description: {t.Description}");
        if (t.Note is not null) lines.Add($"  Note: {t.Note}");
        if (t.CheckNumber is not null) lines.Add($"  Check #: {t.CheckNumber}");
        if (t.BankAccountId is not null) lines.Add($"  Bank Account: {t.BankAccountId}");
        if (t.BankTransactionId is not null) lines.Add($"  Bank Transaction: {t.BankTransactionId}");
        if (t.DeletedAt is not null) lines.Add($"  Deleted: {t.DeletedAt}");
        if (t.Allocations.Count > 0)
        {
            lines.Add($"  Allocations ({t.Allocations.Count}):");
            foreach (var a in t.Allocations)
                lines.Add($"    ${Math.Abs(a.Amount) / 100.0:F2} -> {a.BudgetItemId}");
        }
        else
        {
            lines.Add("  Allocations: none (unallocated)");
        }

        return string.Join("\n", lines);
        }
        catch (Exception ex)
        {
            return $"Error: {ex.Message}";
        }
    }

    [McpServerTool, Description("Get transactions within a date range. Amounts are in cents (negative = expense, positive = income). Omit dates to get all recent transactions. Use unallocatedOnly to find uncategorized transactions.")]
    public static async Task<string> GetTransactions(
        EveryDollarApiClient client,
        [Description("Start date (YYYY-MM-DD), optional")] string? startDate = null,
        [Description("End date (YYYY-MM-DD), optional")] string? endDate = null,
        [Description("If true, only show transactions with no budget category assigned")] bool unallocatedOnly = false)
    {
        try
        {
        var result = await client.GetTransactionsAsync(startDate, endDate);
        if (result is null) return "No transactions found.";

        var active = result.Transactions.Where(t => t.DeletedAt is null);
        if (unallocatedOnly)
        {
            active = active.Where(t => t.Allocations.Count == 0 || t.Allocations.Any(a => string.IsNullOrEmpty(a.BudgetItemId)));
        }
        var activeList = active.ToList();
        var lines = new List<string>
        {
            $"Transactions from {result.StartDate} to {result.EndDate ?? "now"}: {activeList.Count}{(unallocatedOnly ? " unallocated" : " active")}",
            ""
        };

        foreach (var t in activeList.OrderByDescending(t => t.Date))
        {
            var amountStr = t.Amount >= 0 ? $"+${t.Amount / 100.0:F2}" : $"-${Math.Abs(t.Amount) / 100.0:F2}";
            var allocInfo = t.Allocations.Count > 0
                ? $" [{t.Allocations.Count} allocation(s)]"
                : " [unallocated]";
            var noteTag = t.Note is not null ? " 📝" : "";
            lines.Add($"  {t.Date} {amountStr} {t.Merchant}{allocInfo}{noteTag} (ID: {t.Id})");
        }

        return string.Join("\n", lines);
        }
        catch (Exception ex)
        {
            return $"Error: {ex.Message}";
        }
    }

    [McpServerTool, Description("Create a new manual transaction. Amount in cents (negative for expenses, positive for income). Allocations assign portions of the amount to budget categories.")]
    public static async Task<string> CreateTransaction(
        EveryDollarApiClient client,
        [Description("Amount in cents (negative for expense, positive for income)")] long amount,
        [Description("Transaction date (YYYY-MM-DD)")] string date,
        [Description("Merchant/payee name")] string merchant,
        [Description("Optional note. Use actual newlines for multi-line notes.")] string? note = null,
        [Description("Optional check number")] string? checkNumber = null,
        [Description("JSON array of allocations: [{\"amount\": -500, \"budgetItemId\": \"urn:...\"}]")] string? allocationsJson = null,
        [Description("Bank account ID to associate with")] string? accountId = null)
    {
        List<TransactionAllocation>? allocations = null;
        if (allocationsJson is not null)
        {
            allocations = JsonSerializer.Deserialize<List<TransactionAllocation>>(allocationsJson);
        }

        // Unescape literal \n sequences that MCP clients may send instead of real newlines
        var normalizedNote = note?.Replace("\\n", "\n");

        var request = new CreateTransactionRequest(amount, date, merchant, normalizedNote, checkNumber, allocations, accountId);
        try
        {
            return await client.CreateTransactionAsync(request);
        }
        catch (Exception ex)
        {
            return $"Error: {ex.Message}";
        }
    }

    [McpServerTool, Description("Update an existing transaction. You must provide amount, date, and merchant (required by the API even if unchanged). For note, checkNumber, and allocations: omit to preserve existing values; pass empty string/array to clear them.")]
    public static async Task<string> UpdateTransaction(
        EveryDollarApiClient client,
        [Description("Transaction ID to update")] string transactionId,
        [Description("New amount in cents")] long amount,
        [Description("New date (YYYY-MM-DD)")] string date,
        [Description("New merchant name")] string merchant,
        [Description("Omit to preserve existing note. Pass '' to clear. Pass text to set.")] string? note = null,
        [Description("Omit to preserve existing check number. Pass '' to clear. Pass value to set.")] string? checkNumber = null,
        [Description("Omit to preserve existing allocations. Pass '[]' to clear/unassign. Pass '[{\"amount\":123,\"budgetItemId\":\"...\"}]' to set.")] string? allocationsJson = null,
        [Description("Bank account ID")] string? accountId = null)
    {
        // Fetch existing transaction once if we need to preserve any field
        Transaction? existing = null;
        if (note is null || checkNumber is null || allocationsJson is null)
        {
            existing = await client.GetTransactionAsync(transactionId);
        }

        // Allocations: null = preserve, "[]" = clear, "[{...}]" = set
        List<TransactionAllocation>? allocations;
        if (allocationsJson is not null)
        {
            allocations = JsonSerializer.Deserialize<List<TransactionAllocation>>(allocationsJson);
        }
        else
        {
            allocations = existing?.Allocations is { Count: > 0 } ? existing.Allocations : null;
        }

        // Note: null = preserve, "" = clear, other = set
        string? resolvedNote;
        if (note is null)
        {
            resolvedNote = existing?.Note;
        }
        else if (note == "")
        {
            resolvedNote = null;
        }
        else
        {
            // Unescape literal \n sequences that MCP clients may send instead of real newlines
            resolvedNote = note.Replace("\\n", "\n");
        }

        // CheckNumber: null = preserve, "" = clear, other = set
        string? resolvedCheckNumber;
        if (checkNumber is null)
        {
            resolvedCheckNumber = existing?.CheckNumber;
        }
        else if (checkNumber == "")
        {
            resolvedCheckNumber = null;
        }
        else
        {
            resolvedCheckNumber = checkNumber;
        }

        var request = new UpdateTransactionRequest(amount, date, merchant, resolvedNote, resolvedCheckNumber, allocations, accountId);
        try
        {
            return await client.UpdateTransactionAsync(transactionId, request);
        }
        catch (Exception ex)
        {
            return $"Error: {ex.Message}";
        }
    }

    [McpServerTool, Description("Delete one or more transactions by their IDs.")]
    public static async Task<string> DeleteTransactions(
        EveryDollarApiClient client,
        [Description("Comma-separated transaction IDs to delete")] string transactionIds)
    {
        try
        {
        List<string> ids;
        var trimmed = transactionIds.Trim();
        if (trimmed.StartsWith('['))
        {
            ids = JsonSerializer.Deserialize<List<string>>(trimmed) ?? [];
        }
        else
        {
            ids = trimmed.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList();
        }

        if (ids.Count == 0)
        {
            return "Error: No transaction IDs provided.";
        }

        await client.DeleteTransactionsAsync(ids);
        return $"Deleted {ids.Count} transaction(s).";
        }
        catch (Exception ex)
        {
            return $"Error: {ex.Message}";
        }
    }

    [McpServerTool, Description("Restore previously deleted transactions by their IDs.")]
    public static async Task<string> RestoreTransactions(
        EveryDollarApiClient client,
        [Description("Comma-separated transaction IDs to restore")] string transactionIds)
    {
        try
        {
        List<string> ids;
        var trimmed = transactionIds.Trim();
        if (trimmed.StartsWith('['))
        {
            ids = JsonSerializer.Deserialize<List<string>>(trimmed) ?? [];
        }
        else
        {
            ids = trimmed.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList();
        }

        if (ids.Count == 0)
        {
            return "Error: No transaction IDs provided.";
        }

        var result = await client.RestoreTransactionsAsync(ids);
        return $"Restored {ids.Count} transaction(s).\n{result}";
        }
        catch (Exception ex)
        {
            return $"Error: {ex.Message}";
        }
    }
}

[McpServerToolType]
public static class BudgetWriteTools
{
    [McpServerTool, Description("Create a new budget line item in a budget group. For expense items, provide budgetGroupId. For goals, set type to 'goals' and provide targetAmount (cents) and targetDate.")]
    public static async Task<string> CreateBudgetItem(
        EveryDollarApiClient client,
        [Description("Budget ID (e.g. '5ba1af4f-...')")] string budgetId,
        [Description("Item type: 'expense', 'income', 'goals', 'debt'")] string type,
        [Description("Label/name for the budget item")] string label,
        [Description("Budget group ID to add the item to (required for expense/income items)")] string? budgetGroupId = null,
        [Description("Whether to mark as favorite")] bool favorite = false,
        [Description("Target amount in cents (for goals)")] long? targetAmount = null,
        [Description("Target date YYYY-MM-DD (for goals)")] string? targetDate = null,
        [Description("Starting balance in cents (for goals)")] long? startingBalance = null)
    {
        var request = new CreateBudgetItemRequest(budgetGroupId, type, label, favorite, targetAmount, targetDate, startingBalance);
        try
        {
            return await client.CreateBudgetItemAsync(budgetId, request);
        }
        catch (Exception ex)
        {
            return $"Error: {ex.Message}";
        }
    }

    [McpServerTool, Description("Update a budget line item's amount, fill schedule, target amount, or mark a goal as completed. You must provide at least one field to change. Every item must have a fill schedule (the API requires it) — to switch from weekly to monthly or vice versa, just set the new schedule and the old one is replaced.")]
    public static async Task<string> UpdateBudgetItem(
        EveryDollarApiClient client,
        [Description("Budget ID")] string budgetId,
        [Description("Budget item ID (numeric part, e.g. '3045617024')")] string itemId,
        [Description("New budgeted amount in cents")] long? amountBudgeted = null,
        [Description("Monthly fill days as comma-separated numbers, e.g. '1,15'. Mutually exclusive with weeklyFillDay.")] string? monthlyFillDays = null,
        [Description("Weekly fill day: 0=Sun, 1=Mon, 2=Tue, 3=Wed, 4=Thu, 5=Fri, 6=Sat. Mutually exclusive with monthlyFillDays.")] int? weeklyFillDay = null,
        [Description("Date to mark goal as completed (YYYY-MM-DD)")] string? completedAt = null,
        [Description("Target amount in cents (for funds/goals)")] long? targetAmount = null)
    {
        if (amountBudgeted is null && monthlyFillDays is null && weeklyFillDay is null && completedAt is null && targetAmount is null)
        {
            return "Error: No fields to update. Provide at least one of: amountBudgeted, monthlyFillDays, weeklyFillDay, completedAt, targetAmount.";
        }

        if (monthlyFillDays is not null && weeklyFillDay is not null)
        {
            return "Error: monthlyFillDays and weeklyFillDay are mutually exclusive.";
        }

        List<int>? fillDays = null;
        if (monthlyFillDays is not null)
        {
            fillDays = monthlyFillDays.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Select(int.Parse).ToList();
            if (fillDays.Count == 0)
                return "Error: monthlyFillDays must contain at least one day (the API requires a fill schedule).";
        }

        var request = new UpdateBudgetItemRequest(amountBudgeted, fillDays, weeklyFillDay, completedAt, targetAmount);
        try
        {
            return await client.UpdateBudgetItemAsync(budgetId, itemId, request);
        }
        catch (Exception ex)
        {
            return $"Error: {ex.Message}";
        }
    }

    [McpServerTool, Description("Update the buffer amount for a budget. The buffer is the checking account cash at month start used for cashflow timing in paycheck planning.")]
    public static async Task<string> UpdateBuffer(
        EveryDollarApiClient client,
        [Description("Budget ID")] string budgetId,
        [Description("Buffer amount in cents (e.g. 536000 for $5,360.00)")] long bufferAmountCents)
    {
        try
        {
            await client.UpdateBufferAsync(budgetId, bufferAmountCents);
            return $"Buffer updated to ${bufferAmountCents / 100.0:F2}.";
        }
        catch (Exception ex)
        {
            return $"Error: {ex.Message}";
        }
    }

    [McpServerTool, Description("Delete a budget line item from a budget.")]
    public static async Task<string> DeleteBudgetItem(
        EveryDollarApiClient client,
        [Description("Budget ID")] string budgetId,
        [Description("Budget item ID (numeric part)")] string itemId)
    {
        try
        {
            await client.DeleteBudgetItemAsync(budgetId, itemId);
            return "Budget item deleted.";
        }
        catch (Exception ex)
        {
            return $"Error: {ex.Message}";
        }
    }

    [McpServerTool, Description("Reorder budget items within a group. Provide ALL item IDs in the group in the desired order.")]
    public static async Task<string> ReorderBudgetItems(
        EveryDollarApiClient client,
        [Description("Budget ID")] string budgetId,
        [Description("JSON array of ALL budget item IDs in the group, in desired order. E.g. '[\"urn:...:item:123\", \"urn:...:item:456\"]'")] string itemIdsJson)
    {
        try
        {
        var itemIds = JsonSerializer.Deserialize<List<string>>(itemIdsJson);
        if (itemIds is null || itemIds.Count == 0)
        {
            return "Error: itemIdsJson must be a non-empty JSON array of budget item ID strings.";
        }

        return await client.ReorderBudgetItemsAsync(budgetId, itemIds);
        }
        catch (Exception ex)
        {
            return $"Error: {ex.Message}";
        }
    }

    [McpServerTool, Description("Create a new budget group (custom spending category section).")]
    public static async Task<string> CreateBudgetGroup(
        EveryDollarApiClient client,
        [Description("Budget ID")] string budgetId,
        [Description("Group type: 'expense', 'income', or 'debt'")] string type,
        [Description("Group label/name")] string label)
    {
        try
        {
            return await client.CreateBudgetGroupAsync(budgetId, type, label);
        }
        catch (Exception ex)
        {
            return $"Error: {ex.Message}";
        }
    }

    [McpServerTool, Description("Delete a budget group. The group must be empty (no items) first.")]
    public static async Task<string> DeleteBudgetGroup(
        EveryDollarApiClient client,
        [Description("Budget ID")] string budgetId,
        [Description("Group ID to delete (numeric part or full URN)")] string groupId)
    {
        try
        {
            await client.DeleteBudgetGroupAsync(budgetId, groupId);
            return "Budget group deleted.";
        }
        catch (Exception ex)
        {
            return $"Error: {ex.Message}";
        }
    }

    [McpServerTool, Description("Reorder budget groups. Provide ALL group IDs in the desired display order.")]
    public static async Task<string> ReorderBudgetGroups(
        EveryDollarApiClient client,
        [Description("Budget ID")] string budgetId,
        [Description("JSON array of ALL budget group IDs in desired order")] string groupIdsJson)
    {
        try
        {
        var groupIds = JsonSerializer.Deserialize<List<string>>(groupIdsJson);
        if (groupIds is null || groupIds.Count == 0)
        {
            return "Error: groupIdsJson must be a non-empty JSON array of group ID strings.";
        }

        return await client.ReorderBudgetGroupsAsync(budgetId, groupIds);
        }
        catch (Exception ex)
        {
            return $"Error: {ex.Message}";
        }
    }

    [McpServerTool, Description("Clone an existing budget to create a new budget for a different month. Returns the new budget.")]
    public static async Task<string> CloneBudget(
        EveryDollarApiClient client,
        [Description("Budget ID to clone from")] string sourceBudgetId,
        [Description("Target month date (YYYY-MM-01)")] string targetDate)
    {
        try
        {
            return await client.CloneBudgetAsync(sourceBudgetId, targetDate);
        }
        catch (Exception ex)
        {
            return $"Error: {ex.Message}";
        }
    }

    [McpServerTool, Description("Reset a budget to its default state. WARNING: This clears all budgeted amounts.")]
    public static async Task<string> ResetBudget(
        EveryDollarApiClient client,
        [Description("Budget ID to reset")] string budgetId)
    {
        try
        {
            return await client.ResetBudgetAsync(budgetId);
        }
        catch (Exception ex)
        {
            return $"Error: {ex.Message}";
        }
    }

    [McpServerTool, Description("Convert a regular budget item into a sinking fund (savings fund with a target).")]
    public static async Task<string> ConvertToFund(
        EveryDollarApiClient client,
        [Description("Budget ID")] string budgetId,
        [Description("Budget item ID (numeric part)")] string itemId,
        [Description("Target savings amount in cents (0 to set later)")] long targetAmount = 0,
        [Description("Starting balance in cents (0 if starting fresh)")] long originalStartingBalance = 0)
    {
        try
        {
            return await client.ConvertToFundAsync(budgetId, itemId, targetAmount, originalStartingBalance);
        }
        catch (Exception ex)
        {
            return $"Error: {ex.Message}";
        }
    }

    [McpServerTool, Description("Convert a sinking fund back to a regular expense item. This is a compound operation: deletes the fund, creates a new regular expense item in the same group, and reassigns all transactions.")]
    public static async Task<string> ConvertFromFund(
        EveryDollarApiClient client,
        [Description("Budget ID")] string budgetId,
        [Description("Budget item ID (full URN, e.g. 'urn:everydollar:budget:...:item:123')")] string itemId,
        [Description("Budget date for transaction lookup (YYYY-MM-01)")] string budgetDate)
    {
        try
        {
        // 1. Get budget to find the item details
        var budget = await client.GetBudgetByDateAsync(budgetDate);
        if (budget is null) return "Error: Budget not found for that date.";

        BudgetItem? fundItem = null;
        BudgetGroup? itemGroup = null;
        foreach (var group in budget.Groups)
        {
            fundItem = group.BudgetItems.FirstOrDefault(i => i.Id == itemId);
            if (fundItem is not null) { itemGroup = group; break; }
        }
        if (fundItem is null || itemGroup is null) return $"Error: Item '{itemId}' not found in budget.";

        // 2. Get transactions to find ones allocated to this item (no date params — API won't return future-month transactions with any date filter)
        var txResult = await client.GetTransactionsAsync();
        var affectedTransactions = txResult?.Transactions
            .Where(t => t.DeletedAt is null && t.Allocations.Any(a => a.BudgetItemId == itemId))
            .ToList() ?? [];

        // 3. Delete the fund item
        var numericId = itemId.Contains(":item:") ? itemId.Split(":item:").Last() : itemId;
        await client.DeleteBudgetItemAsync(budgetId, numericId);

        // 4. Create a new regular expense item in the same group
        var createRequest = new CreateBudgetItemRequest(
            BudgetGroupId: itemGroup.Id,
            Type: "expense",
            Label: fundItem.Label
        );
        var createResponse = await client.CreateBudgetItemAsync(budgetId, createRequest);

        // 5. Extract new item ID from response
        using var doc = System.Text.Json.JsonDocument.Parse(createResponse);
        var newItemId = doc.RootElement.GetProperty("id").GetString();
        if (newItemId is null) return $"Error: Could not get new item ID from response. Created item: {createResponse}";

        // 6. Set the budgeted amount on the new item
        if (fundItem.AmountBudgeted != 0)
        {
            var updateReq = new UpdateBudgetItemRequest(AmountBudgeted: fundItem.AmountBudgeted);
            var newNumericId = newItemId.Contains(":item:") ? newItemId.Split(":item:").Last() : newItemId;
            await client.UpdateBudgetItemAsync(budgetId, newNumericId, updateReq);
        }

        // 7. Reassign transactions to the new item
        var reassigned = 0;
        foreach (var tx in affectedTransactions)
        {
            var newAllocations = tx.Allocations.Select(a =>
                a.BudgetItemId == itemId ? new TransactionAllocation(a.Amount, newItemId) : a
            ).ToList();

            var updateTx = new UpdateTransactionRequest(
                tx.Amount, tx.Date, tx.Merchant ?? "", tx.Note, tx.CheckNumber, newAllocations, tx.BankAccountId);
            await client.UpdateTransactionAsync(tx.Id, updateTx);
            reassigned++;
        }

        return $"Converted fund '{fundItem.Label}' to regular expense item.\nNew item ID: {newItemId}\nTransactions reassigned: {reassigned}";
        }
        catch (Exception ex)
        {
            return $"Error: {ex.Message}";
        }
    }

    [McpServerTool, Description("Move a budget item to a different group. This is a compound operation: deletes the item from the old group, creates it in the new group, and reassigns all transactions.")]
    public static async Task<string> MoveBudgetItem(
        EveryDollarApiClient client,
        [Description("Budget ID")] string budgetId,
        [Description("Budget item ID (full URN, e.g. 'urn:everydollar:budget:...:item:123')")] string itemId,
        [Description("Target budget group ID to move the item to")] string targetGroupId,
        [Description("Budget date for transaction lookup (YYYY-MM-01)")] string budgetDate)
    {
        try
        {
        // 1. Get budget to find the item details
        var budget = await client.GetBudgetByDateAsync(budgetDate);
        if (budget is null) return "Error: Budget not found for that date.";

        BudgetItem? sourceItem = null;
        foreach (var group in budget.Groups)
        {
            sourceItem = group.BudgetItems.FirstOrDefault(i => i.Id == itemId);
            if (sourceItem is not null) break;
        }
        if (sourceItem is null) return $"Error: Item '{itemId}' not found in budget.";

        // Verify target group exists
        var targetGroup = budget.Groups.FirstOrDefault(g => g.Id == targetGroupId);
        if (targetGroup is null) return $"Error: Target group '{targetGroupId}' not found in budget.";

        // 2. Get transactions to find ones allocated to this item (no date params — API won't return future-month transactions with any date filter)
        var txResult = await client.GetTransactionsAsync();
        var affectedTransactions = txResult?.Transactions
            .Where(t => t.DeletedAt is null && t.Allocations.Any(a => a.BudgetItemId == itemId))
            .ToList() ?? [];

        // 3. Delete the item from the old group
        var numericId = itemId.Contains(":item:") ? itemId.Split(":item:").Last() : itemId;
        await client.DeleteBudgetItemAsync(budgetId, numericId);

        // 4. Create the item in the new group
        var createRequest = new CreateBudgetItemRequest(
            BudgetGroupId: targetGroupId,
            Type: sourceItem.Type,
            Label: sourceItem.Label
        );
        var createResponse = await client.CreateBudgetItemAsync(budgetId, createRequest);

        // 5. Extract new item ID from response
        using var doc = System.Text.Json.JsonDocument.Parse(createResponse);
        var newItemId = doc.RootElement.GetProperty("id").GetString();
        if (newItemId is null) return $"Error: Could not get new item ID from response. Created item: {createResponse}";

        // 6. Set the budgeted amount on the new item
        if (sourceItem.AmountBudgeted != 0)
        {
            var updateReq = new UpdateBudgetItemRequest(AmountBudgeted: sourceItem.AmountBudgeted);
            var newNumericId = newItemId.Contains(":item:") ? newItemId.Split(":item:").Last() : newItemId;
            await client.UpdateBudgetItemAsync(budgetId, newNumericId, updateReq);
        }

        // 7. Reassign transactions to the new item
        var reassigned = 0;
        foreach (var tx in affectedTransactions)
        {
            var newAllocations = tx.Allocations.Select(a =>
                a.BudgetItemId == itemId ? new TransactionAllocation(a.Amount, newItemId) : a
            ).ToList();

            var updateTx = new UpdateTransactionRequest(
                tx.Amount, tx.Date, tx.Merchant ?? "", tx.Note, tx.CheckNumber, newAllocations, tx.BankAccountId);
            await client.UpdateTransactionAsync(tx.Id, updateTx);
            reassigned++;
        }

        return $"Moved '{sourceItem.Label}' to group '{targetGroup.Label}'.\nNew item ID: {newItemId}\nTransactions reassigned: {reassigned}";
        }
        catch (Exception ex)
        {
            return $"Error: {ex.Message}";
        }
    }

    [McpServerTool, Description("Transfer money from a fund budget item to another budget item. Creates two paired transactions: a debit from the source fund and a credit to the destination item. The source must be a fund (has carry-over balance). The destination can be any budget item type. If the source is not a fund, reduce its planned/budgeted amount instead of using this tool.")]
    public static async Task<string> TransferFund(
        EveryDollarApiClient client,
        [Description("Budget date (YYYY-MM-01) for the budget containing both items")] string budgetDate,
        [Description("Source fund budget item ID (full URN or numeric ID). Money is deducted from this fund.")] string sourceItemId,
        [Description("Destination budget item ID (full URN or numeric ID). Money is added to this item.")] string destinationItemId,
        [Description("Amount to transfer in cents (positive number, e.g. 19100 for $191.00)")] long amountCents)
    {
        try
        {
        if (amountCents <= 0)
            return "Error: amountCents must be a positive number.";

        // 1. Get budget to resolve item labels and validate source is a fund
        var budget = await client.GetBudgetByDateAsync(budgetDate);
        if (budget is null) return "Error: Budget not found for that date.";

        BudgetItem? sourceItem = null;
        BudgetItem? destItem = null;
        foreach (var group in budget.Groups)
        {
            foreach (var item in group.BudgetItems)
            {
                if (item.Id == sourceItemId) sourceItem = item;
                if (item.Id == destinationItemId) destItem = item;
            }
        }

        if (sourceItem is null) return $"Error: Source item '{sourceItemId}' not found in budget.";
        if (destItem is null) return $"Error: Destination item '{destinationItemId}' not found in budget.";
        if (sourceItem.Type is not "fund" and not "sinking_fund") return $"Error: Source item '{sourceItem.Label}' is not a fund (type: {sourceItem.Type}). For non-fund items, reduce the budgeted/planned amount instead of transferring.";

        // Determine transaction date: must fall within the budget month
        if (!DateTime.TryParseExact(budgetDate, "yyyy-MM-dd", null, System.Globalization.DateTimeStyles.None, out var budgetMonth))
            return "Error: budgetDate must be in YYYY-MM-DD format (e.g. 2026-04-01).";

        var today = DateTime.Now.Date;
        var budgetMonthStart = new DateTime(budgetMonth.Year, budgetMonth.Month, 1);
        var budgetMonthEnd = budgetMonthStart.AddMonths(1).AddDays(-1);

        if (budgetMonthStart > today)
            return $"Error: Budget month {budgetMonthStart:yyyy-MM} is in the future. For future months, modify the planned/budgeted amounts for the categories instead of transferring fund balances.";

        string transactionDate;
        if (budgetMonthEnd < today)
            transactionDate = budgetMonthEnd.ToString("yyyy-MM-dd"); // past month: use last day
        else
            transactionDate = today.ToString("yyyy-MM-dd"); // current month: use today

        // 2. Create debit transaction (negative amount from source fund)
        var debitRequest = new CreateTransactionRequest(
            Amount: -amountCents,
            Date: transactionDate,
            Merchant: $"Transfer to {destItem.Label}",
            Allocations: [new TransactionAllocation(-amountCents, sourceItem.Id)]
        );
        var debitResult = await client.CreateTransactionAsync(debitRequest);

        // 3. Create credit transaction (positive amount to destination fund)
        var creditRequest = new CreateTransactionRequest(
            Amount: amountCents,
            Date: transactionDate,
            Merchant: $"Transfer from {sourceItem.Label}",
            Allocations: [new TransactionAllocation(amountCents, destItem.Id)]
        );
        var creditResult = await client.CreateTransactionAsync(creditRequest);

        return $"Fund transfer complete: ${amountCents / 100.0:F2} from '{sourceItem.Label}' to '{destItem.Label}'.\nDebit transaction: {debitResult}\nCredit transaction: {creditResult}";
        }
        catch (Exception ex)
        {
            return $"Error: {ex.Message}";
        }
    }
}

[McpServerToolType]
public static class ReadOnlyTools
{
    [McpServerTool, Description("Get debt snowball information including all debts with balances, interest rates, and payment amounts.")]
    public static async Task<string> GetDebtSnowball(EveryDollarApiClient client)
    {
        try
        {
            return await client.GetDebtSnowballAsync();
        }
        catch (Exception ex)
        {
            return $"Error: {ex.Message}";
        }
    }

    [McpServerTool, Description("Get savings goals with target amounts, available balances, and target dates.")]
    public static async Task<string> GetGoals(EveryDollarApiClient client)
    {
        try
        {
            return await client.GetGoalsAsync();
        }
        catch (Exception ex)
        {
            return $"Error: {ex.Message}";
        }
    }

    [McpServerTool, Description("Update user preferences for the EveryDollar app. Pass a JSON object with preference keys and values.")]
    public static async Task<string> UpdateUserPreferences(
        EveryDollarApiClient client,
        [Description("JSON object of preferences to update. Keys: showSuggestedAllocations (bool), useFillScheduleInAmountRemaining (bool), paycheckPlanningVisible (bool), budgetDefaultTab (string), hidePaycheckPlanningWalkthrough (bool)")] string preferencesJson)
    {
        try
        {
        var preferences = JsonSerializer.Deserialize<Dictionary<string, object>>(preferencesJson);
        if (preferences is null || preferences.Count == 0)
        {
            return "Error: preferencesJson must be a non-empty JSON object.";
        }

        return await client.UpdateUserPreferencesAsync(preferences);
        }
        catch (Exception ex)
        {
            return $"Error: {ex.Message}";
        }
    }
}

[McpServerToolType]
public static class AccountTools
{
    [McpServerTool, Description("List all linked bank accounts with their names, types, balances, and sync status.")]
    public static async Task<string> GetAccounts(EveryDollarApiClient client)
    {
        try
        {
        var accounts = await client.GetAccountsAsync();
        var lines = new List<string> { $"Linked accounts: {accounts.Count}", "" };

        foreach (var a in accounts)
        {
            var balance = $"${a.BalanceCents / 100.0:F2}";
            var status = a.Connection?.Status ?? "unknown";
            var lastSync = a.Connection?.LastUpdatedAt ?? "never";
            var subType = a.SubType is not null ? $"/{a.SubType}" : "";
            var acctNum = a.AccountNumberDisplay is not null ? $" {a.AccountNumberDisplay}" : "";
            lines.Add($"  {a.Name} ({a.Type}{subType}){acctNum} at {a.InstitutionName}");
            lines.Add($"    Balance: {balance} | Status: {status} | Last sync: {lastSync}");
            if (a.Connection?.Error is not null) lines.Add($"    Error: {a.Connection.Error}");
            lines.Add($"    ID: {a.Id}");
        }

        return string.Join("\n", lines);
        }
        catch (Exception ex)
        {
            return $"Error: {ex.Message}";
        }
    }

    [McpServerTool, Description("Trigger a refresh/sync of all linked bank accounts.")]
    public static async Task<string> RefreshBankAccounts(EveryDollarApiClient client)
    {
        try
        {
            await client.RefreshBankAccountsAsync();
            return "Bank account refresh triggered.";
        }
        catch (Exception ex)
        {
            return $"Error: {ex.Message}";
        }
    }
}

[McpServerToolType]
public static class InsightTools
{
    [McpServerTool, Description("Get budget insights (spending trends, patterns) for a date range.")]
    public static async Task<string> GetInsights(
        EveryDollarApiClient client,
        [Description("Start date (YYYY-MM-DD)")] string startDate,
        [Description("End date (YYYY-MM-DD)")] string endDate)
    {
        try
        {
            return await client.GetInsightsAsync(startDate, endDate);
        }
        catch (Exception ex)
        {
            return $"Error: {ex.GetType().Name}: {ex.Message}";
        }
    }

    [McpServerTool, Description("Get subscription status for the EveryDollar account.")]
    public static async Task<string> GetSubscription(EveryDollarApiClient client)
    {
        try
        {
        var subs = await client.GetSubscriptionsAsync();
        if (subs.Count == 0) return "No active subscriptions.";

        var lines = new List<string>();
        foreach (var s in subs)
        {
            lines.Add($"{s.FriendlyName}: {s.State}");
            lines.Add($"  Period: {s.StartDate} to {s.EndDate}");
            lines.Add($"  Auto-renew: {s.AutoRenew} | In trial: {s.InTrial}");
            if (s.ProductId is not null) lines.Add($"  Product: {s.ProductId}");
        }
        return string.Join("\n", lines);
        }
        catch (Exception ex)
        {
            return $"Error: {ex.Message}";
        }
    }
}
