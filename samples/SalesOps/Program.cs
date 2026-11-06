using ModelContextProtocol.Protocol;
using SalesOps;

// Two ways to run the sample:
//   dotnet run                → HTTP server on http://localhost:5181/mcp, with the preview host at /preview
//   dotnet run -- --stdio     → stdio server, for Claude Desktop or any local MCP client
var useStdio = args.Contains("--stdio");

if (useStdio)
{
    var builder = Host.CreateApplicationBuilder(args);
    builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);
    AddShop(builder.Services);
    AddMcp(builder.Services).WithStdioServerTransport();
    await builder.Build().RunAsync();
}
else
{
    var builder = WebApplication.CreateBuilder(args);
    AddShop(builder.Services);
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

static void AddShop(IServiceCollection services)
{
    services.AddSingleton(TimeProvider.System);
    services.AddSingleton<Catalog>();
    services.AddSingleton<SalesService>();
    services.AddSingleton<PriceChangeService>();
}

static IMcpServerBuilder AddMcp(IServiceCollection services) =>
    services
        .AddMcpServer(options => options.ServerInfo = new Implementation { Name = "sales-ops", Version = "0.1.0" })
        .WithTools<SalesTools>()
        .WithRazorApps(apps => apps.AddAppsFromAssembly(typeof(SalesTools).Assembly));
