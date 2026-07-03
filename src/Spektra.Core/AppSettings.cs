namespace Spektra.Core;

public sealed record WindowPlacement(int X, int Y, int Width, int Height, bool Maximized);

public sealed class AppSettings
{
    public const int MaxRecent = 10;

    public WindowPlacement? Window { get; set; }
    public List<string> RecentFiles { get; set; } = [];

    public void PushRecent(string path)
    {
        RecentFiles.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        RecentFiles.Insert(0, path);
        if (RecentFiles.Count > MaxRecent)
            RecentFiles.RemoveRange(MaxRecent, RecentFiles.Count - MaxRecent);
    }
}
