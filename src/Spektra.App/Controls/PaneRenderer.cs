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
    private DisplaySettings _display = new();

    /// Colormap + dB range used to paint pixels. Changing it repaints the cached
    /// overview and tile bitmaps from the source columns (no re-analysis).
    public void SetDisplay(DisplaySettings display)
    {
        if (_display == display) return;
        _display = display;
        if (_overview is not null) { _written = 0; Clear(_overview); SyncColumns(); }
        if (_tileShown is { } tile) SetTile(tile);
    }

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
                    unchecked((int)PaletteLut.Sample(_display.Lut, data[bin], _display.DbFloor, _display.DbCeil)));
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
                    unchecked((int)PaletteLut.Sample(_display.Lut, data[bin], _display.DbFloor, _display.DbCeil)));
            }
        }
        _tileShown = tile;
    }

    public bool HasImage => _doc is not null && _overview is not null && _written > 0;

    public void DrawInto(DrawingContext ctx, Rect rect,
        double paneT0, double paneT1, double vpT0, double vpT1, FreqAxis freq)
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
            var dst = new Rect(
                rect.X + (vis0 - paneT0) / span * rect.Width, rect.Y,
                (vis1 - vis0) / span * rect.Width, rect.Height);
            DrawBands(ctx, _overview!, _doc!.Bins,
                vis0 * _written, Math.Max(1, (vis1 - vis0) * _written), dst, freq);
        }

        if (_tile is not null && _tileShown is { } tile &&
            Math.Abs(tile.T0 - vpT0) < 1e-9 && Math.Abs(tile.T1 - vpT1) < 1e-9)
        {
            DrawBands(ctx, _tile, tile.Bins, 0, _tile.PixelSize.Width, rect, freq);
        }
    }

    // Draws a bitmap's frequency band [F0,F1] into dst. Linear is one stretch;
    // log splits dst into horizontal strips, each mapping its own frequency
    // sub-band (low frequencies get more vertical room).
    internal static void DrawBands(DrawingContext ctx, WriteableBitmap bmp, int bins,
        double srcX, double srcW, Rect dst, FreqAxis freq)
    {
        if (!freq.Log)
        {
            var src = new Rect(srcX, (1 - freq.F1) * bins, srcW, Math.Max(1, (freq.F1 - freq.F0) * bins));
            ctx.DrawImage(bmp, src, dst);
            return;
        }
        var strips = Math.Clamp((int)dst.Height, 1, 256);
        for (var k = 0; k < strips; k++)
        {
            var qTop = freq.FreqAt(1.0 - (double)k / strips);
            var qBot = freq.FreqAt(1.0 - (double)(k + 1) / strips);
            var src = new Rect(srcX, (1 - qTop) * bins, srcW, Math.Max(0.5, (qTop - qBot) * bins));
            var stripRect = new Rect(dst.X, dst.Y + (double)k / strips * dst.Height, dst.Width, dst.Height / strips + 1);
            ctx.DrawImage(bmp, src, stripRect);
        }
    }

    private void Clear(WriteableBitmap bmp)
    {
        using var fb = bmp.Lock();
        var black = unchecked((int)PaletteLut.Sample(_display.Lut, Db.Floor, _display.DbFloor, _display.DbCeil));
        var px = fb.Size.Width * fb.Size.Height;
        for (var i = 0; i < px; i++) Marshal.WriteInt32(fb.Address + i * 4, black);
    }

    public void Dispose()
    {
        _overview?.Dispose();
        _tile?.Dispose();
    }
}
