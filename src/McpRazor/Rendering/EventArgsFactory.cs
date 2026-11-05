using System.Text.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace McpRazor.Rendering;

/// <summary>Builds the Blazor <see cref="EventArgs"/> for a DOM event reported by the MCP App runtime.</summary>
internal static class EventArgsFactory
{
    public static EventArgs Create(string eventType, JsonElement? payload)
    {
        var data = payload is { ValueKind: JsonValueKind.Object } p ? p : default;

        return eventType switch
        {
            "change" or "input" => new ChangeEventArgs { Value = ReadValue(data) },
            "click" or "dblclick" or "mousedown" or "mouseup" or "contextmenu" or "auxclick" => new MouseEventArgs
            {
                Type = eventType,
                Detail = GetLong(data, "detail") ?? 1,
                Button = GetLong(data, "button") ?? 0,
                ShiftKey = GetBool(data, "shiftKey"),
                CtrlKey = GetBool(data, "ctrlKey"),
                AltKey = GetBool(data, "altKey"),
                MetaKey = GetBool(data, "metaKey"),
            },
            "keydown" or "keyup" or "keypress" => new KeyboardEventArgs
            {
                Type = eventType,
                Key = GetString(data, "key") ?? string.Empty,
                Code = GetString(data, "code") ?? string.Empty,
                Repeat = GetBool(data, "repeat"),
                ShiftKey = GetBool(data, "shiftKey"),
                CtrlKey = GetBool(data, "ctrlKey"),
                AltKey = GetBool(data, "altKey"),
                MetaKey = GetBool(data, "metaKey"),
            },
            "focus" or "blur" or "focusin" or "focusout" => new FocusEventArgs { Type = eventType },
            _ => EventArgs.Empty,
        };
    }

    private static object? ReadValue(JsonElement data)
    {
        if (data.ValueKind != JsonValueKind.Object || !data.TryGetProperty("value", out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            JsonValueKind.Array => value.EnumerateArray().Select(item => item.ValueKind == JsonValueKind.String ? item.GetString() : item.GetRawText()).ToArray(),
            _ => null,
        };
    }

    private static string? GetString(JsonElement data, string name) =>
        data.ValueKind == JsonValueKind.Object && data.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static bool GetBool(JsonElement data, string name) =>
        data.ValueKind == JsonValueKind.Object && data.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;

    private static long? GetLong(JsonElement data, string name) =>
        data.ValueKind == JsonValueKind.Object && data.TryGetProperty(name, out var value) && value.TryGetInt64(out var number)
            ? number
            : null;
}
