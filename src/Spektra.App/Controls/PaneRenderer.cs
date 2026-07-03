using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using System.Runtime.InteropServices;
using Spektra.Core;

namespace Spektra.App.Controls;

/// Owns one document's progressive overview bitmap + optional sharp tile and
/// draws them into a rect. Shared by SpectrogramView (single doc) and
/// CompareSurface (two panes). paneT0/paneT1 select this pane's own normalized
/// time window (differs from the shared viewport for the offset-shifted B pane);
/// vpT0/vpT1 are the shared viewport range used to match the sharp tile.
sealed class PaneRenderer : IDisposable
{
    private SpectrogramDocument? _doc;
    private WriteableBitmap? _overview;
    private int _written;
    private WriteableBitmap? _tile;
    private SpectrogramTile? _tileShown;

    public void SetDocument(SpectrogramDocument? doc)
    {
        _overview?.Dispose();
        _overview = null;
        _written = 0;
        _doc = doc;
        if (doc is not null)
        {
            _overview = new WriteableBitmap(new PixelSize(doc.EstimatedColumns, doc.Bins),
                new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
            Clear(_overview);
            SyncColumns();
        }
    }

    public void SyncColumns()
    {
        if (_doc is null || _overview is null) return;
        var count = Math.Min(_doc.Count, _overview.PixelSize.Width);
        if (count <= _written) return;
        using var fb = _overview.Lock();
        for (var col = _written; col < count; col++)
        {
            var data = _doc.GetColumn(col);
            for (var bin = 0; bin < _doc.Bins; bin++)
            {
                var row = _doc.Bins - 1 - bin;
                Marshal.WriteInt32(fb.Address + row * fb.RowBytes + col * 4,
                    unchecked((int)Palette.ToBgra(data[bin])));
            }
        }
        _written = count;
    }

    public void SetTile(SpectrogramTile? tile)
    {
        _tile?.Dispose();
        _tile = null;
        _tileShown = null;
        if (tile is null || tile.Columns.Count == 0) return;
        _tile = new WriteableBitmap(new PixelSize(tile.Columns.Count, tile.Bins),
            new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
        using var fb = _tile.Lock();
        for (var col = 0; col < tile.Columns.Count; col++)
        {
            var data = tile.Columns[col];
            for (var bin = 0; bin < tile.Bins; bin++)
            {
                var row = tile.Bins - 1 - bin;
                Marshal.WriteInt32(fb.Address + row * fb.RowBytes + col * 4,
                    unchecked((int)Palette.ToBgra(data[bin])));
            }
        }
        _tileShown = tile;
    }

    public bool HasImage => _doc is not null && _overview is not null && _written > 0;

    public void DrawInto(DrawingContext ctx, Rect rect,
        double paneT0, double paneT1, double f0, double f1, double vpT0, double vpT1)
    {
        if (!HasImage) return;
        var span = paneT1 - paneT0;
        // Clip this pane's own [0,1] data to the requested window. When an offset
        // pushes the window past the data (paneT0 < 0 or paneT1 > 1), draw the
        // valid slice into the matching sub-rect so the pane SHIFTS (leaving a
        // blank margin) instead of stretching the remainder over the whole rect.
        var vis0 = Math.Clamp(paneT0, 0, 1);
        var vis1 = Math.Clamp(paneT1, 0, 1);
        if (vis1 > vis0 && span > 0)
        {
            var src = new Rect(
                vis0 * _written, (1 - f1) * _doc!.Bins,
                Math.Max(1, (vis1 - vis0) * _written),
                Math.Max(1, (f1 - f0) * _doc.Bins));
            var dst = new Rect(
                rect.X + (vis0 - paneT0) / span * rect.Width, rect.Y,
                (vis1 - vis0) / span * rect.Width, rect.Height);
            ctx.DrawImage(_overview!, src, dst);
        }

        if (_tile is not null && _tileShown is { } tile &&
            Math.Abs(tile.T0 - vpT0) < 1e-9 && Math.Abs(tile.T1 - vpT1) < 1e-9)
        {
            var tsrc = new Rect(0, (1 - f1) * tile.Bins,
                _tile.PixelSize.Width, Math.Max(1, (f1 - f0) * tile.Bins));
            ctx.DrawImage(_tile, tsrc, rect);
        }
    }

    private static void Clear(WriteableBitmap bmp)
    {
        using var fb = bmp.Lock();
        var black = unchecked((int)Palette.ToBgra(Db.Floor));
        var px = fb.Size.Width * fb.Size.Height;
        for (var i = 0; i < px; i++) Marshal.WriteInt32(fb.Address + i * 4, black);
    }

    public void Dispose()
    {
        _overview?.Dispose();
        _tile?.Dispose();
    }
}
