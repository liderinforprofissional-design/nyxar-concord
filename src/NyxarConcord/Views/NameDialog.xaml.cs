using System.Windows;

namespace NyxarConcord.Views;

public partial class NameDialog : Window
{
    public string EnteredName => NameInput.Text;

    public NameDialog()
    {
        InitializeComponent();
        NameInput.Focus();
    }

    private void Enter_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }
}
