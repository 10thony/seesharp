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
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(10) },
            Stroke = Color.FromArgb("#D9D9D9"),
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
        };

    public static Label Caption(string text) =>
        new()
        {
            Text = text,
            FontSize = 12,
            TextColor = Colors.Gray,
        };

    public static Entry Entry(string placeholder, bool isPassword = false, string? initialValue = null) =>
        new()
        {
            Placeholder = placeholder,
            IsPassword = isPassword,
            Text = initialValue,
            ClearButtonVisibility = ClearButtonVisibility.WhileEditing,
        };

    public static Button PrimaryButton(string text, EventHandler onClicked)
    {
        var button = new Button
        {
            Text = text,
            BackgroundColor = Color.FromArgb("#2E6FE8"),
            TextColor = Colors.White,
            CornerRadius = 8,
            Padding = new Thickness(14, 10),
        };
        button.Clicked += onClicked;
        return button;
    }

    public static Button SecondaryButton(string text, EventHandler onClicked)
    {
        var button = new Button
        {
            Text = text,
            CornerRadius = 8,
            Padding = new Thickness(14, 10),
        };
        button.Clicked += onClicked;
        return button;
    }
}
