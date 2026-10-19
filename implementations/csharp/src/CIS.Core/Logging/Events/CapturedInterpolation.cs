using System.Globalization;
using System.Text;
using CIS.Text;

namespace CIS.Logging.Events;

internal sealed class CapturedInterpolation
{
    private readonly InterpolationPart[] parts;
    internal CapturedInterpolation(InterpolationPart[] parts, bool truncated, string? errorCode)
    {
        this.parts = parts; IsTruncated = truncated; ErrorCode = errorCode;
        var template = new StringBuilder();
        int argument = 0;
        foreach (var part in parts)
        {
            if (part.Literal)
            { template.Append(((string)part.Value!).Replace("{", "{{", StringComparison.Ordinal).Replace("}", "}}", StringComparison.Ordinal)); }
            else
            {
                template.Append('{').Append((argument++).ToString(CultureInfo.InvariantCulture));
                if (part.Alignment != 0) { template.Append(',').Append(part.Alignment.ToString(CultureInfo.InvariantCulture)); }
                if (part.Format is { } format) { template.Append(':').Append(format); }
                template.Append('}');
            }
        }
        Template = UnicodeScalarText.Prefix(template.ToString(), EventProjection.MaximumPayloadScalars, out bool clipped);
        IsTruncated |= clipped;
    }
    internal string Template { get; }
    internal bool IsTruncated { get; }
    internal string? ErrorCode { get; }
    internal CapturedInterpolation WithError(string code) => new(parts, IsTruncated, code);

    internal CapturedEventFacts Render(CapturedEventFacts facts)
    {
        var output = new StringBuilder();
        int remaining = EventProjection.MaximumPayloadScalars;
        bool truncated = facts.IsTruncated;
        Span<char> scratch = stackalloc char[EventProjection.MaximumPayloadScalars * 2];
        foreach (var part in parts)
        {
            if (remaining == 0)
            {
                if (!part.Omitted && part.Alignment == 0 && part.Value is null or "") { continue; }
                truncated = true; break;
            }
            if (part.Omitted) { truncated = true; continue; }
            string text;
            if (part.Value is null) { text = string.Empty; }
            else if (part.Value is string literal) { text = literal; }
            else if (part.Value is ISpanFormattable scalar)
            {
                if (!scalar.TryFormat(scratch, out int written, part.Format, CultureInfo.InvariantCulture))
                { truncated = true; continue; }
                text = scratch[..written].ToString();
            }
            else { text = part.Value.ToString()!; } // Only owned built-in scalars can reach this branch.
            int padding = part.Literal ? 0 : (int)Math.Min(int.MaxValue, Math.Max(0, Math.Abs((long)part.Alignment) - text.Length));
            if (part.Alignment > 0) { Pad(output, padding, ref remaining, ref truncated); }
            string prefix = UnicodeScalarText.Prefix(text, remaining, out bool clipped);
            output.Append(prefix); remaining -= prefix.EnumerateRunes().Count(); truncated |= clipped;
            if (part.Alignment < 0) { Pad(output, padding, ref remaining, ref truncated); }
        }
        return new(facts.TimestampUtc, facts.TraceId, facts.SpanId, facts.Status, facts.EventName,
            output.ToString(), facts.MessageTemplate, facts.OperationId, facts.Service, facts.Environment,
            facts.Version, facts.Component, facts.Resource, facts.Attributes, facts.Exception, truncated,
            facts.HasProviderEventId, facts.ExplicitAttributes);

    }

    private static void Pad(StringBuilder output, int count, ref int remaining, ref bool truncated)
    {
        int retained = Math.Min(count, remaining);
        output.Append(' ', retained); remaining -= retained; truncated |= retained != count;
    }
}
