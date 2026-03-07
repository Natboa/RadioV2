using System.Windows;

namespace RadioV2.DevTool.Dialogs;

public partial class ConfirmDialog : Window
{
    private ConfirmDialog(string message, string title)
    {
        InitializeComponent();
        Title = title;
        MessageText.Text = message;
    }

    private void OnConfirm(object sender, RoutedEventArgs e) => DialogResult = true;
    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;

    public static bool Show(string message, string title = "Confirm")
    {
        var dlg = new ConfirmDialog(message, title)
        {
            Owner = Application.Current.MainWindow
        };
        return dlg.ShowDialog() == true;
    }
}
