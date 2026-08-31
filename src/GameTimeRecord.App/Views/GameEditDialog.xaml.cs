using System.Windows;
using GameTimeRecord.Core;

namespace GameTimeRecord.App.Views;

public partial class GameEditDialog : Window
{
    public GameEditDialog(Game? game = null)
    {
        InitializeComponent();
        if (game is not null)
        {
            NameBox.Text = game.Name;
            AliasBox.Text = game.Alias;
            PlatformBox.Text = game.Platform;
            NotesBox.Text = game.Notes;
        }

        Loaded += (_, _) => NameBox.Focus();
    }

    public string GameName => NameBox.Text;

    public string GameAlias => AliasBox.Text;

    public string GamePlatform => PlatformBox.Text;

    public string GameNotes => NotesBox.Text;

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(GameName))
        {
            MessageBox.Show(
                this,
                "请填写游戏名称。",
                "无法保存",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            NameBox.Focus();
            return;
        }

        DialogResult = true;
    }
}
