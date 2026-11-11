using System.Text;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using BalloonMesh.DebugTool.ViewModels;

namespace BalloonMesh.DebugTool.Views;

public partial class DecodedLogView : UserControl
{
    public DecodedLogView()
    {
        InitializeComponent();
    }

    private async void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.StorageProvider is not { } storage)
        {
            return;
        }

        try
        {
            var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Save decoded log",
                SuggestedFileName = $"balloonmesh-decoded-{DateTime.Now:yyyyMMdd-HHmmss}.log",
                DefaultExtension = "log",
                FileTypeChoices =
                [
                    new FilePickerFileType("Log files") { Patterns = ["*.log", "*.txt"] }
                ]
            });
            if (file is null)
            {
                return;
            }

            await using var stream = await file.OpenWriteAsync();
            await using var writer = new StreamWriter(stream, Encoding.UTF8);
            await writer.WriteAsync(viewModel.GetDecodedLogText());
            viewModel.ReportLog("info", $"Decoded log saved to {file.Name}.");
        }
        catch (Exception exception)
        {
            viewModel.ReportLog("error", $"Could not save decoded log: {exception.Message}");
        }
    }
}
