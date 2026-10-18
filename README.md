# HMoeViewer

Avalonia 12.1.3 桌面应用，运行于 .NET 10。

使用 Windows x64 的 Native AOT 配置发布（需要 Visual Studio 的 C++ 桌面开发工具）：

```powershell
dotnet publish src/HMoeViewer/HMoeViewer.csproj -p:PublishProfile=NativeAot
```

输出位于 `artifacts/native-aot`，请完整分发该目录。界面和数据库运行于原生程序；Playwright 不支持当前 Native AOT 运行方式，因此浏览器操作由 `browser-worker` 中随包发布的独立 .NET 进程执行，无需另装 .NET。浏览器仍使用 Microsoft Edge 和原有登录会话。

数据库统一使用 sqlite-net-pcl，保留现有数据库格式。读取和业务写入使用 ORM；仅新建旧格式数据库的外键、复合主键及必要的 PRAGMA 使用 SQL，已有业务表不重建。
