using BalloonMesh.DebugTool.Core;
using Xunit;

namespace BalloonMesh.DebugTool.Tests;

public sealed class DebugToolSettingsTests
{
    [Fact]
    public void SaveAndLoad_RoundTripsNonSecretPreferences()
    {
        var directory = Path.Combine(Path.GetTempPath(), "balloonmesh-debugtool-tests",
            Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "settings.json");
        try
        {
            var settings = new DebugToolSettings
            {
                ServerUrl = "https://example.test",
                GatewayId = "gw-test",
                GatewayTransport = "HTTP",
                PollWaitMs = 2_500,
                FtmBatchId = "9",
                Red = "10",
                Green = "20",
                Blue = "30",
                ExecuteAt = "2026-11-12T00:00:00.123+00:00",
                ResetSetNow = true,
                FtmTimestamp = "12345",
                ExpandShortOpcode = false
            };

            settings.Save(path);
            var loaded = DebugToolSettings.Load(path);

            Assert.Equal("https://example.test", loaded.ServerUrl);
            Assert.Equal("gw-test", loaded.GatewayId);
            Assert.Equal("HTTP", loaded.GatewayTransport);
            Assert.Equal(2_500, loaded.PollWaitMs);
            Assert.Equal("9", loaded.FtmBatchId);
            Assert.Equal("10", loaded.Red);
            Assert.Equal("20", loaded.Green);
            Assert.Equal("30", loaded.Blue);
            Assert.Equal("2026-11-12T00:00:00.123+00:00", loaded.ExecuteAt);
            Assert.True(loaded.ResetSetNow);
            Assert.Equal("12345", loaded.FtmTimestamp);
            Assert.False(loaded.ExpandShortOpcode);
            Assert.False(File.Exists(path + ".tmp"));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public void Load_MissingOrCorruptFileReturnsDefaults()
    {
        var directory = Path.Combine(Path.GetTempPath(), "balloonmesh-debugtool-tests",
            Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "settings.json");
        try
        {
            Assert.Equal("http://localhost:5080",
                DebugToolSettings.Load(path).ServerUrl);

            Directory.CreateDirectory(directory);
            File.WriteAllText(path, "{not json");
            Assert.Equal("http://localhost:5080",
                DebugToolSettings.Load(path).ServerUrl);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}
