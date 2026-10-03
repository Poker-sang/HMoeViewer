# Third-party sources

## HMoeWebCrawler and HMoeData

`src/HMoeWebCrawler` and `src/HMoeData` contain the source projects and assets imported from `C:/WorkSpace/HMoeWebCrawler`. Copyright (c) 2025 Poker, MIT; see `HMoeWebCrawler.LICENSE`.

## Pixeval conditional binding

`src/HMoeViewer/Markup/EitherBindingExtension.cs` is adapted from Pixeval's `Views/Markup/EitherBindingExtension.cs` and `Views/Converters/EitherConverter.cs`. It supports constants, live bindings and nested conditions in both branches. Copyright (c) Pixeval, GPL-3.0; see `Pixeval.LICENSE`.

## Pixeval Markdown controls

`src/HMoeViewer/Controls/Markdown` is copied from `C:/WorkSpace/Pixeval/src/Pixeval/Controls/Markdown`. Copyright (c) Pixeval, GPL-3.0; see `Pixeval.LICENSE`. Local adaptations use the HMoeViewer namespace, the native HTTP(S) launcher and local cached image streams instead of Pixeval services. Rendering uses the Markdown.Avalonia packages.

## Local QR models

`src/HMoeViewer.Extraction/models` contains the WeChatCV detector and super-resolution models used by OpenCV WeChatQRCode. Model license and checksums are included in that directory and copied with the application. QR recognition also uses ZXing.Net locally.

## Pixeval virtual grid

`src/HMoeViewer/Themes/ItemsViewListBoxStyles.axaml` is adapted from Pixeval's `Controls/ItemsViewListBoxStyles.axaml`, retaining its selection border, mask and hover appearance. Its selectors target the progress indicator containers; the viewer keeps its existing initial-selection checkbox. Copyright (c) Pixeval, GPL-3.0; see `Pixeval.LICENSE`.

`src/HMoeViewer/Controls/Panels/VirtualizingStackPanel.cs` also comes from the same Pixeval directory, retaining its GPL-3.0 header. Its namespace is changed to HMoeViewer.Controls. The download list binds the live scroll offset and viewport to this panel.

`src/HMoeViewer/Controls/Panels/VirtualizingAdaptiveGrid.cs`, `OrientationBasedMeasuresExt.cs`, `IAdaptiveGridLayoutInfo.cs`, and `WrapPanelItemsAlignment.cs` originate from `C:/WorkSpace/Pixeval/src/Pixeval/Controls/Panels`.

Copyright (c) Pixeval. Licensed under GPL-3.0; see `Pixeval.LICENSE`. Local changes replace the namespace and remove the dependency on Pixeval's numeric utility. The enum is extracted from the original WrapPanel source.

## SmoothScroll.Avalonia

`src/lib/SmoothScroll.Avalonia` is a Git submodule from https://github.com/Poker-sang/SmoothScroll.Avalonia.git, pinned to the same revision used by Pixeval. Licensed under MIT; see `src/lib/SmoothScroll.Avalonia/LICENSE.txt`. The parent repository's `src/lib/Directory.Build.targets` uses Avalonia 12.1.3 with upstream central package management and disables implicit usings without modifying the submodule. The application targets .NET 10. `SmoothScroll.Usings.cs` supplies the upstream sources' required imports. The application uses `ScrollViewerSmoothTheme` and its `ScrollViewerPresenter`, as Pixeval does.
