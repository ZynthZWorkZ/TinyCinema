using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace TinyCinema;

public partial class RandomPickOverlay : UserControl
{
    private Storyboard? _diceSpinStoryboard;
    private TaskCompletionSource<RandomPickAction>? _actionTcs;
    private Movie? _currentPick;

    public event Action<Movie>? PreviewRequested;

    public RandomPickOverlay()
    {
        InitializeComponent();
    }

    public async Task<RandomPickResult> RollAndPickAsync(
        IReadOnlyList<Movie> candidates,
        string rollingLabel,
        CancellationToken cancellationToken = default)
    {
        if (candidates.Count == 0)
            return new RandomPickResult { Action = RandomPickAction.Cancelled };

        Visibility = Visibility.Visible;
        Opacity = 0;
        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(280))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        });

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var pick = await AnimateRollAsync(candidates, rollingLabel, cancellationToken);
            _currentPick = pick;

            ShowPickDetails(pick);
            var action = await WaitForUserActionAsync(cancellationToken);

            if (action == RandomPickAction.RollAgain)
            {
                PrepareForReroll(rollingLabel);
                continue;
            }

            await HideAsync(cancellationToken);
            return new RandomPickResult
            {
                Action = action,
                Movie = action == RandomPickAction.Watch ? pick : null
            };
        }
    }

    private async Task<Movie> AnimateRollAsync(
        IReadOnlyList<Movie> candidates,
        string rollingLabel,
        CancellationToken cancellationToken)
    {
        var random = Random.Shared;
        var finalPick = candidates[random.Next(candidates.Count)];

        DiceHeader.Visibility = Visibility.Visible;
        ActionPanel.Visibility = Visibility.Collapsed;
        RevealCard.Opacity = 0.35;
        StatusText.Text = rollingLabel;
        ClearDetails();

        StartDiceSpin();

        var endTime = DateTime.UtcNow.AddMilliseconds(2600);
        var delayMs = 45;
        while (DateTime.UtcNow < endTime)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var teaser = candidates[random.Next(candidates.Count)];
            _ = teaser.LoadImageAsync();
            UpdateReveal(teaser, isTeaser: true);
            await Task.Delay(delayMs, cancellationToken);
            delayMs = Math.Min(delayMs + 10, 190);
        }

        StopDiceSpin();
        await finalPick.LoadImageAsync();
        UpdateReveal(finalPick, isTeaser: false);

        StatusText.Text = finalPick.IsTvShow ? "Your random TV show!" : "Your random movie!";
        AnimateRevealPop();
        PulseDice();

        await Task.Delay(500, cancellationToken);
        return finalPick;
    }

    private void ShowPickDetails(Movie movie)
    {
        DiceHeader.Visibility = Visibility.Collapsed;
        StatusText.Text = movie.IsTvShow ? "Random TV show picked" : "Random movie picked";
        UpdateReveal(movie, isTeaser: false);
        UpdateDetails(movie);
        PreviewButton.Content = movie.IsTvShow ? "Opening Credits" : "Trailer";
        PreviewButton.Visibility = Visibility.Visible;
        ActionPanel.Visibility = Visibility.Visible;
    }

    private void PrepareForReroll(string rollingLabel)
    {
        _actionTcs = null;
        DiceHeader.Visibility = Visibility.Visible;
        ActionPanel.Visibility = Visibility.Collapsed;
        PreviewButton.Visibility = Visibility.Collapsed;
        RevealCard.Opacity = 0.35;
        StatusText.Text = rollingLabel;
        ClearDetails();
    }

    private async Task<RandomPickAction> WaitForUserActionAsync(CancellationToken cancellationToken)
    {
        _actionTcs = new TaskCompletionSource<RandomPickAction>(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var registration = cancellationToken.Register(() =>
            _actionTcs.TrySetResult(RandomPickAction.Cancelled));

        return await _actionTcs.Task;
    }

    private void WatchButton_Click(object sender, RoutedEventArgs e)
    {
        _actionTcs?.TrySetResult(RandomPickAction.Watch);
    }

    private void RollAgainButton_Click(object sender, RoutedEventArgs e)
    {
        _actionTcs?.TrySetResult(RandomPickAction.RollAgain);
    }

    private void PreviewButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentPick != null)
            PreviewRequested?.Invoke(_currentPick);
    }

    private void RootGrid_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (ActionPanel.Visibility != Visibility.Visible)
            return;

        _actionTcs?.TrySetResult(RandomPickAction.Cancelled);
    }

    private void Card_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
    }

    private void ClearDetails()
    {
        TitleText.Text = string.Empty;
        SubtitleText.Text = string.Empty;
        DirectorText.Text = string.Empty;
        CastText.Text = string.Empty;
        DescriptionText.Text = string.Empty;
        PosterImage.Source = null;
    }

    private void UpdateReveal(Movie movie, bool isTeaser)
    {
        TitleText.Text = movie.Title;
        SubtitleText.Text = BuildSubtitle(movie);
        RevealCard.Opacity = isTeaser ? 0.55 : 1;
        PosterImage.Source = movie.CachedImage;
    }

    private void UpdateDetails(Movie movie)
    {
        DirectorText.Text = string.IsNullOrWhiteSpace(movie.Director)
            ? string.Empty
            : $"Director: {movie.Director}";

        DirectorText.Visibility = string.IsNullOrWhiteSpace(DirectorText.Text)
            ? Visibility.Collapsed
            : Visibility.Visible;

        CastText.Text = movie.Cast.Count > 0
            ? $"Cast: {string.Join(", ", movie.Cast.Take(6))}"
            : string.Empty;

        CastText.Visibility = string.IsNullOrWhiteSpace(CastText.Text)
            ? Visibility.Collapsed
            : Visibility.Visible;

        if (!string.IsNullOrWhiteSpace(movie.Description))
        {
            DescriptionText.Text = movie.Description.Length > 420
                ? movie.Description[..420] + "…"
                : movie.Description;
        }
        else
        {
            DescriptionText.Text = movie.IsTvShow
                ? "No description in catalog — hit Watch to start this show."
                : "No description in catalog — hit Watch to start this movie.";
        }
    }

    private static string BuildSubtitle(Movie movie)
    {
        var parts = new List<string>();

        if (!string.IsNullOrWhiteSpace(movie.Year) &&
            !movie.Year.Equals("Unknown", StringComparison.OrdinalIgnoreCase))
        {
            parts.Add(movie.Year);
        }

        parts.Add(movie.IsTvShow ? "TV Show" : "Movie");

        if (!string.IsNullOrWhiteSpace(movie.Duration) &&
            !movie.Duration.Equals("Unknown", StringComparison.OrdinalIgnoreCase))
        {
            parts.Add(movie.Duration);
        }

        if (!string.IsNullOrWhiteSpace(movie.Genre) &&
            !movie.Genre.Equals("Unknown", StringComparison.OrdinalIgnoreCase))
        {
            parts.Add(movie.Genre);
        }

        if (!string.IsNullOrWhiteSpace(movie.Country) &&
            !movie.Country.Equals("Unknown", StringComparison.OrdinalIgnoreCase))
        {
            parts.Add(movie.Country);
        }

        return string.Join(" · ", parts);
    }

    private async Task HideAsync(CancellationToken cancellationToken)
    {
        BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(220)));
        await Task.Delay(240, cancellationToken);
        Visibility = Visibility.Collapsed;
        StopDiceSpin();
        ActionPanel.Visibility = Visibility.Collapsed;
        _actionTcs = null;
        _currentPick = null;
    }

    private void StartDiceSpin()
    {
        StopDiceSpin();

        var rotate = new DoubleAnimation
        {
            From = 0,
            To = 360,
            Duration = TimeSpan.FromMilliseconds(650),
            RepeatBehavior = RepeatBehavior.Forever
        };

        var wobble = new DoubleAnimationUsingKeyFrames { RepeatBehavior = RepeatBehavior.Forever };
        wobble.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        wobble.KeyFrames.Add(new LinearDoubleKeyFrame(1.12, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(325))));
        wobble.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(650))));

        _diceSpinStoryboard = new Storyboard();
        Storyboard.SetTarget(rotate, DiceRotate);
        Storyboard.SetTargetProperty(rotate, new PropertyPath(RotateTransform.AngleProperty));
        Storyboard.SetTarget(wobble, DiceScale);
        Storyboard.SetTargetProperty(wobble, new PropertyPath(ScaleTransform.ScaleXProperty));
        _diceSpinStoryboard.Children.Add(rotate);

        var wobbleY = wobble.Clone();
        Storyboard.SetTarget(wobbleY, DiceScale);
        Storyboard.SetTargetProperty(wobbleY, new PropertyPath(ScaleTransform.ScaleYProperty));
        _diceSpinStoryboard.Children.Add(wobbleY);

        _diceSpinStoryboard.Begin();
    }

    private void StopDiceSpin()
    {
        _diceSpinStoryboard?.Stop();
        _diceSpinStoryboard = null;
        DiceRotate.Angle = 0;
        DiceScale.ScaleX = 1;
        DiceScale.ScaleY = 1;
    }

    private void AnimateRevealPop()
    {
        var scaleX = new DoubleAnimation(0.94, 1, TimeSpan.FromMilliseconds(420))
        {
            EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.45 }
        };
        var scaleY = scaleX.Clone();
        RevealScale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleX);
        RevealScale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleY);
    }

    private void PulseDice()
    {
        var pulse = new DoubleAnimation(1, 1.18, TimeSpan.FromMilliseconds(260))
        {
            AutoReverse = true,
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };
        var pulseY = pulse.Clone();
        DiceScale.BeginAnimation(ScaleTransform.ScaleXProperty, pulse);
        DiceScale.BeginAnimation(ScaleTransform.ScaleYProperty, pulseY);
    }
}
