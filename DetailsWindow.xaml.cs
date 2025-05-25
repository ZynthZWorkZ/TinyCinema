using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;

namespace TinyCinema;

public partial class DetailsWindow : Window
{
    public DetailsWindow(string title, string year, string description, string genre, string imageUrl = null)
    {
        InitializeComponent();
        
        // Set window title
        Title = $"{title} ({year}) - Details";
        TitleText.Text = Title;
        
        // Set content
        MovieTitleText.Text = $"{title} ({year})";
        GenreText.Text = $"Genre: {genre}";
        DescriptionText.Text = $"Description:\n{description}";

        // Load background image if provided
        if (!string.IsNullOrEmpty(imageUrl))
        {
            try
            {
                var image = new BitmapImage(new Uri(imageUrl));
                BackgroundImage.Source = image;
            }
            catch (Exception)
            {
                // If image loading fails, the default dark background will be shown
            }
        }
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