using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using NyxarConcord.Models;

namespace NyxarConcord.Views;

public partial class CreateRoomDialog : Window
{
    public string RoomNameText => RoomName.Text.Trim();
    public RoomKind SelectedKind => AudioRadio.IsChecked == true ? RoomKind.Audio : RoomKind.Text;

    /// <summary>Chave do ícone colorido escolhido (guardada em Room.Emoji).</summary>
    public string Emoji { get; private set; } = "chat";

    public CreateRoomDialog()
    {
        InitializeComponent();
        BuildIconGrid();
        SelectIcon("chat");
        RoomName.Focus();
        RoomName.SelectAll();
        MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); };
    }

    private void BuildIconGrid()
    {
        foreach (var def in ChannelIcons.All)
        {
            var icon = new System.Windows.Shapes.Path
            {
                Data = Geometry.Parse(def.Path),
                Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString(def.Color)),
                Width = 22,
                Height = 22,
                Stretch = Stretch.Uniform
            };
            var btn = new Button
            {
                Content = icon,
                Width = 40,
                Height = 40,
                Margin = new Thickness(3),
                Cursor = Cursors.Hand,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Tag = def.Key
            };
            btn.Click += Icon_Click;
            EmojiPanel.Children.Add(btn);
        }
    }

    private void Icon_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string key }) SelectIcon(key);
    }

    private void SelectIcon(string key)
    {
        Emoji = key;
        var def = ChannelIcons.Find(key);
        EmojiPreview.Data = Geometry.Parse(def.Path);
        EmojiPreview.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString(def.Color));
    }

    private void Create_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(RoomNameText))
        {
            MessageBox.Show("Dê um nome para a sala.");
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
