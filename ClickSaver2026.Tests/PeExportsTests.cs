using System.Runtime.InteropServices;
using ClickSaver2026.Core.Hook;

namespace ClickSaver2026.Tests;

public sealed class PeExportsTests
{
    [Fact]
    public void FindsTheSameAddressAsTheLoader()
    {
        // The test host's own kernel32, compared with where the loader resolved the export.
        nint kernel32 = NativeLibrary.Load("kernel32.dll");
        string path = Path.Combine(Environment.SystemDirectory, "kernel32.dll");

        uint rva = PeExports.GetExportRva(path, "LoadLibraryW");

        Assert.Equal(NativeLibrary.GetExport(kernel32, "LoadLibraryW"), kernel32 + (nint)rva);
    }

    [Fact]
    public void MissingExportIsReported()
    {
        string path = Path.Combine(Environment.SystemDirectory, "kernel32.dll");

        Assert.Throws<EntryPointNotFoundException>(() => PeExports.GetExportRva(path, "NoSuchExport_ClickSaver2026"));
    }

    [Fact]
    public void BuiltHookExportsStartAndShutdown()
    {
        string? hook = TestPaths.Hook;
        Assert.SkipWhen(hook is null, "The hook has not been built (run build.ps1).");

        Assert.NotEqual(0u, PeExports.GetExportRva(hook, HookInjector.StartExport));
        Assert.NotEqual(0u, PeExports.GetExportRva(hook, HookInjector.ShutdownExport));
    }
}
