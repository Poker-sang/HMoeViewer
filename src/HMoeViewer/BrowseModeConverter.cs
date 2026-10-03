using System;
using System.Globalization;
using Avalonia.Data.Converters;
using HMoeViewer.Core;

namespace HMoeViewer;

public sealed class BrowseModeConverter : IValueConverter
{
    public static BrowseModeConverter Instance { get; } = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => (int) (BrowseMode) (value ?? BrowseMode.Brief);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => (BrowseMode) (int) (value ?? 0);
}
