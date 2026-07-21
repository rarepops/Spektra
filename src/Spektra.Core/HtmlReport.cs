using System.Text;

namespace Spektra.Core;

/// Self-contained HTML reports: one file, inline CSS, no external requests,
/// dark theme matching the app (background #111, the tree-marker palette for
/// verdict colors). Three document kinds share the scaffold: the Duplicate
/// Detective duplicate groups, the folder-audit table (AuditDocument, added
/// beside DupesDocument), and the Folder Manifest collapsible tree (ManifestDocument).
/// Every data-derived string goes through Escape; nothing from a file name or
/// tag may reach the page raw.
public static class HtmlReport
{
    public static string DupesDocument(DupesResult result, string title)
    {
        var body = new StringBuilder();
        body.Append($"<h1>{Escape(title)}</h1>");
        body.Append($"<p class=\"gen\">generated {DateTime.Now:yyyy-MM-dd HH:mm} by Spektra Duplicate Detective · view-only report</p>");
        body.Append("<div class=\"stats\">");
        body.Append($"<span class=\"stat\"><b>{result.Groups.Count}</b>groups</span>");
        body.Append($"<span class=\"stat\"><b>{result.Groups.Sum(g => g.Group.Members.Count)}</b>duplicate files</span>");
        body.Append($"<span class=\"stat\"><b>{Bytes(result.ReclaimableBytes)}</b>reclaimable</span>");
        body.Append($"<span class=\"stat\"><b>{result.FilesScanned}</b>scanned</span>");
        body.Append("</div>");

        foreach (var g in result.Groups)
        {
            var open = g.Group.Members.Count <= 3 ? " open" : "";
            body.Append($"<details{open}><summary>{Escape(g.Group.Label)}");
            body.Append($" · {g.Group.Members.Count} files · <span class=\"badge\">sameness {Escape(g.Group.SamenessTier)}</span>");
            body.Append($" · reclaim {Bytes(g.ReclaimableBytes)}</summary>");
            body.Append("<table><tbody>");
            foreach (var m in g.Group.Members)
            {
                var row = g.Rows[m.Path];
                var winner = g.Quality.Winners.Contains(m.Path);
                var cutoff = row.CutoffHz is { } c ? $" {c / 1000.0:0.0}k" : "";
                body.Append("<tr>");
                body.Append($"<td class=\"win\">{(winner ? "★" : "")}</td>");
                body.Append($"<td>{Escape(m.Path)}</td>");
                body.Append($"<td>{Escape(row.Codec)}</td>");
                body.Append($"<td>{Escape(row.Bandwidth)}{cutoff}</td>");
                body.Append($"<td class=\"{IntegrityClass(row.Integrity)}\">{Escape(row.Integrity)}</td>");
                body.Append($"<td>{Bytes(g.Sizes[m.Path])}</td>");
                body.Append($"<td>sameness {m.Sameness:0.00}</td>");
                body.Append($"<td>{(m.FoundByAudio ? "<span class=\"audio\">found by audio</span>" : "")}</td>");
                body.Append("</tr>");
            }
            body.Append("</tbody></table>");
            body.Append($"<p class=\"quality\">quality {Escape(g.Quality.Confidence)}: {Escape(g.Quality.Reason)}</p>");
            body.Append("</details>");
        }

        if (result.NotAnalyzed.Count > 0)
        {
            body.Append($"<details><summary>{result.NotAnalyzed.Count} file(s) not analyzed</summary><table><tbody>");
            foreach (var n in result.NotAnalyzed)
                body.Append($"<tr><td>{Escape(n.Path)}</td><td>{Escape(n.Reason)}</td></tr>");
            body.Append("</tbody></table></details>");
        }

        return Page(title, body.ToString());
    }

    public static string AuditDocument(IReadOnlyList<AuditRow> rows, string title)
    {
        var body = new StringBuilder();
        body.Append($"<h1>{Escape(title)}</h1>");
        body.Append($"<p class=\"gen\">generated {DateTime.Now:yyyy-MM-dd HH:mm} by Spektra · {rows.Count} file(s)</p>");
        body.Append("<table><thead><tr>");
        foreach (var h in (string[])["File", "Codec", "Bandwidth", "Cutoff kHz", "Integrity", "Errors", "Dropouts"])
            body.Append($"<th onclick=\"sortBy(this)\">{h}</th>");
        body.Append("</tr></thead><tbody>");
        foreach (var row in rows)
        {
            var sev = (int)FolderAudit.RowSeverityOf(row);
            var sevClass = sev == 2 ? "bad" : sev == 1 ? "sus" : "ok";
            body.Append($"<tr data-sev=\"{sev}\">");
            body.Append($"<td>{Escape(row.File)}</td>");
            body.Append($"<td>{Escape(row.Codec)}</td>");
            body.Append($"<td class=\"{(row.Bandwidth is "Upsampled" ? "up" : sevClass)}\">{Escape(row.Bandwidth)}</td>");
            body.Append($"<td data-v=\"{row.CutoffHz ?? 0}\">{(row.CutoffHz is { } c ? $"{c / 1000.0:0.0}" : "")}</td>");
            body.Append($"<td class=\"{IntegrityClass(row.Integrity)}\">{Escape(row.Integrity)}</td>");
            body.Append($"<td>{row.DecodeErrors}</td><td>{row.Dropouts}</td>");
            body.Append("</tr>");
        }
        body.Append("</tbody></table>");
        return Page(title, body.ToString(), SortScript);
    }

    /// Folder Manifest: one collapsible tree, folders as nested details/summary
    /// (root and its direct children open, deeper levels start closed), files
    /// as rows with a kind chip colored by cached severity.
    public static string ManifestDocument(ManifestFolder root, string title)
    {
        var body = new StringBuilder();
        body.Append($"<h1>{Escape(title)}</h1>");
        body.Append($"<p class=\"gen\">generated {DateTime.Now:yyyy-MM-dd HH:mm} by Spektra Folder Manifest · view-only report</p>");
        body.Append($"<p class=\"gen\">{Escape(root.Path)}</p>");
        AppendManifestFolder(body, root, depth: 0);
        return Page(title, body.ToString());
    }

    private static void AppendManifestFolder(StringBuilder body, ManifestFolder f, int depth)
    {
        body.Append(depth <= 1 ? "<details open>" : "<details>");
        // The badge carries the recursive byte total when there is one;
        // "empty · 0.0 KB" and "unreadable · 0.0 KB" would be noise.
        var badge = f.TotalBytes > 0 ? $"{f.Rollup} · {Bytes(f.TotalBytes)}" : f.Rollup;
        body.Append($"<summary>{Escape(f.Name)} <span class=\"badge\">{Escape(badge)}</span></summary>");
        foreach (var sub in f.Folders) AppendManifestFolder(body, sub, depth + 1);
        foreach (var file in f.Files)
        {
            var cls = file.Severity switch
            {
                RowSeverity.Problem => "kind bad",
                RowSeverity.Suspect => "kind sus",
                RowSeverity.Clean => "kind ok",
                _ => "kind",
            };
            body.Append($"<div class=\"manifest-file\">{Escape(file.Name)} <span class=\"{cls}\">{Escape(file.Kind)}</span> <span class=\"manifest-size\">{Bytes(file.SizeBytes)}</span></div>");
        }
        body.Append("</details>");
    }

    private const string SortScript = """
        function sortBy(th){const t=th.closest('table'),i=[...th.parentNode.children].indexOf(th),
        asc=th.dataset.asc!=='1';th.dataset.asc=asc?'1':'0';
        [...t.tBodies[0].rows].sort((a,b)=>{const x=a.cells[i].dataset.v??a.cells[i].textContent,
        y=b.cells[i].dataset.v??b.cells[i].textContent;const n=parseFloat(x)-parseFloat(y);
        return (isNaN(n)?x.localeCompare(y):n)*(asc?1:-1);}).forEach(r=>t.tBodies[0].appendChild(r));}
        """;

    private static string IntegrityClass(string integrity) => integrity switch
    {
        "Ok" => "ok",
        "Suspect" => "sus",
        "Corrupt" or "Error" => "bad",
        _ => "",
    };

    internal static string Escape(string? s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
    }

    private static string Bytes(long b) => b switch
    {
        >= 1L << 30 => $"{b / (double)(1L << 30):0.0} GB",
        >= 1L << 20 => $"{b / (double)(1L << 20):0.0} MB",
        _ => $"{b / 1024.0:0.0} KB",
    };

    /// The shared page scaffold: dark theme, tree-marker verdict palette,
    /// generic table styling that all three document kinds use. A non-empty
    /// `script` (the audit table's sort handler) is inlined before </body>.
    private static string Page(string title, string body, string script = "") => $$"""
        <!DOCTYPE html>
        <html><head><meta charset="utf-8"><title>{{Escape(title)}}</title><style>
        body{background:#111111;color:#cccccc;font:14px 'Segoe UI',sans-serif;margin:24px;max-width:1200px}
        h1{color:#eeeeee;font-size:20px;margin:0 0 4px 0}
        .gen{color:#777777;font-size:12px;margin:0 0 16px 0}
        .stats{display:flex;gap:28px;margin:0 0 18px 0}
        .stat b{display:block;color:#eeeeee;font-size:20px}
        .stat{color:#999999;font-size:12px}
        details{border:1px solid #2e2e2e;border-radius:4px;margin:8px 0;padding:6px 12px}
        summary{cursor:pointer;color:#eeeeee}
        table{border-collapse:collapse;width:100%;margin:8px 0}
        td,th{padding:3px 10px;text-align:left;border-bottom:1px solid #222222;font-size:13px}
        th{color:#eeeeee;cursor:pointer}
        .badge{border:1px solid #444444;border-radius:8px;padding:0 8px;font-size:12px}
        .quality{color:#999999;font-size:12px;margin:2px 0 4px 0}
        .win{color:#d8b060;width:18px}
        .audio{color:#6fb3e8;font-size:12px}
        .ok{color:#4a7a4a}.sus{color:#d8b060}.bad{color:#e08080}.up{color:#b08fd8}
        details details{margin-left:1.25em}
        .manifest-file{padding:3px 10px;border-bottom:1px solid #222222;font-size:13px}
        .kind{border:1px solid #444444;border-radius:8px;padding:0 8px;font-size:12px;color:#999999}
        .kind.ok{color:#4a7a4a}.kind.sus{color:#d8b060}.kind.bad{color:#e08080}
        .manifest-size{color:#999999;font-size:12px}
        </style></head><body>
        {{body}}
        {{(script.Length > 0 ? $"<script>{script}</script>" : "")}}
        </body></html>
        """;
}
