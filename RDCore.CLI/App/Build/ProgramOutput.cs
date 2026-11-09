using System.IO.Abstractions;

namespace RDCore.CLI.App.Build;

internal sealed class ProgramOutput(IFileSystem fileSystem, string runtimeFolder)
{
    private const string RdcName = "rdc";

    public string RuntimeFolder => runtimeFolder;

    public bool IsTaken(string program)
        => fileSystem.File.Exists(fileSystem.Path.Combine(runtimeFolder, $"{program}.dll")) || string.Equals(program, RdcName, StringComparison.OrdinalIgnoreCase);

    public string? Write(string folder, string program, byte[] assembly, byte[] pdb, string? host, string runtime)
    {
        _ = fileSystem.Directory.CreateDirectory(folder);
        var dll = $"{program}.dll";
        fileSystem.File.WriteAllBytes(fileSystem.Path.Combine(folder, dll), assembly);
        fileSystem.File.WriteAllBytes(fileSystem.Path.Combine(folder, $"{program}.pdb"), pdb);
        fileSystem.File.WriteAllText(fileSystem.Path.Combine(folder, $"{program}.runtimeconfig.json"), RuntimeConfiguration());
        CopyRuntime(folder);

        if (host is null)
        {
            return null;
        }

        var executable = fileSystem.Path.Combine(folder, AppHost.ExecutableName(program, runtime));
        fileSystem.File.WriteAllBytes(executable, AppHost.Patch(fileSystem.File.ReadAllBytes(host), dll));
        if (!OperatingSystem.IsWindows() && fileSystem is FileSystem)
        {
            File.SetUnixFileMode(executable, File.GetUnixFileMode(executable) | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);
        }

        return executable;
    }

    // what .NET runs the program on: what it runs rdc on, as rdc's own runtime configuration says, so the runtime's assemblies find what they were built
    // against; the shared framework of the version rdc runs on when rdc has none.
    private string RuntimeConfiguration()
    {
        var rdc = fileSystem.Path.Combine(runtimeFolder, $"{RdcName}.runtimeconfig.json");
        if (fileSystem.File.Exists(rdc))
        {
            return fileSystem.File.ReadAllText(rdc);
        }

        var version = Environment.Version;
        return $$"""
            {
              "runtimeOptions": {
                "tfm": "net{{version.Major}}.{{version.Minor}}",
                "framework": {
                  "name": "Microsoft.NETCore.App",
                  "version": "{{version.Major}}.{{version.Minor}}.0"
                }
              }
            }
            """;
    }

    // the runtime's assemblies, and the folders of their resources in each language: what the program loads from beside itself.
    private void CopyRuntime(string folder)
    {
        foreach (var file in fileSystem.Directory.GetFiles(runtimeFolder, "*.dll"))
        {
            fileSystem.File.Copy(file, fileSystem.Path.Combine(folder, fileSystem.Path.GetFileName(file)), overwrite: true);
        }

        foreach (var culture in fileSystem.Directory.GetDirectories(runtimeFolder))
        {
            var resources = fileSystem.Directory.GetFiles(culture, "*.resources.dll");
            if (resources.Length == 0)
            {
                continue;
            }

            var target = fileSystem.Path.Combine(folder, fileSystem.Path.GetFileName(culture));
            _ = fileSystem.Directory.CreateDirectory(target);
            foreach (var file in resources)
            {
                fileSystem.File.Copy(file, fileSystem.Path.Combine(target, fileSystem.Path.GetFileName(file)), overwrite: true);
            }
        }
    }
}
