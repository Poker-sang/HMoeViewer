using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using HMoeData.Models;
using HMoeData.Persistence;
using HMoeWebCrawler;
using HMoeWebCrawler.LocalModels;

// 连续获取到n个已存在的项目后，停止爬取
const int continuousExistenceThreshold = 10;
Settings? settings = null;

ConsoleLogger.Header("HMoe Web Crawler");
ConsoleLogger.Info("正在加载运行配置...");

// 记录日志路径
var loggerPath =
#if DEBUG
    @"D:\HMoeWebCrawler";
#else
    Environment.CurrentDirectory;
#endif
var loggerImgPath = Path.Combine(loggerPath, "img");
var loggerDbPath = Path.Combine(loggerPath, "current.db");
var loggerLastDbPath = Path.Combine(loggerPath, "last.db");
var loggerSettingsPath = Path.Combine(loggerPath, "settings.json");

_ = Directory.CreateDirectory(loggerImgPath);

if (!File.Exists(loggerSettingsPath))
    throw new("Missing Settings in " + loggerSettingsPath);

try
{
    settings = await JsonSerializer.OpenDeserializeAsync(loggerSettingsPath, SerializerContext.DefaultOverride.Settings);
}
catch (Exception e)
{
    ConsoleLogger.Exception(e, "配置加载失败");
}

if (settings is null)
    throw new InvalidDataException("Invalid settings " + loggerSettingsPath);

ConsoleLogger.Info($"运行模式: {(settings.NewSession ? "全量会话" : "增量会话")}");

await using var session = new HMoeSession();
await session.InitAsync();
await session.NavigateToSiteAsync();
await session.EnsureLoggedInAsync(settings.Email, settings.Password);
await session.FetchNonceAsync(); // 获取 nonce 并签到

using var postLookup = HMoeDbStore.OpenPostLookup(loggerDbPath);
var postsToSave = new List<Post>();
var latestBatchPostsCount = 0;

if (!settings.NewSession)
    foreach (var post in HMoeDbStore.LoadNewPosts(loggerDbPath))
    {
        latestBatchPostsCount++;
        session.DownloadThumbnailAddToList(post, loggerImgPath);
    }

var newItemsCount = 0;
var continuousExistence = 0;
var data = new SearchData(1);
while (true)
{
    var tempPosts = await session.SearchPageAsync(data);

    while (tempPosts.TryPop(out var post))
        if (!postLookup.Exists(post.Id) && postsToSave.All(existingPost => existingPost.Id != post.Id))
        {
            postsToSave.Add(post);
            ConsoleLogger.Success($"New Item [{post.Id}]: {post.Url}");
            newItemsCount++;
            if (continuousExistence < continuousExistenceThreshold)
                continuousExistence = 0;
            session.DownloadThumbnailAddToList(post, loggerImgPath);
        }
        else
        {
            ConsoleLogger.Skip($"Item existed: {post.Id} | Continuous existence count: {continuousExistence} | Next: {continuousExistence + 1}/{continuousExistenceThreshold}");
            continuousExistence++;
        }

    if (continuousExistence >= continuousExistenceThreshold)
        break;

    data.Paged++;
}

ConsoleLogger.Success("达到连续存在阈值，停止爬取。等待缩略图下载完成");

await session.WhenAllDownloadAsync();

if (newItemsCount is 0)
{
    ConsoleLogger.Info("没有新项目，不需要保存");
}
else
{
    var resultPosts = postsToSave.OrderByDescending(t => t.Date).ToList();
    var writeTime = DateTimeOffset.UtcNow;
    var currentBatchCount = settings.NewSession ? newItemsCount : latestBatchPostsCount + newItemsCount;
    ConsoleLogger.Success($"本次写入批次 {currentBatchCount} 项，新抓取 {newItemsCount} 项");

    try
    {
        ConsoleLogger.Info("正在保存数据库: " + loggerDbPath);

        if (File.Exists(loggerDbPath))
            File.Copy(loggerDbPath, loggerLastDbPath, true);

        HMoeDbStore.SavePosts(loggerDbPath, resultPosts, new(writeTime, !settings.NewSession));
    }
    catch (Exception e)
    {
        ConsoleLogger.Exception(e, "数据库保存失败");
        var fileName = $"TempLog {DateTime.Now:yyyy.MM.dd HH-mm-ss}.db";
        ConsoleLogger.Warning($"主数据库保存失败，正在写入备份: {fileName}");
        var loggerTempDbPath = Path.Combine(loggerPath, fileName);
        HMoeDbStore.SavePosts(loggerTempDbPath, resultPosts, new(writeTime, !settings.NewSession));
        ConsoleLogger.Success("备份数据库写入完成");
    }
}

ConsoleLogger.Success("任务完成，按任意键退出");
Console.ReadKey();

return;
