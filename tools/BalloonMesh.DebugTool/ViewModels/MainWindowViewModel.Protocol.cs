using System.Collections.ObjectModel;
using BalloonMesh.DebugTool.Core;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BalloonMesh.DebugTool.ViewModels;

public partial class MainWindowViewModel
{
    public IReadOnlyList<string> ProtocolPresets { get; } =
        ProtocolInspector.KnownOps.Select(op => op.Name).ToArray();

    public ObservableCollection<ProtocolFieldRow> ProtocolFields { get; } = [];

    [ObservableProperty]
    private string _protocolPreset = "LED_SET";

    [ObservableProperty]
    private string _protocolOpcode = "0xC102E5";

    [ObservableProperty]
    private string _protocolPayload = string.Empty;

    [ObservableProperty]
    private string _protocolSummary = "Enter an opcode and payload, then press Decode.";

    [ObservableProperty]
    private string _protocolWarnings = string.Empty;

    [ObservableProperty]
    private bool _expandShortOpcode = true;

    [RelayCommand]
    private void BuildProtocolMessage()
    {
        try
        {
            EncodedMessage WithColor(Func<byte, byte, byte, EncodedMessage> build)
            {
                var (r, g, b) = ParseColor();
                return build(r, g, b);
            }

            var encoded = ProtocolPreset switch
            {
                "LED_SET" => WithColor((r, g, b) => ProtocolInspector.EncodeLedSet(
                    ParseUtcOr(ExecuteAt, "Execute at", DateTimeOffset.UtcNow.AddSeconds(10)),
                    r, g, b)),
                "LED_CANCEL" => ProtocolInspector.EncodeLedCancel(
                    ParseUtcOr(ExecuteAt, "Execute at", DateTimeOffset.UtcNow)),
                "LED_RESET_ALL" => ProtocolInspector.EncodeLedReset(
                    ParseUtcOr(ResetCutoff, "Reset cutoff", DateTimeOffset.UtcNow)),
                "LED_SET_NOW" => WithColor((r, g, b) => ProtocolInspector.EncodeLedSetNow(
                    ParseUtcOr(ExecuteAt, "Issued at", DateTimeOffset.UtcNow), r, g, b)),
                "LED_RESET_AND_SET" => WithColor((r, g, b) =>
                    ProtocolInspector.EncodeLedResetAndSet(
                        ParseUtcOr(ResetCutoff, "Reset cutoff", DateTimeOffset.UtcNow),
                        r, g, b)),
                "FTM_REPORT" => ProtocolInspector.EncodeFtmReport(
                    ParseByteValue(FtmAnchor, "Anchor id"),
                    ParseUShortValue(FtmDistanceCm, "Distance cm"),
                    string.IsNullOrWhiteSpace(FtmTimestamp)
                        ? ProtocolInspector.NowTimestamp32()
                        : ParseUInt32Value(FtmTimestamp, "FTM timestamp"),
                    ParseByteValue(FtmBatchId, "Batch id")),
                "QUEUE_STATUS" => ProtocolInspector.EncodeQueueStatus(
                    QueueKind switch
                    {
                        "NearFull" => ProtocolInspector.QueueStatusNearFull,
                        "Recovered" => ProtocolInspector.QueueStatusRecovered,
                        _ => throw new ArgumentException($"Unknown queue kind '{QueueKind}'.")
                    },
                    ParseUShortValue(QueueCount, "Queue count")),
                "NEIGHBOR_REPORT" => ProtocolInspector.EncodeNeighborReport(
                    ParseByteValue(NeighborBatchId, "Neighbor batch id"),
                    ParseByteValue(NeighborPacketIndex, "Neighbor packet index"),
                    BuildNeighborSamples()),
                "NEIGHBOR_REQUEST" or "NEIGHBOR_PROBE" or "TIME_REQUEST" =>
                    ProtocolInspector.EncodeRaw(OpFor(ProtocolPreset), string.Empty),
                "TIME_REPORT" => ProtocolInspector.EncodeTimeReport(
                    ParseUtcOr(ExecuteAt, "Time report", DateTimeOffset.UtcNow)),
                _ => throw new ArgumentException($"Unknown protocol preset '{ProtocolPreset}'.")
            };
            ApplyEncoded(encoded);
        }
        catch (Exception exception)
        {
            ProtocolSummary = exception.Message;
            ProtocolWarnings = exception.Message;
            AddLog("error", $"Protocol build failed: {exception.Message}");
        }
    }

    [RelayCommand]
    private void DecodeProtocolMessage()
    {
        try
        {
            var decoded = ProtocolInspector.Decode(ParseOpcode(ProtocolOpcode), ProtocolPayload);
            ProtocolOpcode = $"0x{decoded.Opcode:X6}";
            ProtocolSummary = decoded.Summary;
            ProtocolWarnings = decoded.HasWarnings
                ? string.Join(Environment.NewLine, decoded.Warnings)
                : string.Empty;
            ProtocolFields.Clear();
            foreach (var field in decoded.Fields)
            {
                ProtocolFields.Add(new ProtocolFieldRow(field.Name, field.Value));
            }
            AddDecodedEntry(DateTimeOffset.Now, "DECODE", null, decoded, string.Empty);
            AddLog("info", $"Decoded {ProtocolInspector.FormatDecoded(decoded)}");
            if (decoded.HasWarnings)
            {
                AddLog("warn", string.Join("; ", decoded.Warnings));
            }
        }
        catch (Exception exception)
        {
            ProtocolSummary = exception.Message;
            ProtocolWarnings = exception.Message;
            ProtocolFields.Clear();
            AddLog("error", $"Protocol decode failed: {exception.Message}");
        }
    }

    public void OpenInProtocol(GatewayCommandInfo command)
    {
        ProtocolOpcode = $"0x{command.Opcode:X6}";
        ProtocolPayload = command.Payload;
        ExpandShortOpcode = false;
        DecodeProtocolMessage();
        SelectedTabIndex = 3;
    }

    private void ApplyEncoded(EncodedMessage encoded)
    {
        ProtocolOpcode = encoded.OpcodeHex;
        ProtocolPayload = encoded.PayloadHex;
        ProtocolSummary = $"Built {encoded.Name} ({encoded.PayloadHex.Length / 2} bytes).";
        ProtocolWarnings = string.Empty;
        AddLog("info",
            $"Built {encoded.Name} opcode={encoded.OpcodeHex} payload={encoded.PayloadHex}");
    }

    private static byte OpFor(string name) =>
        ProtocolInspector.KnownOps.First(op => op.Name == name).Op;
}
