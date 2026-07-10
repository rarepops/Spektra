using Avalonia.Platform.Storage;
using Spektra.Core;

namespace Spektra.App;

/// CSV or JSON is chosen by the saved file's extension; anything that is
/// not .json (including no extension) writes CSV, matching the default.
public static class ReportWriter
{
    public static async Task WriteAsync(IStorageFile file, IReadOnlyList<AuditRow> rows)
    {
        var json = Path.GetExtension(file.Name).Equals(".json", StringComparison.OrdinalIgnoreCase);
        var text = json ? Reporting.ToJson(rows) : Reporting.ToCsv(rows);
        await using var stream = await file.OpenWriteAsync();
        await using var writer = new StreamWriter(stream);
        await writer.WriteAsync(text);
    }
}
