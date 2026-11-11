using System.Globalization;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BalloonMesh.DebugTool.Core;

/// <summary>
/// JSON messages spoken by the gateway WebSocket/HTTP bridge.  This is kept
/// separate from <see cref="ProtocolInspector"/> because it covers the server
/// transport envelope, not the BLE Mesh vendor payload.
/// </summary>
public static class GatewayProtocolCodec
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static string CreateHello(GatewayEmulatorOptions options)
    {
        options.Validate();
        return JsonSerializer.Serialize(new
        {
            type = "hello",
            gatewayId = options.GatewayId,
            name = $"debugtool-{options.GatewayId}",
            firmware = options.Firmware,
            token = options.Token,
            provisioned = options.Provisioned,
            spoolPending = options.SpoolPending
        }, Json);
    }

    public static string CreateUplink(GatewayEventInfo message)
    {
        var payload = ProtocolInspector.NormalizePayloadHex(message.Payload);
        return JsonSerializer.Serialize(new
        {
            type = "mesh",
            src = message.Src,
            dst = message.Dst,
            opcode = message.Opcode,
            payload,
            rssi = message.Rssi,
            ttl = message.Ttl,
            receivedMs = message.ReceivedMs
        }, Json);
    }

    /// <summary>
    /// Serializes the command object returned by the HTTP long-poll endpoint.
    /// The WebSocket transport keeps the exact received frame instead.
    /// </summary>
    public static string CreateCommandJson(GatewayCommandInfo command) =>
        JsonSerializer.Serialize(new
        {
            type = "mesh",
            dst = command.Dst,
            opcode = command.Opcode,
            payload = command.Payload,
            ttl = command.Ttl
        }, Json);

    public static bool TryParseDownlink(string json, out GatewayCommandInfo command,
        out string? error)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("type", out var type) ||
                !string.Equals(type.GetString(), "mesh", StringComparison.OrdinalIgnoreCase))
            {
                command = default!;
                error = "not a mesh message";
                return false;
            }
            if (!TryReadUInt16(root, "dst", out var dst))
            {
                command = default!;
                error = "missing or invalid dst";
                return false;
            }
            if (!TryReadUInt32(root, "opcode", out var opcode))
            {
                command = default!;
                error = "missing or invalid opcode";
                return false;
            }
            var payloadText = root.TryGetProperty("payload", out var payloadElement) &&
                              payloadElement.ValueKind == JsonValueKind.String
                ? payloadElement.GetString()
                : null;
            string payload;
            try
            {
                payload = ProtocolInspector.NormalizePayloadHex(payloadText);
            }
            catch (FormatException exception)
            {
                command = default!;
                error = $"invalid payload: {exception.Message}";
                return false;
            }
            var ttl = root.TryGetProperty("ttl", out var ttlElement) &&
                      ttlElement.TryGetInt32(out var parsedTtl)
                ? parsedTtl
                : 0;
            command = new GatewayCommandInfo(dst, opcode, payload, ttl);
            error = null;
            return true;
        }
        catch (JsonException exception)
        {
            command = default!;
            error = $"malformed JSON: {exception.Message}";
            return false;
        }
        catch (InvalidOperationException exception)
        {
            command = default!;
            error = $"malformed message: {exception.Message}";
            return false;
        }
    }

    /// <summary>Maps an HTTP(S) server URL to the gateway WebSocket endpoint.</summary>
    public static Uri BuildWebSocketUri(Uri serverUri)
    {
        var normalized = DebugApiClient.NormalizeBaseUri(serverUri);
        var endpoint = new Uri(normalized, "ws/gateway");
        var builder = new UriBuilder(endpoint)
        {
            Scheme = endpoint.Scheme == "https" ? "wss" : "ws"
        };
        return builder.Uri;
    }

    private static bool TryReadUInt16(JsonElement root, string property, out ushort value)
    {
        value = 0;
        if (!root.TryGetProperty(property, out var element))
        {
            return false;
        }
        if (element.ValueKind == JsonValueKind.Number &&
            element.TryGetUInt16(out value))
        {
            return true;
        }
        if (element.ValueKind == JsonValueKind.String)
        {
            return TryParseNumber(element.GetString(), out value);
        }
        return false;
    }

    private static bool TryReadUInt32(JsonElement root, string property, out uint value)
    {
        value = 0;
        if (!root.TryGetProperty(property, out var element))
        {
            return false;
        }
        if (element.ValueKind == JsonValueKind.Number &&
            element.TryGetUInt32(out value))
        {
            return true;
        }
        if (element.ValueKind == JsonValueKind.String)
        {
            return TryParseNumber(element.GetString(), out value);
        }
        return false;
    }

    private static bool TryParseNumber<T>(string? text, out T value)
        where T : struct, INumber<T>
    {
        value = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }
        text = text.Trim();
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            return T.TryParse(text[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture,
                out value);
        }
        return T.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }
}
