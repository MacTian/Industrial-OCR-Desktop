// Views/TextInputDialog.xaml.cs
using System.Windows;

namespace PaddleOcrDesktop.Views;

public partial class TextInputDialog : Window
{
    public string InputText { get; set; } = string.Empty;
    public string PromptText { get; set; } = "请输入文本:";

    public TextInputDialog(string prompt, string defaultText = "")
    {
        PromptText = prompt;
        InputText = defaultText;
        DataContext = this;
        InitializeComponent();
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
