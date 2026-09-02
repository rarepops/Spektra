using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Spektra.Core;

namespace Spektra.App;

/// Outcome of a manual update check, and the place an update is fetched from.
/// Download saves the one release file that fits this machine into the user's
/// Downloads folder, verifies it against the release's SHA256SUMS.txt, and only
/// then offers Install (an MSI) or Show in folder (a zip). Nothing is launched
/// that has not passed that check.
public partial class UpdateDialog : Window
{
    private string? _releaseUrl;
    private ReleaseAsset? _asset;
    private ReleaseAsset? _checksums;
    private string? _verifiedPath;
    private CancellationTokenSource? _cts;

    public UpdateDialog()
    {
        InitializeComponent();
    }

    /// `startDownload` begins fetching as soon as the window opens (the banner's
    /// Download button does this); it is ignored when there is nothing to fetch.
    public UpdateDialog(UpdateCheckResult result, string currentVersion, bool startDownload = false) : this()
    {
        switch (result.Outcome)
        {
            case UpdateOutcome.UpdateAvailable when result.Info is { } info:
                _releaseUrl = info.Url;
                _asset = ReleaseAssets.Pick(info.Assets, ReleaseTarget.Current());
                _checksums = ReleaseAssets.Checksums(info.Assets);
                ShowAvailable($"Spektra {UpdateChecker.FormatVersion(info.Latest)} is available. You have {currentVersion}.");
                if (startDownload && DownloadButton.IsVisible)
                    Opened += (_, _) => _ = DownloadAsync();
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

        // Closing the window mid-download (Close, Escape, the title bar) cancels
        // the fetch; the downloader removes its partial file on the way out.
        Closing += (_, _) => _cts?.Cancel();
    }

    private bool CanDownload => _asset is not null && _checksums is not null;

    // --- states ---

    private void ShowAvailable(string message)
    {
        TitleText.Text = "Update available";
        MessageText.Text = CanDownload ? $"{message} {Describe(_asset!)}" : message;
        ProgressPanel.IsVisible = false;
        ViewReleaseButton.IsVisible = !string.IsNullOrEmpty(_releaseUrl);
        DownloadButton.Content = "Download";
        DownloadButton.IsVisible = CanDownload;
        CancelDownloadButton.IsVisible = false;
        InstallButton.IsVisible = false;
        ShowInFolderButton.IsVisible = false;
    }

    private void ShowDownloading()
    {
        TitleText.Text = "Downloading update";
        MessageText.Text = $"Saving {_asset!.Name} to your Downloads folder.";
        DownloadBar.IsIndeterminate = true;
        DownloadBar.Value = 0;
        ProgressText.Text = "Starting…";
        ProgressPanel.IsVisible = true;
        ViewReleaseButton.IsVisible = false;
        DownloadButton.IsVisible = false;
        CancelDownloadButton.IsVisible = true;
    }

    private void OnProgress(DownloadProgress p)
    {
        if (p.Total > 0)
        {
            DownloadBar.IsIndeterminate = false;
            DownloadBar.Value = (double)p.Done / p.Total;
            ProgressText.Text = $"{Mb(p.Done)} of {Mb(p.Total)}";
        }
        else
        {
            ProgressText.Text = Mb(p.Done);
        }
    }

    private void ShowVerified(string path, bool wasAlreadyThere)
    {
        _verifiedPath = path;
        var name = Path.GetFileName(path);
        var installer = path.EndsWith(".msi", StringComparison.OrdinalIgnoreCase);
        TitleText.Text = installer ? "Ready to install" : "Download complete";
        MessageText.Text = (wasAlreadyThere
                ? $"{name} was already in your Downloads folder and matches the release's checksum."
                : $"{name} was saved to your Downloads folder and matches the release's checksum.")
            + (installer ? " Install closes Spektra and starts the setup." : "");
        ProgressPanel.IsVisible = false;
        CancelDownloadButton.IsVisible = false;
        InstallButton.IsVisible = installer;
        ShowInFolderButton.IsVisible = !installer;
    }

    private void ShowFailed(string message)
    {
        TitleText.Text = "Couldn't download the update";
        MessageText.Text = message;
        ProgressPanel.IsVisible = false;
        CancelDownloadButton.IsVisible = false;
        ViewReleaseButton.IsVisible = !string.IsNullOrEmpty(_releaseUrl);
        DownloadButton.Content = "Try again";
        DownloadButton.IsVisible = CanDownload;
    }

    private static string Describe(ReleaseAsset asset) =>
        asset.Size > 0 ? $"Download is {asset.Name} ({Mb(asset.Size)})." : $"Download is {asset.Name}.";

    private static string Mb(long bytes) => $"{bytes / 1048576.0:0.#} MB";

    // --- the fetch ---

    private async Task DownloadAsync()
    {
        if (!CanDownload || _cts is not null) return;
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        ShowDownloading();
        var folder = ReleaseDownloader.DownloadsFolder();
        var path = Path.Combine(folder, _asset!.Name);
        try
        {
            var http = ReleaseDownloader.DefaultClient;
            var sums = Sha256Sums.Parse(await ReleaseDownloader.FetchTextAsync(http, _checksums!.Url, ct));
            if (!sums.TryGetValue(_asset.Name, out var expected))
            {
                ShowFailed($"The release's checksum list has no entry for {_asset.Name}, so the download can't be verified.");
                return;
            }

            Directory.CreateDirectory(folder);
            if (await ReleaseDownloader.IsVerifiedAsync(path, expected, ct))
            {
                ShowVerified(path, wasAlreadyThere: true);
                return;
            }

            // Progress<T> posts to the UI thread it was created on.
            var progress = new Progress<DownloadProgress>(OnProgress);
            var outcome = await ReleaseDownloader.DownloadVerifiedAsync(http, _asset, path, expected, progress, ct);
            if (outcome == DownloadOutcome.Verified)
                ShowVerified(path, wasAlreadyThere: false);
            else
                ShowFailed("The downloaded file did not match the release's checksum, so it was deleted. "
                           + "Try again, or download from the release page.");
        }
        catch (OperationCanceledException)
        {
            ShowAvailable("Download cancelled.");
        }
        catch (Exception e) when (e is HttpRequestException or IOException or UnauthorizedAccessException)
        {
            ShowFailed($"Download failed: {e.Message}");
        }
        finally
        {
            _cts.Dispose();
            _cts = null;
        }
    }

    // --- buttons ---

    private async void OnViewRelease(object? sender, RoutedEventArgs e)
    {
        if (_releaseUrl is { Length: > 0 } url && Uri.TryCreate(url, UriKind.Absolute, out var uri))
            await Launcher.LaunchUriAsync(uri);
        Close();
    }

    private void OnDownload(object? sender, RoutedEventArgs e) => _ = DownloadAsync();

    private void OnCancelDownload(object? sender, RoutedEventArgs e) => _cts?.Cancel();

    private async void OnInstall(object? sender, RoutedEventArgs e)
    {
        if (_verifiedPath is null) return;
        if (!await Launcher.LaunchFileInfoAsync(new FileInfo(_verifiedPath)))
        {
            ShowFailed($"Windows didn't open {Path.GetFileName(_verifiedPath)}. It is in your Downloads folder; run it from there.");
            ShowInFolderButton.IsVisible = true;
            return;
        }

        // The installer replaces files this process has open, so leave before it
        // gets there. Shutting the application down closes every window through
        // its normal closing path, which is where the session is saved.
        Close();
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.Shutdown();
        else
            (Owner as Window)?.Close();
    }

    private async void OnShowInFolder(object? sender, RoutedEventArgs e)
    {
        if (_verifiedPath is null) return;
        if (OperatingSystem.IsWindows())
            Process.Start("explorer.exe", $"/select,\"{_verifiedPath}\"");
        else
            await Launcher.LaunchDirectoryInfoAsync(new DirectoryInfo(Path.GetDirectoryName(_verifiedPath)!));
        Close();
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
