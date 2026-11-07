# HMoeViewer

Avalonia 12.1.3 桌面应用，运行于 .NET 10。

使用 Windows x64 的 Native AOT 配置发布（需要 Visual Studio 的 C++ 桌面开发工具）：

```powershell
dotnet publish src/HMoeViewer/HMoeViewer.csproj -p:PublishProfile=NativeAot
```

输出位于 `artifacts/native-aot`，请完整分发该目录。界面和数据库运行于原生程序；Playwright 不支持当前 Native AOT 运行方式，因此浏览器操作由 `browser-worker` 中随包发布的独立 .NET 进程执行，无需另装 .NET。浏览器仍使用 Microsoft Edge 和原有登录会话。

数据库统一使用 sqlite-net-pcl，保留现有数据库格式。读取和业务写入使用 ORM；仅新建旧格式数据库的外键、复合主键及必要的 PRAGMA 使用 SQL，已有业务表不重建。
爬取器与 Viewer 的存储根目录统一由 `HMoeData.StoragePaths` 定义。Windows 默认使用 `D:\WebCrawler`：

- `current.db`、`last.db`、`img`、`settings.json`、`browser-data`：爬取器数据及网站登录会话。
- `viewer/history`：按数据库及批次隔离的撤回／恢复记录、临时筛选状态、正文图片和下载链接缓存。
- `viewer/baidu-browser-data`：百度网盘登录会话。

Viewer 仍可手动选择其他数据库；其正文及历史缓存统一保存在上述 `viewer/history` 下。缓存已脱离系统临时目录，清理构建输出不会移除这些数据。

SQLite WAL 模式下，最后一个连接若为只读连接，即使已正确释放，也可能留下空的 `-wal` 和 `-shm` 文件；文件存在本身不代表连接泄漏。不要直接删除可能仍包含未写回事务的 WAL 文件。
