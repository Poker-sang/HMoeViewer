using System;
using System.IO;

namespace HMoeData;

/// <summary>爬取器与 Viewer 共用的数据目录，与构建及系统临时目录分离。</summary>
public static class StoragePaths
{
    public static string RootDirectory { get; } = OperatingSystem.IsWindows()
        ? @"D:\WebCrawler"
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "WebCrawler");

    public static string DatabasePath { get; } = Path.Combine(RootDirectory, "current.db");

    public static string BrowserProfileDirectory { get; } = Path.Combine(RootDirectory, "browser-data");

    public static string ViewerDirectory { get; } = Path.Combine(RootDirectory, "viewer");

    public static string HistoryDirectory { get; } = Path.Combine(ViewerDirectory, "history");

    public static string BaiduProfileDirectory { get; } = Path.Combine(ViewerDirectory, "baidu-browser-data");
}
