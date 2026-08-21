using System.Windows.Controls;
using System.Windows.Media.Imaging;

namespace NyxarConcord.Views;

public partial class BrandLogo : UserControl
{
    public BrandLogo()
    {
        InitializeComponent();
        // Carrega o balão original de Assets\nyxar.png (se existir).
        try
        {
            LogoImage.Source = new BitmapImage(new System.Uri("pack://application:,,,/Assets/nyxar.png"));
        }
        catch
        {
            // Sem a imagem ainda — mostra só o texto "Concord".
        }
    }
}
