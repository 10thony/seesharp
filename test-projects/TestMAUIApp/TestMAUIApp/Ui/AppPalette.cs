namespace TestMAUIApp.Ui;

/// <summary>
/// Single source of truth for programmatic UI colors so all platforms render the same.
/// Values align with Resources/Styles/Colors.xaml (light theme).
/// </summary>
public static class AppPalette
{
    public static Color Primary { get; } = Color.FromArgb("#512BD4");

    public static Color PrimaryText { get; } = Colors.White;

    public static Color PageBackground { get; } = Colors.White;

    public static Color FlyoutBackground { get; } = Color.FromArgb("#F5F5F5");

    public static Color CardBackground { get; } = Colors.White;

    public static Color CardStroke { get; } = Color.FromArgb("#D9D9D9");

    public static Color TitleText { get; } = Color.FromArgb("#212121");

    public static Color BodyText { get; } = Colors.Black;

    public static Color CaptionText { get; } = Color.FromArgb("#6E6E6E");

    public static Color NavBarBackground { get; } = Color.FromArgb("#512BD4");

    public static Color NavBarText { get; } = Colors.White;
}
