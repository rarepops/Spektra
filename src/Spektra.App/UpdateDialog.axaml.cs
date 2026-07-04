using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Spektra.Core;

namespace Spektra.App;

public partial class UpdateDialog : Window
{
    private string? _releaseUrl;

    public UpdateDialog()
    {
        InitializeComponent();
    }

    public UpdateDialog(UpdateCheckResult result, string currentVersion) : this()
    {
        switch (result.Outcome)
        {
            case UpdateOutcome.UpdateAvailable when result.Info is { } info:
                TitleText.Text = "Update available";
                MessageText.Text =
                    $"Spektra {Fmt(info.Latest)} is available. You have {currentVersion}.";
                _releaseUrl = info.Url;
                ViewReleaseButton.IsVisible = !string.IsNullOrEmpty(info.Url);
                break;
            case UpdateOutcome.CheckFailed:
                TitleText.Text = "Couldn't check for updates";
                MessageText.Text =
                    "Spektra couldn't reach GitHub. Check your internet connection and try again.";
                break;
            default:
                TitleText.Text = "You're up to date";
                MessageText.Text = $"Spektra {currentVersion} is the latest version.";
                break;
        }
    }

    private static string Fmt(Version v) => $"{v.Major}.{v.Minor}.{Math.Max(0, v.Build)}";

    private async void OnViewRelease(object? sender, RoutedEventArgs e)
    {
        if (_releaseUrl is { Length: > 0 } url && Uri.TryCreate(url, UriKind.Absolute, out var uri))
            await Launcher.LaunchUriAsync(uri);
        Close();
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
