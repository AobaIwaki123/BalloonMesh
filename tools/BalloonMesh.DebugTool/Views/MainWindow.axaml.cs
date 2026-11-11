using System.Collections.Specialized;
using Avalonia.Controls;
using Avalonia.Threading;
using BalloonMesh.DebugTool.ViewModels;

namespace BalloonMesh.DebugTool.Views;

public partial class MainWindow : Window
{
    private INotifyCollectionChanged? _observedLog;

    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    protected override void OnClosed(EventArgs e)
    {
        if (_observedLog is not null)
        {
            _observedLog.CollectionChanged -= OnLogCollectionChanged;
            _observedLog = null;
        }
        base.OnClosed(e);
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_observedLog is not null)
        {
            _observedLog.CollectionChanged -= OnLogCollectionChanged;
        }
        _observedLog = (DataContext as MainWindowViewModel)?.Log;
        if (_observedLog is not null)
        {
            _observedLog.CollectionChanged += OnLogCollectionChanged;
        }
    }

    private void OnLogCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            this.FindControl<ScrollViewer>("LogScrollViewer")?.ScrollToEnd();
        }, DispatcherPriority.Background);
    }
}
