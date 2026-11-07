using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using HMoeViewer.Core;

namespace HMoeViewer.Pages;

public partial class SettingsPage : ContentPage
{
    private MainViewModel ViewModel => (MainViewModel) DataContext!;

    public SettingsPage() => InitializeComponent();

    private async void ChooseDatabase(object? sender, RoutedEventArgs e)
    {
        var files = await TopLevel.GetTopLevel(this)!.StorageProvider.OpenFilePickerAsync(new()
        {
            Title = "选择爬虫数据库",
            AllowMultiple = false,
            FileTypeFilter = [new("SQLite 数据库")
                {
                    Patterns = ["*.db", "*.sqlite", "*.sqlite3"]
                }]
        });
        if (files.FirstOrDefault()?.TryGetLocalPath() is not { } path)
            return;
        ViewModel.DatabasePath = path;
        await ViewModel.LoadAsync();
    }
}
