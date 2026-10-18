using System;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
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
            IArticleReviewService articles;
            IClientDownloadService downloads;
            var profile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HMoeViewer", "baidu-browser-data");
            if (RuntimeFeature.IsDynamicCodeSupported)
            {
                var localArticles = new ArticleReviewService();
                articles = localArticles;
                downloads = new BaiduClientDownloadService(profile);
                desktop.Exit += (_, _) => localArticles.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
            else
            {
                var worker = new BrowserWorkerClient();
                articles = new WorkerArticleReviewService(worker);
                downloads = new WorkerClientDownloadService(worker, profile);
                desktop.Exit += (_, _) => worker.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
            desktop.MainWindow = new MainWindow(new(new SqlitePostRepository(), new SqliteSelectionHistoryStore(), articles) { ClientDownloads = downloads }, desktop.Args?.FirstOrDefault());
            desktop.Exit += (_, _) => downloads.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
        base.OnFrameworkInitializationCompleted();
    }
}
