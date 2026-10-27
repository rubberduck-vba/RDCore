namespace RDCore.CLI.App.Commands;

internal static class CommandNames
{
    public static (string Name, string Alias) DescribeExtensionCommand = ("describe-ext", "x");
    public static (string Name, string Alias) NewWorkspaceCommand = ("new", "n");
    public static (string Name, string Alias) ExportLibraryCommand = ("export-library", "xl");
    public static (string Name, string Alias) BenchCommand = ("bench", "b");
    public static (string Name, string Alias) ConfigCommand = ("config", "configure");
    public static (string Name, string Alias) ImportProjectCommand = ("import", "i");
}
