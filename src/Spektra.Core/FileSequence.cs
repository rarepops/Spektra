namespace Spektra.Core;

/// Positional stepping through an ordered list of files: what Next and
/// Previous mean in the spectrum view. Pure, so the arithmetic is testable
/// without a shell (Spektra.App has no unit tests by house rule).
public static class FileSequence
{
    /// The file one step from <paramref name="current"/>, or null when the
    /// step falls off either end, when the list does not hold the current
    /// file, or when it is empty. Direction is +1 / -1, matching the
    /// convention MainWindowViewModel.SelectNext already uses.
    ///
    /// Ends stop rather than wrap: wrapping would silently restart a triage
    /// pass at the top with no signal that it had finished, and on a
    /// two-file list it would make Next and Previous do the same thing.
    ///
    /// Paths compare OrdinalIgnoreCase to agree with the maps the folder tab
    /// is keyed by and with the duplicate-tab check in OpenFile. A sequence
    /// with its own identity rule is how a walk comes to disagree with the
    /// tab it walks in.
    public static string? Step(IReadOnlyList<string> files, string current, int direction)
    {
        var index = IndexOf(files, current);
        if (index < 0) return null;
        var target = index + direction;
        return target >= 0 && target < files.Count ? files[target] : null;
    }

    private static int IndexOf(IReadOnlyList<string> files, string path)
    {
        for (var i = 0; i < files.Count; i++)
            if (string.Equals(files[i], path, StringComparison.OrdinalIgnoreCase))
                return i;
        return -1;
    }
}
