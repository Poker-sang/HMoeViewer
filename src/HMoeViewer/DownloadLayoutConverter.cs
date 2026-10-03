using System;
using System.Globalization;
using Avalonia.Data.Converters;
using HMoeViewer.Core;

namespace HMoeViewer;

public sealed class DownloadLayoutConverter : IValueConverter
{
    public static DownloadLayoutConverter Instance { get; } = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => (int) (DownloadLayoutMode) (value ?? DownloadLayoutMode.Default);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => (DownloadLayoutMode) (int) (value ?? 0);
}
