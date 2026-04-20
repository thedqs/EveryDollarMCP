using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

// Ensure Playwright browsers are installed on first run.
// Redirect stdout to stderr during install so it doesn't corrupt the MCP JSON-RPC protocol.
var originalOut = Console.Out;
Console.SetOut(Console.Error);
try
{
    Microsoft.Playwright.Program.Main(["install", "chromium"]);
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Playwright install warning: {ex.Message}");
}
finally
{
    Console.SetOut(originalOut);
}

var builder = Host.CreateApplicationBuilder(args);
builder.Logging.AddConsole(consoleLogOptions =>
{
    consoleLogOptions.LogToStandardErrorThreshold = LogLevel.Trace;
});

builder.Services.AddSingleton<AskRamseyApiClient>();
builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly();

await builder.Build().RunAsync();
