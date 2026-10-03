// Copyright (c) Pixeval.
// Licensed under the GPL-3.0 License.

using System;
using System.Collections.Generic;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Markup.Xaml;

namespace HMoeViewer.Markup;

/// <summary>A XAML conditional whose branches can be constants or live bindings.</summary>
public sealed class EitherBindingExtension(BindingBase binding, object? x, object? y) : MarkupExtension
{
    private static readonly IMultiValueConverter _Converter = new FuncMultiValueConverter<object?, object?>(
        (IReadOnlyList<object?> values) => values is [var condition, var first, var fallback]
            ? condition is true ? first : fallback
            : BindingOperations.DoNothing);

    /// <inheritdoc />
    public override MultiBinding ProvideValue(IServiceProvider serviceProvider) => new()
    {
        Bindings = { binding, AsBinding(x), AsBinding(y) },
        Converter = _Converter,
        Mode = BindingMode.OneWay
    };

    private static BindingBase AsBinding(object? value) => value as BindingBase
        ?? new CompiledBinding { Source = value, Mode = BindingMode.OneTime };
}
