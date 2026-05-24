namespace TestMAUIApp.Ui;

public sealed class SentTimeValueConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
    {
        return value is DateTime sentAt
            ? sentAt.ToLocalTime().ToString("g")
            : string.Empty;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => throw new NotSupportedException();
}
