using System.Text.RegularExpressions;

namespace RDCore.Tests.Platform;

/// <summary>
/// The repository's project files name a path under the folder NuGet restores packages to through a property that ends in a separator: NuGet gives
/// <c>NuGetPackageRoot</c> as a NuGet.config <c>globalPackagesFolder</c> writes it, which can end without one.
/// </summary>
[TestClass]
public sealed class ProjectFileTests
{
    // NuGetPackageRoot followed by anything but the quote that closes a string it is the whole of ('$(NuGetPackageRoot)' != '', EnsureTrailingSlash).
    private static readonly Regex Joined = new(@"\$\(NuGetPackageRoot\)(?!')", RegexOptions.CultureInvariant);

    [TestMethod]
    public void NoProjectFile_JoinsAPathToNuGetPackageRoot()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "RDCore.slnx")))
        {
            root = root.Parent;
        }

        Assert.IsNotNull(root, "the repository root, where RDCore.slnx is");
        var joined = Directory.EnumerateFiles(root.FullName, "*", SearchOption.AllDirectories)
            .Where(path => Path.GetExtension(path) is ".csproj" or ".props" or ".targets")
            .Where(path => !Path.GetRelativePath(root.FullName, path).Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Any(part => part is "bin" or "obj" or "artifacts" or ".git"))
            .SelectMany(path => File.ReadLines(path).Select((line, index) => (path, line, number: index + 1)))
            .Where(each => Joined.IsMatch(each.line))
            .Select(each => $"{Path.GetRelativePath(root.FullName, each.path)}:{each.number}: {each.line.Trim()}")
            .ToArray();

        Assert.IsEmpty(joined, string.Join(Environment.NewLine, joined));
    }
}
