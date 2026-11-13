using System.Globalization;
using Avalonia.Media;
using BalloonMesh.DebugTool.Core;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BalloonMesh.DebugTool.ViewModels;

public partial class MainWindowViewModel
{
    public IReadOnlyList<string> TargetKinds { get; } = ["all", "balloon"];

    [ObservableProperty]
    private string _targetKind = "all";

    [ObservableProperty]
    private string _target = "all";

    [ObservableProperty]
    private string _red = "255";

    [ObservableProperty]
    private string _green = "255";

    [ObservableProperty]
    private string _blue = "255";

    [ObservableProperty]
    private string _executeAt = string.Empty;

    [ObservableProperty]
    private string _resetCutoff = string.Empty;

    [ObservableProperty]
    private bool _resetSetNow;

    [ObservableProperty]
    private IBrush _commandColorBrush = Brushes.White;

    [ObservableProperty]
    private string _commandResult = "No command sent yet.";

    [RelayCommand]
    private Task SendNowAsync() => RunAsync("Send LED now", async () =>
    {
        var client = GetClient();
        var (r, g, b) = ParseColor();
        var result = await client.SendLedNowAsync(
            new LedNowCommand(TargetKind, Target.Trim(), r, g, b)).ConfigureAwait(true);
        ApplyCommandResult(result);
    });

    [RelayCommand]
    private Task ScheduleAsync() => RunAsync("Schedule LED", async () =>
    {
        var client = GetClient();
        var (r, g, b) = ParseColor();
        var executeAt = ParseUtcOr(ExecuteAt, "Execute at",
            DateTimeOffset.UtcNow.AddSeconds(10));
        // Show the exact time the server will use.  A blank field previously
        // hid the fallback, so the matching Cancel could never be issued.
        ExecuteAt = executeAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
        var result = await client.SendLedSetAsync(new LedSetCommand(
            TargetKind, Target.Trim(), executeAt, r, g, b)).ConfigureAwait(true);
        ApplyCommandResult(result);
        await RefreshCoreAsync(client).ConfigureAwait(true);
    });

    [RelayCommand]
    private Task CancelAsync() => RunAsync("Cancel LED", async () =>
    {
        if (string.IsNullOrWhiteSpace(ExecuteAt))
        {
            throw new ArgumentException(
                "Cancel requires the reservation's exact execute time.");
        }
        var client = GetClient();
        var result = await client.SendLedCancelAsync(new LedCancelCommand(
            TargetKind, Target.Trim(), ParseUtc(ExecuteAt, "Execute at"))).ConfigureAwait(true);
        ApplyCommandResult(result);
    });

    [RelayCommand]
    private Task ResetAsync() => ResetCoreAsync(ResetSetNow);

    [RelayCommand]
    private Task ResetAndSetAsync() => ResetCoreAsync(setNow: true);

    private Task ResetCoreAsync(bool setNow) => RunAsync(setNow ? "Reset + set" : "Reset",
        async () =>
        {
            var client = GetClient();
            var color = setNow ? ParseColor() : (R: (byte)0, G: (byte)0, B: (byte)0);
            var cutoff = string.IsNullOrWhiteSpace(ResetCutoff)
                ? (DateTimeOffset?)null
                : ParseUtc(ResetCutoff, "Reset cutoff");
            var result = await client.SendLedResetAsync(new LedResetCommand(
                TargetKind, Target.Trim(), cutoff, setNow, color.R, color.G, color.B))
                .ConfigureAwait(true);
            ApplyCommandResult(result);
        });

    partial void OnRedChanged(string value) => UpdateCommandColor();

    partial void OnGreenChanged(string value) => UpdateCommandColor();

    partial void OnBlueChanged(string value) => UpdateCommandColor();

    private void ApplyCommandResult(CommandResult result)
    {
        CommandResult = FormatCommandResult(result);
        AddLog(result.Accepted ? "info" : "warn", CommandResult);
    }

    private (byte R, byte G, byte B) ParseColor() => (
        ParseByteValue(Red, "Red"),
        ParseByteValue(Green, "Green"),
        ParseByteValue(Blue, "Blue"));

    private void UpdateCommandColor()
    {
        if (byte.TryParse(Red.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture,
                out var r) &&
            byte.TryParse(Green.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture,
                out var g) &&
            byte.TryParse(Blue.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture,
                out var b))
        {
            CommandColorBrush = new SolidColorBrush(Color.FromRgb(r, g, b));
            return;
        }
        CommandColorBrush = Brushes.Transparent;
    }
}
