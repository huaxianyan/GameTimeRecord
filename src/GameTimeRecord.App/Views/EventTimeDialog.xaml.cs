using System.Globalization;
using System.Windows;

namespace GameTimeRecord.App.Views;

public partial class EventTimeDialog : Window
{
    public EventTimeDialog(string localTime)
    {
        InitializeComponent();
        TimeBox.Text = localTime;
        Loaded += (_, _) =>
        {
            TimeBox.Focus();
            TimeBox.SelectAll();
        };
    }

    public string LocalTimeText => TimeBox.Text;

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!DateTime.TryParseExact(
                LocalTimeText,
                "yyyy-MM-dd HH:mm:ss",
                CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces,
                out _))
        {
            MessageBox.Show(
                this,
                "请按“年-月-日 时:分:秒”的格式填写时间。",
                "无法保存",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            TimeBox.Focus();
            return;
        }

        DialogResult = true;
    }
}
