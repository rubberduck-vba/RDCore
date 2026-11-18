namespace RDCore.Tests;

/// <summary>
/// The categories a test class is filtered by (<c>dotnet test --filter TestCategory=...</c>).
/// </summary>
internal static class TestCategories
{
    /// <summary>
    /// The tests that run on macOS, where the whole suite takes too long: the file system (the file statements and functions, workspaces loaded,
    /// saved, imported and watched, <c>rdc build</c> and <c>rdc install</c>), the processes and pipes the platform starts and talks through, a program
    /// parsed, bound and run, and a few diagnostics. A few tests of each, across the platform: Windows and Linux run them all. At most 1000 rows
    /// (<c>.github/workflows/build.yml</c> counts them).
    /// </summary>
    public const string Wide = "Wide";
}
