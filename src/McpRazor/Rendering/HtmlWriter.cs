using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Components.RenderTree;

namespace McpRazor.Rendering;

/// <summary>
/// Turns the render tree of a <see cref="McpRazorRenderer"/> into HTML.
/// Event handlers become <c>data-mcp-on-{event}="{handlerId}"</c> attributes that the runtime in the
/// MCP App turns back into <c>mcp_razor_event</c> calls.
/// </summary>
internal static class HtmlWriter
{
    internal const string EventAttributePrefix = "data-mcp-on-";
    internal const string PreventDefaultAttributePrefix = "data-mcp-pd-";
    internal const string StopPropagationAttributePrefix = "data-mcp-sp-";

    private const string InternalPreventDefault = "__internal_preventDefault_on";
    private const string InternalStopPropagation = "__internal_stopPropagation_on";

    private static readonly HashSet<string> VoidElements = new(StringComparer.OrdinalIgnoreCase)
    {
        "area", "base", "br", "col", "embed", "hr", "img", "input", "link", "meta", "param", "source", "track", "wbr",
    };

    public static RenderedHtml Write(McpRazorRenderer renderer, int rootComponentId)
    {
        var context = new WriterContext(renderer);
        if (rootComponentId >= 0)
        {
            WriteComponent(context, rootComponentId);
        }

        return new RenderedHtml(context.Output.ToString(), context.EventHandlerIds);
    }

    private static void WriteComponent(WriterContext context, int componentId)
    {
        var frames = context.Renderer.GetFrames(componentId);
        WriteFrames(context, frames.Array, 0, frames.Count);
    }

    private static int WriteFrames(WriterContext context, RenderTreeFrame[] frames, int position, int end)
    {
        while (position < end)
        {
            position = WriteFrame(context, frames, position);
        }

        return position;
    }

    private static int WriteFrame(WriterContext context, RenderTreeFrame[] frames, int position)
    {
        ref var frame = ref frames[position];
        switch (frame.FrameType)
        {
            case RenderTreeFrameType.Element:
                return WriteElement(context, frames, position);

            case RenderTreeFrameType.Text:
                AppendEscaped(context.Output, frame.TextContent);
                return position + 1;

            case RenderTreeFrameType.Markup:
                context.Output.Append(frame.MarkupContent);
                return position + 1;

            case RenderTreeFrameType.Component:
                WriteComponent(context, frame.ComponentId);
                return position + frame.ComponentSubtreeLength;

            case RenderTreeFrameType.Region:
                return WriteFrames(context, frames, position + 1, position + frame.RegionSubtreeLength);

            default:
                // Attributes are written with their element; reference captures, render modes and named events have no HTML.
                return position + 1;
        }
    }

    private static int WriteElement(WriterContext context, RenderTreeFrame[] frames, int position)
    {
        ref var element = ref frames[position];
        var name = element.ElementName;
        var end = position + element.ElementSubtreeLength;
        var output = context.Output;

        output.Append('<').Append(name);

        string? textareaValue = null;
        string? selectValue = null;
        string? optionValue = null;
        var optionSelected = false;

        var cursor = position + 1;
        for (; cursor < end && frames[cursor].FrameType == RenderTreeFrameType.Attribute; cursor++)
        {
            ref var attribute = ref frames[cursor];
            var attributeName = attribute.AttributeName;
            var value = attribute.AttributeValue;

            if (attribute.AttributeEventHandlerId != 0)
            {
                var eventName = attributeName.StartsWith("on", StringComparison.Ordinal) ? attributeName[2..] : attributeName;
                output.Append(' ').Append(EventAttributePrefix).Append(eventName.ToLowerInvariant())
                      .Append("=\"").Append(attribute.AttributeEventHandlerId.ToString(CultureInfo.InvariantCulture)).Append('"');
                context.EventHandlerIds.Add(attribute.AttributeEventHandlerId);
                continue;
            }

            if (attributeName.StartsWith(InternalPreventDefault, StringComparison.Ordinal))
            {
                if (value is true)
                {
                    output.Append(' ').Append(PreventDefaultAttributePrefix).Append(attributeName[InternalPreventDefault.Length..]);
                }

                continue;
            }

            if (attributeName.StartsWith(InternalStopPropagation, StringComparison.Ordinal))
            {
                if (value is true)
                {
                    output.Append(' ').Append(StopPropagationAttributePrefix).Append(attributeName[InternalStopPropagation.Length..]);
                }

                continue;
            }

            if (attributeName.StartsWith("__internal_", StringComparison.Ordinal))
            {
                continue;
            }

            // Blazor uses a "value" attribute on <textarea> and <select>; HTML needs content and "selected".
            if (attributeName == "value" && string.Equals(name, "textarea", StringComparison.OrdinalIgnoreCase))
            {
                textareaValue = FormatValue(value);
                continue;
            }

            if (attributeName == "value" && string.Equals(name, "select", StringComparison.OrdinalIgnoreCase))
            {
                selectValue = FormatValue(value);
                continue;
            }

            if (attributeName == "value" && string.Equals(name, "option", StringComparison.OrdinalIgnoreCase))
            {
                optionValue = FormatValue(value);
            }

            if (attributeName == "selected" && value is true)
            {
                optionSelected = true;
            }

            WriteAttribute(output, attributeName, value);
        }

        if (optionValue is not null && !optionSelected && context.SelectValues.TryPeek(out var currentSelect) &&
            currentSelect is not null && string.Equals(currentSelect, optionValue, StringComparison.Ordinal))
        {
            output.Append(" selected");
        }

        output.Append('>');

        if (VoidElements.Contains(name))
        {
            return end;
        }

        if (textareaValue is not null)
        {
            AppendEscaped(output, textareaValue);
        }
        else
        {
            var isSelect = string.Equals(name, "select", StringComparison.OrdinalIgnoreCase);
            if (isSelect)
            {
                context.SelectValues.Push(selectValue);
            }

            WriteFrames(context, frames, cursor, end);

            if (isSelect)
            {
                context.SelectValues.Pop();
            }
        }

        output.Append("</").Append(name).Append('>');
        return end;
    }

    private static void WriteAttribute(StringBuilder output, string name, object? value)
    {
        switch (value)
        {
            case null:
            case false:
                return;
            case true:
                output.Append(' ').Append(name);
                return;
            default:
                output.Append(' ').Append(name).Append("=\"");
                AppendEscaped(output, FormatValue(value) ?? string.Empty);
                output.Append('"');
                return;
        }
    }

    // Escapes the characters that are significant in HTML text and in double-quoted attribute values.
    // Everything else, including non-ASCII text, is written as is: the document is UTF-8.
    private static void AppendEscaped(StringBuilder output, string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        var start = 0;
        for (var i = 0; i < text.Length; i++)
        {
            var replacement = text[i] switch
            {
                '&' => "&amp;",
                '<' => "&lt;",
                '>' => "&gt;",
                '"' => "&quot;",
                _ => null,
            };

            if (replacement is not null)
            {
                output.Append(text, start, i - start).Append(replacement);
                start = i + 1;
            }
        }

        output.Append(text, start, text.Length - start);
    }

    private static string? FormatValue(object? value) => value switch
    {
        null => null,
        string text => text,
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString(),
    };

    private sealed class WriterContext(McpRazorRenderer renderer)
    {
        public McpRazorRenderer Renderer { get; } = renderer;
        public StringBuilder Output { get; } = new();
        public HashSet<ulong> EventHandlerIds { get; } = [];
        public Stack<string?> SelectValues { get; } = new();
    }
}
