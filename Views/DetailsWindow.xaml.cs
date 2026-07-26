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
    public DetailsWindow(
        string title,
        string year,
        string description,
        string detailsInfo,
        IReadOnlyList<string>? cast = null,
        string? imageUrl = null,
        bool isTvShow = false)
    {
        InitializeComponent();
        
        Title = isTvShow ? $"{title} - TV Show Details" : $"{title} - Details";
        TitleText.Text = isTvShow ? "TV Show Details" : "Movie Details";
        
        MovieTitleText.Text = title;
        YearText.Text = year;
        
        if (!string.IsNullOrWhiteSpace(detailsInfo))
        {
            GenreText.Text = FormatDetailsInfo(detailsInfo);
            GenreSection.Visibility = Visibility.Visible;
        }
        else
        {
            GenreSection.Visibility = Visibility.Collapsed;
        }

        if (!isTvShow)
        {
            var castNames = cast?
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Select(name => name.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList() ?? [];

            if (castNames.Count > 0)
                _ = LoadCastImagesAsync(castNames);
            else
                CastSection.Visibility = Visibility.Collapsed;
        }
        else
        {
            CastSection.Visibility = Visibility.Collapsed;
        }
        
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

        if (!string.IsNullOrEmpty(imageUrl))
        {
            try
            {
                var posterImage = new BitmapImage();
                posterImage.BeginInit();
                posterImage.UriSource = new Uri(imageUrl);
                posterImage.CacheOption = BitmapCacheOption.OnLoad;
                posterImage.EndInit();
                
                PosterImage.Source = posterImage;
                PosterPlaceholder.Visibility = Visibility.Collapsed;
                
                var backgroundImage = new BitmapImage();
                backgroundImage.BeginInit();
                backgroundImage.UriSource = new Uri(imageUrl);
                backgroundImage.CacheOption = BitmapCacheOption.OnLoad;
                backgroundImage.EndInit();
                BackgroundImage.Source = backgroundImage;
            }
            catch (Exception)
            {
                PosterPlaceholder.Visibility = Visibility.Visible;
            }
        }
        else
        {
            PosterPlaceholder.Visibility = Visibility.Visible;
        }
    }

    private static string FormatDetailsInfo(string rawText)
    {
        if (string.IsNullOrWhiteSpace(rawText))
            return string.Empty;

        var text = rawText
            .Replace("Released:", "\n• Released:")
            .Replace("Genre:", "\n\n• Genre:")
            .Replace("Duration:", "\n\n• Duration:")
            .Replace("Seasons:", "\n\n• Seasons:")
            .Replace("Country:", "\n\n• Country:")
            .Replace("Director:", "\n\n• Director:")
            .Replace("Directors:", "\n\n• Directors:")
            .Replace("Production:", "\n\n• Production:")
            .Replace("Writer:", "\n\n• Writer:")
            .Replace("Quality:", "\n\n• Quality:")
            .Trim();

        while (text.StartsWith('\n'))
            text = text[1..];

        return text;
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
            DragMove();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private async Task LoadCastImagesAsync(IReadOnlyList<string> castNames)
    {
        try
        {
            if (castNames.Count == 0)
            {
                CastSection.Visibility = Visibility.Collapsed;
                return;
            }

            CastSection.Visibility = Visibility.Visible;
            CastImagesPanel.Children.Clear();

            var tasks = castNames.Take(6).Select(async name =>
            {
                var (imageUrl, details) = await FetchCastImageAndDetailsAsync(name);
                return !string.IsNullOrEmpty(imageUrl)
                    ? (name, imageUrl, details)
                    : (name, (string?)null, (CastDetails?)null);
            });

            var detailResults = await Task.WhenAll(tasks);

            foreach (var (name, imageUrl, details) in detailResults)
            {
                if (!string.IsNullOrEmpty(imageUrl))
                    AddCastMemberToUI(name, imageUrl, details);
            }

            if (CastImagesPanel.Children.Count == 0)
                CastSection.Visibility = Visibility.Collapsed;
        }
        catch (Exception)
        {
            CastSection.Visibility = Visibility.Collapsed;
        }
    }

    private async Task<(string? imageUrl, CastDetails? details)> FetchCastImageAndDetailsAsync(string actorName)
    {
        try
        {
            var formattedName = FormatActorNameForUrl(actorName);
            if (string.IsNullOrEmpty(formattedName))
                return (null, null);

            var url = $"https://www.famousbirthdays.com/people/{formattedName}.html";

            using var client = new HttpClient();
            client.Timeout = TimeSpan.FromSeconds(10);
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

            var htmlDoc = new HtmlDocument();
            htmlDoc.LoadHtml(response);

            string? imageUrl = null;
            var details = new CastDetails();

            var imgNode = htmlDoc.DocumentNode.SelectSingleNode("//div[contains(@class, 'profile-pictures-carousel__slide')]//img");
            if (imgNode != null)
                imageUrl = imgNode.GetAttributeValue("src", null);

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
                        
                        if (label == null || valueNode == null)
                            continue;

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

            var aboutNode = htmlDoc.DocumentNode.SelectSingleNode("//h2[contains(text(), 'About')]/following-sibling::p[1]");
            if (aboutNode != null)
                details.About = WebUtility.HtmlDecode(aboutNode.InnerText.Trim());

            return (imageUrl, details);
        }
        catch
        {
            return (null, null);
        }
    }

    private static string? FormatActorNameForUrl(string actorName)
    {
        try
        {
            actorName = System.Text.RegularExpressions.Regex.Replace(actorName, @"\([^)]*\)", "").Trim();

            var parts = actorName.Split([' '], StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
                return null;

            var firstName = parts[0].ToLowerInvariant();
            var lastName = parts[^1].ToLowerInvariant();

            return $"{firstName}-{lastName}";
        }
        catch
        {
            return null;
        }
    }

    private void AddCastMemberToUI(string name, string imageUrl, CastDetails? details)
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

            var imageBorder = new Border
            {
                CornerRadius = new CornerRadius(8, 8, 0, 0),
                ClipToBounds = true,
                Child = new Image
                {
                    Stretch = Stretch.UniformToFill,
                    Source = new BitmapImage(new Uri(imageUrl))
                }
            };
            Grid.SetRow(imageBorder, 0);

            var nameText = new TextBlock
            {
                Text = name.Split(' ')[0],
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

            if (details != null)
                castItem.ToolTip = CreateCastTooltip(name, details);

            CastImagesPanel.Children.Add(castItem);
        }
        catch
        {
            // Skip cast member on render error.
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
        stackPanel.Children.Add(new TextBlock
        {
            Text = name,
            FontSize = 16,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Colors.White),
            Margin = new Thickness(0, 0, 0, 12)
        });

        if (!string.IsNullOrEmpty(details.Birthday))
            AddDetailRow(stackPanel, "Birthday:", details.Birthday);
        if (!string.IsNullOrEmpty(details.BirthSign))
            AddDetailRow(stackPanel, "Birth Sign:", details.BirthSign);
        if (!string.IsNullOrEmpty(details.Birthplace))
            AddDetailRow(stackPanel, "Birthplace:", details.Birthplace);
        if (!string.IsNullOrEmpty(details.Age))
            AddDetailRow(stackPanel, "Age:", details.Age);

        if (!string.IsNullOrEmpty(details.About))
        {
            stackPanel.Children.Add(new TextBlock
            {
                Text = "About",
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Colors.White),
                Margin = new Thickness(0, 12, 0, 8)
            });
            stackPanel.Children.Add(new TextBlock
            {
                Text = details.About,
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(204, 204, 204)),
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 18
            });
        }

        border.Child = stackPanel;
        tooltip.Content = border;
        return tooltip;
    }

    private static void AddDetailRow(StackPanel parent, string label, string value)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 8) };
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
    public string Birthday { get; set; } = string.Empty;
    public string BirthSign { get; set; } = string.Empty;
    public string Birthplace { get; set; } = string.Empty;
    public string Age { get; set; } = string.Empty;
    public string About { get; set; } = string.Empty;
}
