using System.Text.Json.Serialization;

namespace EveryDollarMCP.Models;

// --- Budget ---

public record FillPlan(
    [property: JsonPropertyName("date")] string Date,
    [property: JsonPropertyName("amountCents")] long AmountCents
);

public record Allocation(
    [property: JsonPropertyName("budgetItemId")] string BudgetItemId,
    [property: JsonPropertyName("amount")] long AmountCents
);

public record DueDate(
    [property: JsonPropertyName("value")] string? Value,
    [property: JsonPropertyName("recurring")] bool Recurring
);

public record BudgetItem(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("serverId")] string? ServerId,
    [property: JsonPropertyName("budgetGroupId")] string BudgetGroupId,
    [property: JsonPropertyName("label")] string Label,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("subType")] string? SubType,
    [property: JsonPropertyName("amountBudgeted")] long AmountBudgeted,
    [property: JsonPropertyName("carryOverBalance")] long CarryOverBalance,
    [property: JsonPropertyName("originalStartingBalance")] long OriginalStartingBalance,
    [property: JsonPropertyName("targetAmount")] long? TargetAmount,
    [property: JsonPropertyName("isFavorite")] bool IsFavorite,
    [property: JsonPropertyName("dueDate")] System.Text.Json.JsonElement? DueDate,
    [property: JsonPropertyName("note")] string? Note,
    [property: JsonPropertyName("allocations")] List<Allocation> Allocations,
    [property: JsonPropertyName("goal")] System.Text.Json.JsonElement? Goal,
    [property: JsonPropertyName("weeklyFillDay")] int? WeeklyFillDay,
    [property: JsonPropertyName("monthlyFillDays")] List<int>? MonthlyFillDays,
    [property: JsonPropertyName("fillPlans")] List<FillPlan>? FillPlans,
    [property: JsonPropertyName("snowballExternalId")] string? SnowballExternalId,
    [property: JsonPropertyName("debtExternalId")] string? DebtExternalId,
    [property: JsonPropertyName("sortOrder")] int? SortOrder
)
{
    [JsonIgnore]
    public string? DueDateString => DueDate?.ValueKind switch
    {
        System.Text.Json.JsonValueKind.String => DueDate.Value.GetString(),
        System.Text.Json.JsonValueKind.Object when DueDate.Value.TryGetProperty("value", out var v) => v.GetString(),
        _ => null
    };
};

public record BudgetGroup(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("budgetId")] string BudgetId,
    [property: JsonPropertyName("label")] string Label,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("budgetItems")] List<BudgetItem> BudgetItems
);

public record Budget(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("date")] string Date,
    [property: JsonPropertyName("bufferAmountCents")] long BufferAmountCents,
    [property: JsonPropertyName("groups")] List<BudgetGroup> Groups
);

public record BudgetExistence(
    [property: JsonPropertyName("budgetExistence")] Dictionary<string, Dictionary<string, string>> Budgets
);

// --- Transactions ---

public record TransactionAllocation(
    [property: JsonPropertyName("amount")] long Amount,
    [property: JsonPropertyName("budgetItemId")] string BudgetItemId
);

public record Transaction(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("amount")] long Amount,
    [property: JsonPropertyName("date")] string Date,
    [property: JsonPropertyName("merchant")] string? Merchant,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("note")] string? Note,
    [property: JsonPropertyName("checkNumber")] string? CheckNumber,
    [property: JsonPropertyName("deletedAt")] string? DeletedAt,
    [property: JsonPropertyName("bankAccountId")] string? BankAccountId,
    [property: JsonPropertyName("bankTransactionId")] string? BankTransactionId,
    [property: JsonPropertyName("allocations")] List<TransactionAllocation> Allocations
);

public record TransactionSearchResult(
    [property: JsonPropertyName("startDate")] string StartDate,
    [property: JsonPropertyName("endDate")] string? EndDate,
    [property: JsonPropertyName("transactions")] List<Transaction> Transactions
);

// --- Accounts ---

public record AccountConnection(
    [property: JsonPropertyName("aggregator")] string? Aggregator,
    [property: JsonPropertyName("status")] string? Status,
    [property: JsonPropertyName("lastUpdatedAt")] string? LastUpdatedAt,
    [property: JsonPropertyName("error")] string? Error,
    [property: JsonPropertyName("syncTransactionsAvailable")] bool SyncTransactionsAvailable,
    [property: JsonPropertyName("sendTransactionsToBudget")] bool SendTransactionsToBudget
);

public record BankAccount(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("subType")] string? SubType,
    [property: JsonPropertyName("institutionName")] string? InstitutionName,
    [property: JsonPropertyName("balanceCents")] long BalanceCents,
    [property: JsonPropertyName("deletable")] bool Deletable,
    [property: JsonPropertyName("accountNumberDisplay")] string? AccountNumberDisplay,
    [property: JsonPropertyName("connection")] AccountConnection? Connection
);

// --- Subscription ---

public record Subscription(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("autoRenew")] bool AutoRenew,
    [property: JsonPropertyName("endDate")] string EndDate,
    [property: JsonPropertyName("friendlyName")] string? FriendlyName,
    [property: JsonPropertyName("inTrial")] bool InTrial,
    [property: JsonPropertyName("productId")] string? ProductId,
    [property: JsonPropertyName("state")] string State,
    [property: JsonPropertyName("startDate")] string StartDate,
    [property: JsonPropertyName("userId")] string UserId
);

// --- User Preferences ---

public record UserPreferences(
    [property: JsonPropertyName("safeToSpend")] bool SafeToSpend,
    [property: JsonPropertyName("hidePaycheckPlanningWalkthrough")] bool HidePaycheckPlanningWalkthrough,
    [property: JsonPropertyName("smartMoneyEnabled")] bool SmartMoneyEnabled,
    [property: JsonPropertyName("showSuggestedAllocations")] bool ShowSuggestedAllocations,
    [property: JsonPropertyName("paycheckPlanningVisible")] bool PaycheckPlanningVisible
);

// --- Insights ---

public record BudgetInsight(
    [property: JsonPropertyName("startDate")] string? StartDate,
    [property: JsonPropertyName("endDate")] string? EndDate
);

// --- Write operation request models ---

public record CreateBudgetItemRequest(
    [property: JsonPropertyName("budgetGroupId")] string? BudgetGroupId,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("label")] string Label,
    [property: JsonPropertyName("favorite")] bool? Favorite = false,
    [property: JsonPropertyName("targetAmount")] long? TargetAmount = null,
    [property: JsonPropertyName("targetDate")] string? TargetDate = null,
    [property: JsonPropertyName("startingBalance")] long? StartingBalance = null
);

public record UpdateBudgetItemRequest(
    [property: JsonPropertyName("amountBudgeted")] long? AmountBudgeted = null,
    [property: JsonPropertyName("monthlyFillDays")] List<int>? MonthlyFillDays = null,
    [property: JsonPropertyName("weeklyFillDay")] int? WeeklyFillDay = null,
    [property: JsonPropertyName("completedAt")] string? CompletedAt = null,
    [property: JsonPropertyName("targetAmount")] long? TargetAmount = null
);

public record CreateTransactionRequest(
    [property: JsonPropertyName("amount")] long Amount,
    [property: JsonPropertyName("date")] string Date,
    [property: JsonPropertyName("merchant")] string Merchant,
    [property: JsonPropertyName("note")] string? Note = null,
    [property: JsonPropertyName("checkNumber")] string? CheckNumber = null,
    [property: JsonPropertyName("allocations")] List<TransactionAllocation>? Allocations = null,
    [property: JsonPropertyName("accountId")] string? AccountId = null
);

public record UpdateTransactionRequest(
    [property: JsonPropertyName("amount")] long Amount,
    [property: JsonPropertyName("date")] string Date,
    [property: JsonPropertyName("merchant")] string Merchant,
    [property: JsonPropertyName("note")] string? Note = null,
    [property: JsonPropertyName("checkNumber")] string? CheckNumber = null,
    [property: JsonPropertyName("allocations")] List<TransactionAllocation>? Allocations = null,
    [property: JsonPropertyName("accountId")] string? AccountId = null
);
