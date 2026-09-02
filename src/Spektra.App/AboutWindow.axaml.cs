using System;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Spektra.Core;

namespace Spektra.App;

public partial class AboutWindow : Window
{
    private const string RepoUrl = "https://github.com/rarepops/Spektra";
    private const string LicenseUrl = "https://github.com/rarepops/Spektra/blob/main/LICENSE.md";

    private readonly string _versionLine;
    private readonly string _runtimeLine;
    private string _ffmpegLine;

    private readonly KeyMap _keys;
    private readonly IReadOnlyList<string> _keyProblems;

    /// Carries the shell's key map through rather than reloading the file, so
    /// the Controls window opened from here and the one opened with F1 can
    /// never describe two different keyboards.
    /// For Avalonia's runtime XAML loader only, which needs a public
    /// parameterless constructor; the shell always passes its own map.
    public AboutWindow() : this(KeyMap.Defaults, []) { }

    public AboutWindow(KeyMap keys, IReadOnlyList<string> keyProblems)
    {
        _keys = keys;
        _keyProblems = keyProblems;
        InitializeComponent();

        var v = Assembly.GetExecutingAssembly().GetName().Version;
        _versionLine = v is null ? "" : "v" + UpdateChecker.FormatVersion(v);
        VersionText.Text = _versionLine;

        _runtimeLine = $"{RuntimeInformation.FrameworkDescription} · {RuntimeInformation.RuntimeIdentifier}";
        RuntimeText.Text = _runtimeLine;

        _ffmpegLine = DescribeFfmpeg(out var ffmpegPath);
        FfmpegText.Text = _ffmpegLine;
        if (ffmpegPath is not null)
            _ = FillFfmpegVersionAsync(ffmpegPath);
    }

    // Where ffmpeg was found, resolved synchronously. The version is appended
    // later by FillFfmpegVersionAsync so opening the window never blocks on a
    // child process.
    private static string DescribeFfmpeg(out string? ffmpegPath)
    {
        var paths = FfmpegLocator.LocateDefault();
        ffmpegPath = paths?.FfmpegPath;
        return paths is null
            ? "not found"
            : FfmpegLocator.ClassifySource(paths.FfmpegPath, AppContext.BaseDirectory, FfmpegLocator.DownloadDir);
    }

    private async Task FillFfmpegVersionAsync(string ffmpegPath)
    {
        var version = await FfmpegVersion.QueryAsync(ffmpegPath);
        if (version is null) return;
        _ffmpegLine = $"{version} · {_ffmpegLine}";
        FfmpegText.Text = _ffmpegLine;
    }

    private async void OnCopyInfo(object? sender, RoutedEventArgs e)
    {
        if (Clipboard is null) return;
        var info = $"Spektra {_versionLine}\n{_runtimeLine}\n{RuntimeInformation.OSDescription}\nffmpeg {_ffmpegLine}";
        var data = new DataTransfer();
        data.Add(DataTransferItem.Create(DataFormat.Text, info));
        await Clipboard.SetDataAsync(data);
    }

    private void OnControls(object? sender, PointerPressedEventArgs e) =>
        _ = new ControlsWindow(_keys, _keyProblems).ShowDialog(this);

    private async void OnGitHub(object? sender, PointerPressedEventArgs e) => await OpenUrl(RepoUrl);

    private async void OnLicense(object? sender, PointerPressedEventArgs e) => await OpenUrl(LicenseUrl);

    private async Task OpenUrl(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
            await Launcher.LaunchUriAsync(uri);
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
