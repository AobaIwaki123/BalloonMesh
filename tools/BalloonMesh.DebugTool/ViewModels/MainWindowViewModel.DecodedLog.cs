using System.Collections.ObjectModel;
using System.Text;
using Avalonia.Threading;
using BalloonMesh.DebugTool.Core;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BalloonMesh.DebugTool.ViewModels;

public partial class MainWindowViewModel
{
    private const int MaxDecodedLog = 1000;
    private readonly List<DecodedLogEntryViewModel> _decodedLogAll = [];

    public ObservableCollection<DecodedLogEntryViewModel> DecodedLogView { get; } = [];

    public IReadOnlyList<string> DecodedDirections { get; } = ["All", "TX", "RX", "DECODE"];

    public IReadOnlyList<string> DecodedTransports { get; } = ["All", "WebSocket", "HTTP", "-"];

    [ObservableProperty]
    private string _decodedDirectionFilter = "All";

    [ObservableProperty]
    private string _decodedTransportFilter = "All";

    [ObservableProperty]
    private string _decodedSearch = string.Empty;

    [ObservableProperty]
    private DecodedLogEntryViewModel? _selectedDecodedEntry;

    [RelayCommand]
    private void ClearDecodedLog()
    {
        _decodedLogAll.Clear();
        DecodedLogView.Clear();
        SelectedDecodedEntry = null;
    }

    partial void OnDecodedDirectionFilterChanged(string value) => RefreshDecodedLogView();

    partial void OnDecodedTransportFilterChanged(string value) => RefreshDecodedLogView();

    partial void OnDecodedSearchChanged(string value) => RefreshDecodedLogView();

    public void AddDecodedEntry(DateTimeOffset timestamp, string direction,
        GatewayTransport? transport, DecodedMessage decoded, string rawJson)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() =>
                AddDecodedEntry(timestamp, direction, transport, decoded, rawJson));
            return;
        }

        var entry = new DecodedLogEntryViewModel(timestamp, direction, transport,
            decoded, rawJson);
        _decodedLogAll.Add(entry);
        while (_decodedLogAll.Count > MaxDecodedLog)
        {
            _decodedLogAll.RemoveAt(0);
        }
        if (MatchesFilter(entry))
        {
            DecodedLogView.Add(entry);
        }
        while (DecodedLogView.Count > MaxDecodedLog)
        {
            DecodedLogView.RemoveAt(0);
        }
    }

    public string GetDecodedLogText()
    {
        var builder = new StringBuilder();
        foreach (var entry in DecodedLogView)
        {
            builder.AppendLine(entry.Text);
            if (!string.IsNullOrWhiteSpace(entry.RawJson))
            {
                builder.Append("  raw: ").AppendLine(entry.RawJson);
            }
        }
        return builder.ToString();
    }

    public void ReportLog(string level, string message) => AddLog(level, message);

    private void RefreshDecodedLogView()
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(RefreshDecodedLogView);
            return;
        }
        DecodedLogView.Clear();
        foreach (var entry in _decodedLogAll)
        {
            if (MatchesFilter(entry))
            {
                DecodedLogView.Add(entry);
            }
        }
    }

    private bool MatchesFilter(DecodedLogEntryViewModel entry)
    {
        if (DecodedDirectionFilter != "All" &&
            !string.Equals(entry.Direction, DecodedDirectionFilter,
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        if (DecodedTransportFilter != "All" &&
            !string.Equals(entry.Transport, DecodedTransportFilter,
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        var search = DecodedSearch.Trim();
        return search.Length == 0 ||
               entry.SearchText.Contains(search, StringComparison.OrdinalIgnoreCase);
    }
}
