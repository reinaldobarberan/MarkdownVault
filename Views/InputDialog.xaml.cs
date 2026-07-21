using System.Windows;
using System.Windows.Input;

namespace MarkdownVault.Views;

public partial class InputDialog : Window
{
    public string InputText => InputBox.Text;

    public InputDialog(string title, string label, string defaultValue = "")
    {
        InitializeComponent();
        Title           = title;
        LabelText.Text  = label;
        InputBox.Text   = defaultValue;
        InputBox.SelectAll();
        Loaded += (_, _) => InputBox.Focus();
    }

    private void OK_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }

    private void InputBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
            DialogResult = true;
    }
}
