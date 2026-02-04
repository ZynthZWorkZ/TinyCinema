using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using HtmlAgilityPack;

namespace TinyCinema;

public partial class DetailsWindow : Window
{
    public DetailsWindow(string title, string year, string description, string genre, string imageUrl = null)
    {
        InitializeComponent();
        
        // Set window title
        Title = $"{title} - Details";
        TitleText.Text = Title;
        
        // Set movie title
        MovieTitleText.Text = title;
        
        // Set year
        YearText.Text = year;
        
        // Set genre - clean up and format the text
        if (!string.IsNullOrWhiteSpace(genre))
        {
            GenreText.Text = FormatGenreInfo(genre);
            GenreSection.Visibility = Visibility.Visible;
            
            // Extract and load cast images asynchronously
            _ = LoadCastImagesAsync(genre);
        }
        else
        {
            GenreSection.Visibility = Visibility.Collapsed;
        }
        
        // Set description
        if (!string.IsNullOrWhiteSpace(description))
        {
            DescriptionText.Text = description;
            DescriptionSection.Visibility = Visibility.Visible;
        }
        else
        {
            DescriptionText.Text = "No description available.";
            DescriptionSection.Visibility = Visibility.Visible;
        }

        // Load poster and background image if provided
        if (!string.IsNullOrEmpty(imageUrl))
        {
            try
            {
                var posterImage = new BitmapImage();
                posterImage.BeginInit();
                posterImage.UriSource = new Uri(imageUrl);
                posterImage.CacheOption = BitmapCacheOption.OnLoad;
                posterImage.EndInit();
                
                // Set poster
                PosterImage.Source = posterImage;
                PosterPlaceholder.Visibility = Visibility.Collapsed;
                
                // Set background (blurred effect will come from the poster)
                var backgroundImage = new BitmapImage();
                backgroundImage.BeginInit();
                backgroundImage.UriSource = new Uri(imageUrl);
                backgroundImage.CacheOption = BitmapCacheOption.OnLoad;
                backgroundImage.EndInit();
                BackgroundImage.Source = backgroundImage;
            }
            catch (Exception)
            {
                // If image loading fails, show placeholder
                PosterPlaceholder.Visibility = Visibility.Visible;
            }
        }
        else
        {
            PosterPlaceholder.Visibility = Visibility.Visible;
        }
    }

    private string FormatGenreInfo(string rawText)
    {
        if (string.IsNullOrWhiteSpace(rawText))
            return string.Empty;

        // Split by common patterns and format with line breaks
        var text = rawText
            .Replace("Released:", "\n• Released:")
            .Replace("Genre:", "\n\n• Genre:")
            .Replace("Casts:", "\n\n• Casts:")
            .Replace("Cast:", "\n\n• Cast:")
            .Replace("Director:", "\n\n• Director:")
            .Replace("Directors:", "\n\n• Directors:")
            .Replace("Duration:", "\n\n• Duration:")
            .Replace("Country:", "\n\n• Country:")
            .Replace("Production:", "\n\n• Production:")
            .Replace("Writer:", "\n\n• Writer:")
            .Replace("Quality:", "\n\n• Quality:")
            .Trim();

        // Remove extra newlines at the start
        while (text.StartsWith("\n"))
        {
            text = text.Substring(1);
        }

        return text;
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

    private async Task LoadCastImagesAsync(string genreInfo)
    {
        try
        {
            // Extract cast names from the genre info
            var castNames = ExtractCastNames(genreInfo);
            if (castNames.Count == 0)
            {
                CastSection.Visibility = Visibility.Collapsed;
                return;
            }

            // Show cast section
            CastSection.Visibility = Visibility.Visible;
            CastImagesPanel.Children.Clear();

            // Load images and details for each cast member (limit to first 6 for performance)
            var tasks = castNames.Take(6).Select(async name =>
            {
                var (imageUrl, details) = await FetchCastImageAndDetailsAsync(name);
                if (!string.IsNullOrEmpty(imageUrl))
                {
                    return (name, imageUrl, details);
                }
                return (name, (string)null, (CastDetails)null);
            });

            var detailResults = await Task.WhenAll(tasks);

            // Add images to UI on the UI thread
            foreach (var (name, imageUrl, details) in detailResults)
            {
                if (!string.IsNullOrEmpty(imageUrl))
                {
                    AddCastMemberToUI(name, imageUrl, details);
                }
            }

            // Hide cast section if no images were found
            if (CastImagesPanel.Children.Count == 0)
            {
                CastSection.Visibility = Visibility.Collapsed;
            }
        }
        catch (Exception)
        {
            CastSection.Visibility = Visibility.Collapsed;
        }
    }

    private List<string> ExtractCastNames(string genreInfo)
    {
        var names = new List<string>();
        try
        {
            // Look for "Cast:" or "Casts:" pattern
            var castMatch = System.Text.RegularExpressions.Regex.Match(genreInfo, @"Casts?:\s*([^\n]+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (castMatch.Success)
            {
                var castString = castMatch.Groups[1].Value;
                // Split by comma and clean up names
                names = castString
                    .Split(new[] { ',', '،' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(n => n.Trim())
                    .Where(n => !string.IsNullOrWhiteSpace(n))
                    .ToList();
            }
        }
        catch
        {
            // Return empty list on error
        }
        return names;
    }

    private async Task<(string imageUrl, CastDetails details)> FetchCastImageAndDetailsAsync(string actorName)
    {
        try
        {
            // Format name as "first-last" for URL
            var formattedName = FormatActorNameForUrl(actorName);
            if (string.IsNullOrEmpty(formattedName))
                return (null, null);

            var url = $"https://www.famousbirthdays.com/people/{formattedName}.html";

            using var client = new HttpClient();
            client.Timeout = TimeSpan.FromSeconds(10);

            // Set headers to mimic browser request
            client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:147.0) Gecko/20100101 Firefox/147.0");
            client.DefaultRequestHeaders.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
            client.DefaultRequestHeaders.Add("Accept-Language", "en-US,en;q=0.9");
            client.DefaultRequestHeaders.Add("Accept-Encoding", "identity");
            client.DefaultRequestHeaders.Add("Upgrade-Insecure-Requests", "1");
            client.DefaultRequestHeaders.Add("Sec-Fetch-Dest", "document");
            client.DefaultRequestHeaders.Add("Sec-Fetch-Mode", "navigate");
            client.DefaultRequestHeaders.Add("Sec-Fetch-Site", "none");
            client.DefaultRequestHeaders.Add("Sec-Fetch-User", "?1");

            var response = await client.GetStringAsync(url);

            // Parse HTML
            var htmlDoc = new HtmlDocument();
            htmlDoc.LoadHtml(response);

            string imageUrl = null;
            var details = new CastDetails();

            // Find the image in the carousel
            var imgNode = htmlDoc.DocumentNode.SelectSingleNode("//div[contains(@class, 'profile-pictures-carousel__slide')]//img");
            if (imgNode != null)
            {
                imageUrl = imgNode.GetAttributeValue("src", null);
            }

            // Extract bio attributes
            var bioModule = htmlDoc.DocumentNode.SelectSingleNode("//div[@class='bio-module__person-attributes']");
            if (bioModule != null)
            {
                var paragraphs = bioModule.SelectNodes(".//p");
                if (paragraphs != null)
                {
                    foreach (var p in paragraphs)
                    {
                        var label = p.SelectSingleNode(".//span[@class='type-16-18']")?.InnerText.Trim();
                        var valueNode = p.SelectNodes(".//span")?[1];
                        
                        if (label != null && valueNode != null)
                        {
                            var value = WebUtility.HtmlDecode(valueNode.InnerText.Trim());
                            
                            if (label.Contains("Birthday"))
                                details.Birthday = value;
                            else if (label.Contains("Birth Sign"))
                                details.BirthSign = value;
                            else if (label.Contains("Birthplace"))
                                details.Birthplace = value;
                            else if (label.Contains("Age"))
                                details.Age = value;
                        }
                    }
                }
            }

            // Extract About section
            var aboutNode = htmlDoc.DocumentNode.SelectSingleNode("//h2[contains(text(), 'About')]/following-sibling::p[1]");
            if (aboutNode != null)
            {
                details.About = WebUtility.HtmlDecode(aboutNode.InnerText.Trim());
            }

            return (imageUrl, details);
        }
        catch
        {
            return (null, null);
        }
    }

    private string FormatActorNameForUrl(string actorName)
    {
        try
        {
            // Remove any extra info in parentheses
            actorName = System.Text.RegularExpressions.Regex.Replace(actorName, @"\([^)]*\)", "").Trim();

            // Split into parts and take only first and last name
            var parts = actorName.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
                return null;

            // Use first and last name only
            var firstName = parts[0].ToLower();
            var lastName = parts[parts.Length - 1].ToLower();

            return $"{firstName}-{lastName}";
        }
        catch
        {
            return null;
        }
    }

    private void AddCastMemberToUI(string name, string imageUrl, CastDetails details)
    {
        try
        {
            var castItem = new Border
            {
                Width = 100,
                Height = 140,
                Margin = new Thickness(0, 0, 12, 0),
                CornerRadius = new CornerRadius(8),
                Background = new SolidColorBrush(Color.FromRgb(26, 26, 26)),
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(Color.FromRgb(42, 42, 42)),
                Cursor = Cursors.Hand
            };

            var grid = new Grid();

            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(100) });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            // Cast image
            var imageBorder = new Border
            {
                CornerRadius = new CornerRadius(8, 8, 0, 0),
                ClipToBounds = true
            };

            var image = new Image
            {
                Stretch = Stretch.UniformToFill,
                Source = new BitmapImage(new Uri(imageUrl))
            };

            imageBorder.Child = image;
            Grid.SetRow(imageBorder, 0);

            // Cast name
            var nameText = new TextBlock
            {
                Text = name.Split(' ')[0], // Show only first name
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(204, 204, 204)),
                TextAlignment = TextAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(4, 0, 4, 0)
            };
            Grid.SetRow(nameText, 1);

            grid.Children.Add(imageBorder);
            grid.Children.Add(nameText);
            castItem.Child = grid;

            // Add tooltip with cast details
            if (details != null)
            {
                var tooltip = CreateCastTooltip(name, details);
                castItem.ToolTip = tooltip;
            }

            CastImagesPanel.Children.Add(castItem);
        }
        catch
        {
            // Skip this cast member if there's an error
        }
    }

    private ToolTip CreateCastTooltip(string name, CastDetails details)
    {
        var tooltip = new ToolTip
        {
            MaxWidth = 350,
            Background = new SolidColorBrush(Color.FromRgb(15, 15, 15)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(42, 42, 42)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(0)
        };

        var border = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(26, 26, 26)),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(16)
        };

        var stackPanel = new StackPanel();

        // Name header
        var nameHeader = new TextBlock
        {
            Text = name,
            FontSize = 16,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Colors.White),
            Margin = new Thickness(0, 0, 0, 12)
        };
        stackPanel.Children.Add(nameHeader);

        // Birthday
        if (!string.IsNullOrEmpty(details.Birthday))
        {
            AddDetailRow(stackPanel, "🎂 Birthday:", details.Birthday);
        }

        // Birth Sign
        if (!string.IsNullOrEmpty(details.BirthSign))
        {
            AddDetailRow(stackPanel, "♈ Birth Sign:", details.BirthSign);
        }

        // Birthplace
        if (!string.IsNullOrEmpty(details.Birthplace))
        {
            AddDetailRow(stackPanel, "📍 Birthplace:", details.Birthplace);
        }

        // Age
        if (!string.IsNullOrEmpty(details.Age))
        {
            AddDetailRow(stackPanel, "👤 Age:", details.Age);
        }

        // About section
        if (!string.IsNullOrEmpty(details.About))
        {
            var aboutHeader = new TextBlock
            {
                Text = "About",
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Colors.White),
                Margin = new Thickness(0, 12, 0, 8)
            };
            stackPanel.Children.Add(aboutHeader);

            var aboutText = new TextBlock
            {
                Text = details.About,
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(204, 204, 204)),
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 18
            };
            stackPanel.Children.Add(aboutText);
        }

        border.Child = stackPanel;
        tooltip.Content = border;

        return tooltip;
    }

    private void AddDetailRow(StackPanel parent, string label, string value)
    {
        var grid = new Grid
        {
            Margin = new Thickness(0, 0, 0, 8)
        };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var labelText = new TextBlock
        {
            Text = label,
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(180, 180, 180))
        };
        Grid.SetColumn(labelText, 0);

        var valueText = new TextBlock
        {
            Text = value,
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromRgb(204, 204, 204)),
            TextWrapping = TextWrapping.Wrap
        };
        Grid.SetColumn(valueText, 1);

        grid.Children.Add(labelText);
        grid.Children.Add(valueText);
        parent.Children.Add(grid);
    }
}

public class CastDetails
{
    public string Birthday { get; set; }
    public string BirthSign { get; set; }
    public string Birthplace { get; set; }
    public string Age { get; set; }
    public string About { get; set; }
} 