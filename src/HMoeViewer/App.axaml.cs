using System;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using HMoeViewer.Core;
using HMoeViewer.Downloads;
using HMoeViewer.Persistence;

namespace HMoeViewer;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);

#if DEBUG
        this.AttachDeveloperTools();
#endif
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var articles = new ArticleReviewService();
            var downloads = new BaiduClientDownloadService(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HMoeViewer", "baidu-browser-data"));
            desktop.MainWindow = new MainWindow(new MainViewModel(new SqlitePostRepository(), new SqliteSelectionHistoryStore(), articles) { ClientDownloads = downloads }, desktop.Args?.FirstOrDefault());
            desktop.Exit += (_, _) => articles.DisposeAsync().AsTask().GetAwaiter().GetResult();
            desktop.Exit += (_, _) => downloads.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
        base.OnFrameworkInitializationCompleted();
    }
}
