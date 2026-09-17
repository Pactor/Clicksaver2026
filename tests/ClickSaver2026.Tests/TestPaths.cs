namespace ClickSaver2026.Tests;

internal static class TestPaths
{
    public static string RepositoryRoot { get; } = FindRepositoryRoot();

    /// <summary>A file CMake built (build.ps1), or null when it has not been built.</summary>
    public static string? Built(string project, string file)
    {
        string path = Path.Combine(RepositoryRoot, "build", project, "Release", file);
        return File.Exists(path) ? path : null;
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ClickSaver2026.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }
}
