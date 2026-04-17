# EveryDollar MCP Server

A [Model Context Protocol (MCP)](https://modelcontextprotocol.io/) server that provides AI assistants with full access to the [EveryDollar](https://www.everydollar.com/) budgeting app. Built with C# / .NET 10 and [Microsoft Playwright](https://playwright.dev/dotnet/) for browser-based authentication.

## What It Does

This MCP server lets AI assistants (like GitHub Copilot, Claude, etc.) manage your EveryDollar budget through natural language. Instead of clicking through the web app, you can ask your AI to check your budget, categorize transactions, transfer funds between categories, and more.

## Available Tools (32)

### Authentication
| Tool | Description |
|------|-------------|
| `login` | Opens a browser window for interactive login. Auto-detects Edge or Chrome. Supports persistent browser profiles so saved credentials auto-fill. |
| `set_session_cookie` | Manual authentication via CSRF token and session cookie from browser DevTools. |

### Budget Reading
| Tool | Description |
|------|-------------|
| `get_budget` | Get the full budget for a specific month with all groups, line items, and amounts. |
| `list_budgets` | List all budgets that exist, organized by year and month. |
| `get_budget_item_history` | Get spending history for a budget line item across previous months. |

### Transactions
| Tool | Description |
|------|-------------|
| `get_transaction` | Get full details of a single transaction (note, checkNumber, bank account, etc.). |
| `get_transactions` | Get transactions within a date range. Filter by unallocated to find uncategorized ones. |
| `create_transaction` | Create a new manual transaction with optional budget category allocations. |
| `update_transaction` | Update an existing transaction's amount, date, merchant, note, or allocations. |
| `delete_transactions` | Delete one or more transactions by ID. |
| `restore_transactions` | Restore previously deleted transactions. |

### Budget Management
| Tool | Description |
|------|-------------|
| `create_budget_item` | Create a new budget line item (expense, income, goal, or debt). |
| `update_budget_item` | Update a line item's budgeted amount, fill schedule, target amount, or completion status. |
| `update_buffer` | Update the buffer (checking account cash at month start for paycheck planning). |
| `delete_budget_item` | Delete a budget line item. |
| `reorder_budget_items` | Reorder items within a budget group. |
| `create_budget_group` | Create a new budget group (custom spending category section). |
| `delete_budget_group` | Delete an empty budget group. |
| `reorder_budget_groups` | Reorder budget groups. |
| `clone_budget` | Clone a budget to create a new one for a different month. |
| `reset_budget` | Reset a budget to its default state (clears all amounts). |

### Fund & Item Operations
| Tool | Description |
|------|-------------|
| `convert_to_fund` | Convert a regular budget item into a sinking fund (savings fund with a target). |
| `convert_from_fund` | Convert a sinking fund back to a regular expense item, preserving transactions. |
| `move_budget_item` | Move a budget item to a different group, preserving transactions. |
| `transfer_fund` | Transfer money between budget items via paired debit/credit transactions. |

### Financial Overview
| Tool | Description |
|------|-------------|
| `get_debt_snowball` | Get debt snowball info (balances, interest rates, payment amounts). |
| `get_goals` | Get savings goals with target amounts and balances. |
| `get_insights` | Get spending trends and patterns for a date range (max 24-month span). |
| `get_subscription` | Get EveryDollar subscription status. |
| `update_user_preferences` | Update app preferences (paycheck planning visibility, fill schedules, etc.). |

### Bank Accounts
| Tool | Description |
|------|-------------|
| `get_accounts` | List all linked bank accounts with balances and sync status. |
| `refresh_bank_accounts` | Trigger a refresh/sync of all linked bank accounts. |

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- An [EveryDollar](https://www.everydollar.com/) account (Ramsey+ subscription recommended for full features)
- Microsoft Edge or Google Chrome (for browser-based login)

## Build

```powershell
cd EveryDollarMCP
dotnet publish -c Release
```

The output is a self-contained single-file executable at:
```
bin\Release\net10.0\win-x64\publish\EveryDollarMCP.exe
```

The executable is ~175 MB because it bundles the .NET runtime, Playwright, and all dependencies into one file. No separate runtime installation needed.

## Setup in VS Code

1. Copy the published `EveryDollarMCP.exe` to a permanent location (e.g., `C:\MCPs\EveryDollarMCP.exe`).

2. Add to your VS Code MCP configuration (`settings.json` or `mcp.json`):

```json
{
  "servers": {
    "EveryDollar": {
      "command": "C:\\MCPs\\EveryDollarMCP.exe"
    }
  }
}
```

3. Restart VS Code or reload the MCP servers.

## First Run

On first launch, the server automatically installs Playwright's Chromium browser if not already present. This is a one-time operation.

To authenticate, ask your AI assistant to "log in to EveryDollar." A browser window will open — log in normally or let saved credentials auto-fill. The session is captured automatically. Credentials are persisted in a browser profile at `%LOCALAPPDATA%\EveryDollarMCP\browser-profile`, so subsequent logins may auto-fill.

## Project Structure

```
EveryDollarMCP/
├── Program.cs                 # Entry point, MCP server setup, Playwright auto-install
├── EveryDollarApiClient.cs    # HTTP client for the EveryDollar API
├── Models.cs                  # Data models (Budget, Transaction, etc.)
├── Tools.cs                   # MCP tool definitions (32 tools)
└── EveryDollarMCP.csproj      # Project file
```

## Key Design Decisions

- **Single-file deployment**: The entire app (runtime + Playwright + dependencies) is bundled into one `.exe` for easy distribution.
- **Browser-based login**: Uses Playwright to open a real browser window for login, capturing the session cookie automatically. No need to manually extract tokens.
- **Persistent browser profile**: Saves browser state to `%LOCALAPPDATA%\EveryDollarMCP\browser-profile` so credentials and cookies persist across sessions.
- **Amounts in cents**: All monetary amounts use the EveryDollar API's native format (cents as integers) to avoid floating-point issues.
- **Compound operations**: Complex actions like `convert_from_fund`, `move_budget_item`, and `transfer_fund` are multi-step operations that maintain data integrity by reassigning transactions.

## API Notes

- The EveryDollar API base URL is `https://www.everydollar.com/app/api`.
- The `get_insights` endpoint enforces a maximum 24-month date range.
- All amounts are in cents (e.g., `150000` = $1,500.00). Negative amounts are expenses, positive are income.
- The API uses two JSON serialization modes: one that preserves nulls (for POST/PUT) and one that strips nulls (for PATCH operations).

## Dependencies

| Package | Version | Purpose |
|---------|---------|---------|
| [Microsoft.Extensions.Hosting](https://www.nuget.org/packages/Microsoft.Extensions.Hosting) | 10.0.6 | .NET Generic Host for dependency injection and lifecycle |
| [Microsoft.Playwright](https://www.nuget.org/packages/Microsoft.Playwright) | 1.59.0 | Browser automation for interactive login |
| [ModelContextProtocol](https://www.nuget.org/packages/ModelContextProtocol) | 1.2.0 | MCP server SDK for tool registration and stdio transport |

## Resources

- [EveryDollar Web App](https://www.everydollar.com/)
- [Ramsey Solutions](https://www.ramseysolutions.com/) — Parent company
- [Model Context Protocol](https://modelcontextprotocol.io/) — MCP specification
- [MCP C# SDK](https://github.com/modelcontextprotocol/csharp-sdk) — The .NET MCP library used by this project
- [Playwright for .NET](https://playwright.dev/dotnet/) — Browser automation framework

## Disclaimer

This is an independent, personal project. It is **not affiliated with, endorsed by, or sponsored by** Ramsey Solutions or EveryDollar. "EveryDollar" is a trademark of Lampo Licensing, LLC.

This project interacts with the EveryDollar web API using your own authenticated session. Use at your own risk.

## License

This project is licensed under the [MIT License](LICENSE).
