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

    public AuditEntry[]? Results { get; private set; }

    public ExportProgressDialog()
    {
        InitializeComponent();
    }

    public ExportProgressDialog(FfmpegPaths ffmpeg, IReadOnlyList<AuditTarget> targets, string? pruneFolder) : this()
    {
        Bar.Maximum = targets.Count;
        LabelText.Text = $"Analyzing 0 of {targets.Count}…";
        var done = 0;
        var progress = new Progress<AuditEntry>(_ =>
        {
            done++;
            Bar.Value = done;
            LabelText.Text = $"Analyzing {done} of {targets.Count}…";
        });
        Opened += async (_, _) =>
        {
            AuditCache? cache = null;
            try { cache = AuditCache.Open(AuditCache.DefaultPath); }
            catch (Exception e) when (e is not OutOfMemoryException) { cache = null; }
            try
            {
                var jobs = Math.Max(1, (int)(Environment.ProcessorCount * 0.8));
                Results = await Task.Run(() => FolderAudit.Run(ffmpeg, targets, jobs, cache, false, progress, _cts.Token));
                if (Results is not null && cache is not null && pruneFolder is not null)
                    cache.PruneFolder(pruneFolder, targets.Select(t => t.Path).ToList());
            }
            catch (OperationCanceledException)
            {
                Results = null;
            }
            finally
            {
                cache?.Dispose();
            }
            Close();
        };
        Closing += (_, _) => _cts.Cancel();
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(); // Closing cancels the token
}
