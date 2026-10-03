using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using HMoeViewer.Core;

namespace HMoeViewer;

public partial class MainWindow : Window
{
    private MainViewModel ViewModel => (MainViewModel) DataContext!;

    private bool _closingAfterSave;

    public MainWindow()
    {
        InitializeComponent();
        Opened += async (_, _) =>
        {
            if (DataContext is MainViewModel viewModel)
                await viewModel.LoadAsync();
        };
        Closing += SaveBeforeClosing;
    }

    public MainWindow(MainViewModel viewModel, string? databasePath = null) : this()
    {
        DataContext = viewModel;
        if (!string.IsNullOrWhiteSpace(databasePath))
            viewModel.DatabasePath = databasePath;
    }

    private async void ChooseDatabase(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "选择爬虫数据库",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("SQLite 数据库")
{
Patterns = ["*.db", "*.sqlite", "*.sqlite3"]
}]
        });
        if (files.FirstOrDefault()?.TryGetLocalPath() is not { } path)
            return;
        ViewModel.DatabasePath = path;
        await ViewModel.LoadAsync();
    }

    private async void SaveBeforeClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_closingAfterSave)
            return;
        if (DataContext is not MainViewModel)
            return;
        if (ViewModel.IsBusy)
        {
            e.Cancel = true;
            ViewModel.Status = "正在读写数据库，请稍后关闭。";
            return;
        }

        if (!ViewModel.SaveCommand.CanExecute(null))
            return;
        e.Cancel = true;
        if (await ViewModel.SaveAsync())
        {
            _closingAfterSave = true;
            Close();
        }
    }
}
