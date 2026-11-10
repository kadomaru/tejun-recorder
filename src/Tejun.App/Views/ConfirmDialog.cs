using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Tejun.App.Views;

/// <summary>「はい／いいえ」を尋ねる小さな窓。</summary>
public sealed class ConfirmDialog : Window
{
    ConfirmDialog(string message, string yes, string no)
    {
        Title = "手順レコーダー";
        SizeToContent = SizeToContent.WidthAndHeight;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        var yesButton = new Button { Content = yes, Classes = { "plain" }, MinWidth = 110, HorizontalContentAlignment = HorizontalAlignment.Center };
        var noButton = new Button { Content = no, Classes = { "save" }, MinWidth = 110, IsDefault = true, IsCancel = true };
        yesButton.Click += (_, _) => Close(true);
        noButton.Click += (_, _) => Close(false);

        Content = new StackPanel
        {
            Margin = new Thickness(26, 22),
            Spacing = 20,
            MaxWidth = 440,
            Children =
            {
                new TextBlock { Text = message, FontSize = 15, TextWrapping = TextWrapping.Wrap },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal, Spacing = 10, HorizontalAlignment = HorizontalAlignment.Right,
                    Children = { yesButton, noButton },
                },
            },
        };
    }

    /// <summary><paramref name="yes"/> を選んだら true。窓を閉じた場合と <paramref name="no"/> は false。</summary>
    public static async Task<bool> Ask(Window owner, string message, string yes, string no) =>
        await new ConfirmDialog(message, yes, no).ShowDialog<bool?>(owner) == true;
}
