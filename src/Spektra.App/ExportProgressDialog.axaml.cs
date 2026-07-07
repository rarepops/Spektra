using Avalonia.Controls;
using Avalonia.Interactivity;
using Spektra.Core;

namespace Spektra.App;

/// Modal progress for a folder audit. Runs FolderAudit on a background thread,
/// shows per-file progress, and closes itself when done. `Results` is null
/// when the user cancelled (closing the window cancels the analysis).
public partial class ExportProgressDialog : Window
{
    private readonly CancellationTokenSource _cts = new();

    public AuditResult[]? Results { get; private set; }

    public ExportProgressDialog()
    {
        InitializeComponent();
    }

    public ExportProgressDialog(FfmpegPaths ffmpeg, IReadOnlyList<string> files) : this()
    {
        Bar.Maximum = files.Count;
        LabelText.Text = $"Analyzing 0 of {files.Count}…";
        var progress = new Progress<int>(done =>
        {
            Bar.Value = done;
            LabelText.Text = $"Analyzing {done} of {files.Count}…";
        });
        Opened += async (_, _) =>
        {
            try
            {
                var jobs = Math.Max(1, (int)(Environment.ProcessorCount * 0.8));
                Results = await Task.Run(() => FolderAudit.Run(ffmpeg, files, jobs, progress, _cts.Token));
            }
            catch (OperationCanceledException)
            {
                Results = null;
            }
            Close();
        };
        Closing += (_, _) => _cts.Cancel();
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(); // Closing cancels the token
}
