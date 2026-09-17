namespace ClickSaver2026.Tests;

internal static class TestPaths
{
    public static string RepositoryRoot { get; } = FindRepositoryRoot();

    /// <summary>The hook DLL build.ps1 put beside the app, or null when it has not been built.</summary>
    public static string? Hook => Existing(Path.Combine(RepositoryRoot, "Build", "Release", "ClickSaver2026.Hook.dll"));

    /// <summary>The stand-in client build.ps1 built, or null when it has not been built.</summary>
    public static string? HookHost => Existing(Path.Combine(RepositoryRoot, "Build", "Harness", "Release", "HookHost.exe"));

    private static string? Existing(string path) => File.Exists(path) ? path : null;

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
