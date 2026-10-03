using System;
// Copyright (c) Pixeval.
// Licensed under the GPL-3.0 License.

using Avalonia;

namespace HMoeViewer.Controls;

internal interface IAdaptiveGridLayoutInfo
{
    int Lines { get; }

    int ItemsPerLine { get; }

    event EventHandler<AvaloniaPropertyChangedEventArgs>? PropertyChanged;
}
