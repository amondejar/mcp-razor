using McpRazor.Runtime;
using McpRazor.Tests.Components;

namespace McpRazor.Tests;

public sealed class OptionsAndShellTests
{
    [Fact]
    public void Discovers_apps_marked_with_the_attribute()
    {
        var options = new McpRazorOptions().AddAppsFromAssembly(typeof(OrderApp).Assembly);

        var app = Assert.Single(options.Apps);
        Assert.Equal("ui://test/order", app.Uri);
        Assert.Equal("Order", app.Name);
        Assert.Equal("Test order app", app.Description);
        Assert.True(app.PrefersBorder);
        Assert.Equal(typeof(OrderData), app.DataType);
    }

    [Fact]
    public void Validates_registrations()
    {
        var options = new McpRazorOptions();

        Assert.Throws<ArgumentException>(() => options.AddApp<Counter>("https://example.com/app"));
        Assert.Throws<ArgumentException>(() => options.AddApp<Counter>("ui://"));
        Assert.Throws<ArgumentException>(() => options.AddApp(typeof(string), "ui://test/string"));

        options.AddApp<Counter>("ui://test/counter");
        Assert.Throws<InvalidOperationException>(() => options.AddApp<Child>("ui://test/counter"));
        Assert.Null(options.Apps.Single().DataType);
    }

    [Fact]
    public void Builds_a_static_shell_with_the_runtime()
    {
        var options = new McpRazorOptions().AddApp<Counter>("ui://test/a\"b", app => app.Stylesheet = "p { color: red } </style><script>alert(1)</script>");
        var shell = McpAppShell.Build(options.Apps.Single());

        Assert.StartsWith("<!DOCTYPE html>", shell);
        Assert.Contains("<div id=\"mcp-root\" data-mcp-app=\"ui://test/a&quot;b\">", shell);
        Assert.Contains("ui/initialize", shell);
        Assert.Contains("mcp_razor_event", shell);
        Assert.Contains("<\\/style><script>alert(1)", shell);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(shell, "</style>\\s*</head>"));
        Assert.Equal(shell, McpAppShell.Build(options.Apps.Single()));
    }
}
