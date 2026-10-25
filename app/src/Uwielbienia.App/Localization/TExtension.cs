using Avalonia.Data.Converters;
using Avalonia.Markup.Xaml;
using System.Globalization;
using Uwielbienia.Core.Localization;

namespace Uwielbienia.App.Localization;

/// <summary>Tekst interfejsu w wybranym języku: <c>Text="{l:T 'Dodaj do planu'}"</c> (klucz = tekst polski).</summary>
public sealed class TExtension(string text) : MarkupExtension
{
    public string Text { get; } = text;

    public override object ProvideValue(IServiceProvider serviceProvider) => Tr.T(Text);
}

/// <summary>
/// Formatowanie z tłumaczeniem — zamiast <c>StringFormat</c> w powiązaniu:
/// <c>{Binding X, Converter={x:Static l:Fmt.Instance}, ConverterParameter='Aktualizuj do {0}'}</c>.
/// </summary>
public sealed class Fmt : IValueConverter, IMultiValueConverter
{
    public static readonly Fmt Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        parameter is string format ? string.Format(culture, Tr.T(format), value) : value;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture) =>
        parameter is string format ? string.Format(culture, Tr.T(format), values.ToArray()) : null;
}
