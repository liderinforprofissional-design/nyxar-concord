using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using NyxarConcord.Services;

namespace NyxarConcord.Views;

public partial class ScreenSharePicker : Window
{
    public ScreenSource? Selected { get; private set; }

    /// <summary>Altura da resolução escolhida (720/480/360).</summary>
    public int SelectedHeight => R480.IsChecked == true ? 480 : R360.IsChecked == true ? 360 : 720;

    public ScreenSharePicker(ScreenSourceService service)
    {
        InitializeComponent();

        var view = new ListCollectionView(service.GetSources().ToList());
        view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(ScreenSource.Category)));
        SourceList.ItemsSource = view;

        MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); };
    }

    private void SourceList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        Selected = SourceList.SelectedItem as ScreenSource;
        SelectedLabel.Text = Selected is null ? "Nada selecionado" : $"Selecionado: {Selected.Title}";
    }

    private void Share_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is null) { MessageBox.Show("Selecione uma tela ou janela para compartilhar."); return; }
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
