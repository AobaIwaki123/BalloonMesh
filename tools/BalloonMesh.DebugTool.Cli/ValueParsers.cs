using System.Globalization;
using BalloonMesh.DebugTool.Core;

namespace BalloonMesh.DebugTool.Cli;

/// <summary>Parsers shared by the CLI commands and the <c>--event</c> grammar.</summary>
public static class ValueParsers
{
    public static byte ParseByte(string text, string label) =>
        (byte)RequireInteger(text, label, 0, byte.MaxValue);

    public static ushort ParseUShort(string text, string label) =>
        (ushort)RequireInteger(text, label, 0, ushort.MaxValue);

    public static uint ParseUInt32(string text, string label) =>
        (uint)RequireInteger(text, label, 0, uint.MaxValue);

    public static sbyte ParseSByte(string text, string label) =>
        (sbyte)RequireInteger(text, label, sbyte.MinValue, sbyte.MaxValue);

    public static int ParseInt32(string text, string label, int min, int max) =>
        (int)RequireInteger(text, label, min, max);

    /// <summary>Parses <c>#RRGGBB</c>, <c>RRGGBB</c> or <c>r,g,b</c>.</summary>
    public static (byte R, byte G, byte B) ParseColor(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.Length == 0)
        {
            throw new FormatException("Color is empty. Use #RRGGBB or r,g,b.");
        }
        if (trimmed.Contains(','))
        {
            var parts = trimmed.Split(',', StringSplitOptions.TrimEntries);
            if (parts.Length != 3)
            {
                throw new FormatException("Color must be #RRGGBB or three comma-separated values r,g,b.");
            }
            return (ParseByte(parts[0], "red"), ParseByte(parts[1], "green"), ParseByte(parts[2], "blue"));
        }
        var hex = trimmed.StartsWith('#') ? trimmed[1..] : trimmed;
        if (hex.Length != 6)
        {
            throw new FormatException("Hex color must have exactly six digits (for example #FF8000).");
        }
        try
        {
            var bytes = Convert.FromHexString(hex);
            return (bytes[0], bytes[1], bytes[2]);
        }
        catch (FormatException exception)
        {
            throw new FormatException("Hex color contains non-hexadecimal characters.", exception);
        }
    }

    /// <summary>
    /// Parses an ISO-8601 timestamp, <c>now</c>, or a relative offset such as
    /// <c>+10s</c>, <c>+500ms</c>, <c>+2m</c> and <c>+1h</c>.
    /// </summary>
    public static DateTimeOffset ParseTime(string text, string label)
    {
        var trimmed = text.Trim();
        if (trimmed.Equals("now", StringComparison.OrdinalIgnoreCase))
        {
            return DateTimeOffset.UtcNow;
        }
        if (trimmed.StartsWith('+'))
        {
            return DateTimeOffset.UtcNow.Add(ParseDuration(trimmed[1..], label));
        }
        if (!DateTimeOffset.TryParse(trimmed, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var value))
        {
            throw new FormatException(
                $"{label} '{text}' is not a valid ISO-8601 time or relative offset (for example +10s).");
        }
        return value;
    }

    public static DateTimeOffset ParseTimeOr(string? text, DateTimeOffset fallback, string label) =>
        string.IsNullOrWhiteSpace(text) ? fallback : ParseTime(text, label);

    public static TimeSpan ParseDuration(string text, string label)
    {
        var trimmed = text.Trim();
        var suffix = "s";
        foreach (var candidate in new[] { "ms", "s", "m", "h", "d" })
        {
            if (trimmed.EndsWith(candidate, StringComparison.OrdinalIgnoreCase))
            {
                suffix = candidate;
                trimmed = trimmed[..^candidate.Length];
                break;
            }
        }
        if (!double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var amount) ||
            amount < 0)
        {
            throw new FormatException($"{label} duration '{text}' is not valid. Use ms, s, m, h or d.");
        }
        return suffix switch
        {
            "ms" => TimeSpan.FromMilliseconds(amount),
            "s" => TimeSpan.FromSeconds(amount),
            "m" => TimeSpan.FromMinutes(amount),
            "h" => TimeSpan.FromHours(amount),
            _ => TimeSpan.FromDays(amount)
        };
    }

    /// <summary>Parses <c>0x0102@-45</c> into a neighbor entry.</summary>
    public static NeighborSample ParseNeighbor(string text)
    {
        var separator = text.IndexOf('@');
        if (separator <= 0 || separator == text.Length - 1)
        {
            throw new FormatException($"Neighbor '{text}' must look like 0x0102@-45.");
        }
        return new NeighborSample(
            ParseUShort(text[..separator], "neighbor address"),
            ParseSByte(text[(separator + 1)..], "neighbor rssi"));
    }

    public static IReadOnlyList<NeighborSample> ParseNeighbors(
        IReadOnlyList<string> entries, int maximum = 2)
    {
        if (entries.Count == 0)
        {
            throw new FormatException("At least one '--neighbor 0x0102@-45' entry is required.");
        }
        if (entries.Count > maximum)
        {
            throw new FormatException($"At most {maximum} neighbor entries fit in one packet.");
        }
        return entries.Select(ParseNeighbor).ToArray();
    }

    private static long RequireInteger(string text, string label, long min, long max)
    {
        var trimmed = text.Trim();
        long value;
        if (trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            if (!ulong.TryParse(trimmed[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture,
                    out var unsigned) || unsigned > long.MaxValue)
            {
                throw new FormatException($"{label} '{text}' is not a hexadecimal number.");
            }
            value = (long)unsigned;
        }
        else if (!long.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture,
                     out value))
        {
            throw new FormatException($"{label} '{text}' is not a number.");
        }
        if (value < min || value > max)
        {
            throw new FormatException($"{label} '{text}' is out of range ({min}..{max}).");
        }
        return value;
    }
}
