namespace Spektra.App;

/// Anything in the UI that stands for one file or folder on disk. The five
/// item types across the grid, the browse tree, the manifest, and the
/// duplicates list all answer the same question, so the shared context-menu
/// actions bind to this rather than to five concrete types.
public interface IFileItem
{
    string FullPath { get; }
}
