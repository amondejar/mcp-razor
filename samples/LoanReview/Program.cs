using LoanReview;
using ModelContextProtocol.Protocol;

// Two ways to run the sample:
//   dotnet run                → HTTP server on http://localhost:5180/mcp, with the preview host at /preview
//   dotnet run -- --stdio     → stdio server, for Claude Desktop or any local MCP client
var useStdio = args.Contains("--stdio");

if (useStdio)
{
    var builder = Host.CreateApplicationBuilder(args);
    builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);
    builder.Services.AddSingleton<LoanService>();
    AddMcp(builder.Services).WithStdioServerTransport();
    await builder.Build().RunAsync();
}
else
{
    var builder = WebApplication.CreateBuilder(args);
    builder.Services.AddSingleton<LoanService>();
    AddMcp(builder.Services).WithHttpTransport();

    var app = builder.Build();
    app.MapMcp("/mcp");
    if (app.Environment.IsDevelopment())
    {
        app.MapMcpRazorPreview("/preview", mcpPath: "/mcp");
        app.MapGet("/", () => Results.Redirect("/preview"));
    }

    await app.RunAsync();
}

static IMcpServerBuilder AddMcp(IServiceCollection services) =>
    services
        .AddMcpServer(options => options.ServerInfo = new Implementation { Name = "loan-review", Version = "0.1.0" })
        .WithTools<LoanTools>()
        .WithRazorApps(apps => apps.AddAppsFromAssembly(typeof(LoanTools).Assembly));
