using Avalonia.Platform.Storage;
using Spektra.Core;

namespace Spektra.App;

/// CSV, JSON, or HTML is chosen by the saved file's extension; anything that
/// is not .json or .html (including no extension) writes CSV, matching the
/// default.
public static class ReportWriter
{
    public static async Task WriteAsync(IStorageFile file, IReadOnlyList<AuditRow> rows)
    {
        var ext = Path.GetExtension(file.Name);
        var text = ext.Equals(".json", StringComparison.OrdinalIgnoreCase) ? Reporting.ToJson(rows)
            : ext.Equals(".html", StringComparison.OrdinalIgnoreCase)
                ? HtmlReport.AuditDocument(rows, Path.GetFileNameWithoutExtension(file.Name))
                : Reporting.ToCsv(rows);
        await using var stream = await file.OpenWriteAsync();
        await using var writer = new StreamWriter(stream);
        await writer.WriteAsync(text);
    }
}
