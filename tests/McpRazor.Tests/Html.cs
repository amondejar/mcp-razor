using System.Text.RegularExpressions;

namespace McpRazor.Tests;

/// <summary>Small helpers to read the HTML the renderer produces.</summary>
internal static partial class Html
{
    /// <summary>The opening tag of the element with the given id.</summary>
    public static string Tag(string html, string id)
    {
        var match = Regex.Match(html, $"<[a-z]+[^>]*\\bid=\"{Regex.Escape(id)}\"[^>]*>");
        Assert.True(match.Success, $"No element with id '{id}' in: {html}");
        return match.Value;
    }

    /// <summary>The event handler ID of <paramref name="eventName"/> on the element with the given id.</summary>
    public static ulong Handler(string html, string id, string eventName) => HandlerIn(Tag(html, id), eventName);

    /// <summary>The click handler of the button whose text starts with <paramref name="text"/>.</summary>
    public static ulong ButtonHandler(string html, string text)
    {
        var match = Regex.Match(html, $"<button[^>]*data-mcp-on-click=\"(\\d+)\"[^>]*>{Regex.Escape(text)}");
        Assert.True(match.Success, $"No button '{text}' in: {html}");
        return ulong.Parse(match.Groups[1].Value);
    }

    public static ulong HandlerIn(string tag, string eventName)
    {
        var match = Regex.Match(tag, $"data-mcp-on-{eventName}=\"(\\d+)\"");
        Assert.True(match.Success, $"No {eventName} handler in: {tag}");
        return ulong.Parse(match.Groups[1].Value);
    }

    /// <summary>The text content of the element with the given id.</summary>
    public static string Text(string html, string id)
    {
        var match = Regex.Match(html, $"<([a-z]+)[^>]*\\bid=\"{Regex.Escape(id)}\"[^>]*>(.*?)</\\1>", RegexOptions.Singleline);
        Assert.True(match.Success, $"No element with id '{id}' in: {html}");
        return match.Groups[2].Value;
    }
}
