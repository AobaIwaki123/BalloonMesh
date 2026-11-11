using System.Text.Json;

namespace BalloonMesh.DebugTool.Core;

/// <summary>
/// Non-secret UI preferences.  Gateway tokens and session cookies are
/// intentionally never written to disk.
/// </summary>
public sealed class DebugToolSettings
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public string ServerUrl { get; set; } = "http://localhost:5080";
    public string MockRole { get; set; } = "Admin";
    public string GatewayId { get; set; } = "debug-gateway";
    public string GatewayTransport { get; set; } = "WebSocket";
    public string GatewayFirmware { get; set; } = "debugtool/1.0";
    public bool Provisioned { get; set; } = true;
    public int SpoolPending { get; set; }
    public int PollWaitMs { get; set; } = 10_000;
    public string TargetKind { get; set; } = "all";
    public string Target { get; set; } = "all";
    public string ResetCutoff { get; set; } = string.Empty;
    public string InjectSource { get; set; } = "0x0101";
    public string InjectDst { get; set; } = "0xC011";
    public string FtmAnchor { get; set; } = "1";
    public string FtmDistanceCm { get; set; } = "3000";
    public string FtmBatchId { get; set; } = "1";
    public string QueueKind { get; set; } = "NearFull";
    public string QueueCount { get; set; } = "401";
    public string NeighborBatchId { get; set; } = "1";
    public string NeighborPacketIndex { get; set; } = "0";
    public string NeighborA { get; set; } = "0x0102";
    public string NeighborARssi { get; set; } = "-45";
    public string NeighborB { get; set; } = "0x0103";
    public string NeighborBRssi { get; set; } = "-60";

    public static string DefaultPath
    {
        get
        {
            var root = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            if (string.IsNullOrWhiteSpace(root))
            {
                root = Path.GetTempPath();
            }
            return Path.Combine(root, "BalloonMesh", "debugtool.json");
        }
    }

    public static DebugToolSettings Load(string? path = null)
    {
        path ??= DefaultPath;
        try
        {
            if (!File.Exists(path))
            {
                return new DebugToolSettings();
            }
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<DebugToolSettings>(json, Json)
                ?? new DebugToolSettings();
        }
        catch (Exception exception) when (exception is IOException or
                                          UnauthorizedAccessException or
                                          JsonException)
        {
            return new DebugToolSettings();
        }
    }

    public void Save(string? path = null)
    {
        path ??= DefaultPath;
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }
        var temporary = path + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(this, Json));
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }
}
