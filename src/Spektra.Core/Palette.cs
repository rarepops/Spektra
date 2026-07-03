namespace Spektra.Core;

/// Magma-family colormap over the full [Db.Floor, 0] range. Thin wrapper over
/// `Colormaps` kept for callers that want the default map without a display range.
public static class Palette
{
    public static uint ToBgra(float db) => Colormaps.ToBgra(PaletteKind.Magma, db, Db.Floor, 0f);
}
