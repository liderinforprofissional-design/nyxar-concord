using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace NyxarConcord.Views;

public partial class CreateServerDialog : Window
{
    public string ServerNameText => ServerName.Text.Trim();
    public string AvatarPath { get; private set; } = "";

    public CreateServerDialog()
    {
        InitializeComponent();
        ServerName.Focus();
        ServerName.SelectAll();
        MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); };
    }

    private void ChoosePhoto_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Filter = "Imagens|*.png;*.jpg;*.jpeg;*.bmp;*.gif" };
        if (dlg.ShowDialog() == true)
        {
            AvatarPath = dlg.FileName;
            try { AvatarPreview.ImageSource = new BitmapImage(new System.Uri(AvatarPath)); } catch { }
        }
    }

    private void Create_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(ServerNameText))
        {
            MessageBox.Show("Dê um nome ao servidor.");
            return;
        }
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
