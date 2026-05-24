using Microsoft.Maui.Controls.Shapes;

namespace TestMAUIApp.Ui;

public static class SharedUiFactory
{
    public static Border Card(params View[] children)
    {
        var stack = new VerticalStackLayout
        {
            Spacing = 10,
        };

        foreach (var child in children)
        {
            stack.Children.Add(child);
        }

        return new Border
        {
            BackgroundColor = AppPalette.CardBackground,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(10) },
            Stroke = AppPalette.CardStroke,
            Padding = new Thickness(12),
            Content = stack,
        };
    }

    public static Label Title(string text) =>
        new()
        {
            Text = text,
            FontSize = 22,
            FontAttributes = FontAttributes.Bold,
            TextColor = AppPalette.TitleText,
            BackgroundColor = Colors.Transparent,
        };

    public static Label Caption(string text) =>
        new()
        {
            Text = text,
            FontSize = 12,
            TextColor = AppPalette.CaptionText,
            BackgroundColor = Colors.Transparent,
        };

    public static Label BodyLabel(string? text = null) =>
        new()
        {
            Text = text,
            FontSize = 14,
            TextColor = AppPalette.BodyText,
            BackgroundColor = Colors.Transparent,
            LineBreakMode = LineBreakMode.WordWrap,
        };

    public static Label EmphasisLabel(string? text = null) =>
        new()
        {
            Text = text,
            FontSize = 16,
            FontAttributes = FontAttributes.Bold,
            TextColor = AppPalette.TitleText,
            BackgroundColor = Colors.Transparent,
        };

    public static Label MutedLabel(string? text = null) =>
        new()
        {
            Text = text,
            FontSize = 11,
            TextColor = AppPalette.CaptionText,
            BackgroundColor = Colors.Transparent,
        };

    public static Entry Entry(string placeholder, bool isPassword = false, string? initialValue = null) =>
        new()
        {
            Placeholder = placeholder,
            IsPassword = isPassword,
            Text = initialValue,
            ClearButtonVisibility = ClearButtonVisibility.WhileEditing,
            TextColor = AppPalette.BodyText,
            PlaceholderColor = AppPalette.CaptionText,
            BackgroundColor = Colors.Transparent,
        };

    public static Button PrimaryButton(string text, EventHandler onClicked) =>
        CreateActionButton(text, onClicked);

    public static Button SecondaryButton(string text, EventHandler onClicked) =>
        CreateActionButton(text, onClicked);

    private static Button CreateActionButton(string text, EventHandler onClicked)
    {
        var button = new Button
        {
            Text = text,
            BackgroundColor = AppPalette.Primary,
            TextColor = AppPalette.PrimaryText,
            CornerRadius = 8,
            Padding = new Thickness(14, 10),
        };
        button.Clicked += onClicked;
        return button;
    }

    public static void ApplyPageChrome(ContentPage page, Color? backgroundColor = null)
    {
        page.BackgroundColor = backgroundColor ?? AppPalette.PageBackground;
    }
}
