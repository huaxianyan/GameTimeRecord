using System.Windows;
using System.Windows.Media;

namespace GameTimeRecord.App.Views;

public partial class AppDialog : Window
{
    private bool _accepted;

    private AppDialog(
        string title,
        string message,
        AppDialogKind kind,
        bool showSecondaryButton,
        string primaryButtonText)
    {
        InitializeComponent();
        Title = title;
        TitleText.Text = title;
        MessageText.Text = message;
        PrimaryButton.Content = primaryButtonText;
        SecondaryButton.Visibility = showSecondaryButton
            ? Visibility.Visible
            : Visibility.Collapsed;

        ApplyKind(kind);
        Loaded += (_, _) => PrimaryButton.Focus();
    }

    public static void ShowMessage(
        Window? owner,
        string title,
        string message,
        AppDialogKind kind = AppDialogKind.Information)
    {
        var dialog = new AppDialog(title, message, kind, false, "确定");
        SetOwner(dialog, owner);
        dialog.ShowDialog();
    }

    public static bool Confirm(
        Window owner,
        string title,
        string message,
        string primaryButtonText,
        AppDialogKind kind = AppDialogKind.Warning)
    {
        var dialog = new AppDialog(
            title,
            message,
            kind,
            true,
            primaryButtonText);
        SetOwner(dialog, owner);
        dialog.ShowDialog();
        return dialog._accepted;
    }

    private static void SetOwner(AppDialog dialog, Window? owner)
    {
        if (owner is { IsVisible: true })
        {
            dialog.Owner = owner;
        }
        else
        {
            dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }
    }

    private void ApplyKind(AppDialogKind kind)
    {
        switch (kind)
        {
            case AppDialogKind.Error:
                IconBorder.Background = Brush("#F4EAEA");
                IconText.Foreground = Brush("#815955");
                IconText.Text = "!";
                break;
            case AppDialogKind.Warning:
                IconBorder.Background = Brush("#F3EFE7");
                IconText.Foreground = Brush("#7A6848");
                IconText.Text = "!";
                PrimaryButton.Background = Brush("#865B58");
                PrimaryButton.BorderBrush = Brush("#865B58");
                break;
            case AppDialogKind.Information:
                break;
        }
    }

    private static SolidColorBrush Brush(string color) =>
        new((Color)ColorConverter.ConvertFromString(color));

    private void PrimaryButton_Click(object sender, RoutedEventArgs e)
    {
        _accepted = true;
        DialogResult = true;
    }

    private void SecondaryButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}

public enum AppDialogKind
{
    Information,
    Warning,
    Error,
}
