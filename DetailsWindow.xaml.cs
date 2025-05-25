using System;
using System.Windows;
using System.Windows.Input;

namespace TinyCinema;

public partial class DetailsWindow : Window
{
    public DetailsWindow(string title, string year, string description, string genre)
    {
        InitializeComponent();
        
        // Set window title
        Title = $"{title} ({year}) - Details";
        TitleText.Text = Title;
        
        // Set content
        MovieTitleText.Text = $"{title} ({year})";
        GenreText.Text = $"Genre: {genre}";
        DescriptionText.Text = $"Description:\n{description}";
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            DragMove();
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
} 