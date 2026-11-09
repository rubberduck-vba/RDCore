using RDCore.Runtime.Support.Build;
using System.IO.Abstractions;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

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
        CopyRuntime(folder, RuntimeClosure([SupportName, .. ReferencesOf(assembly)]));

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

    // the assembly a built program calls into, and the root of what it runs on.
    public static string SupportName { get; } = typeof(BuiltProgram).Assembly.GetName().Name!;

    // the named assemblies of the runtime folder and every one of it they reference, followed transitively without loading them: what the program loads
    // from beside itself. A reference to the shared framework is not in the folder, and is not followed; rdc itself, its parser and its console are not
    // referenced, and are not copied.
    public IReadOnlyList<string> RuntimeClosure(IEnumerable<string> roots)
    {
        var found = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pending = new Queue<string>(roots);
        while (pending.TryDequeue(out var name))
        {
            var path = fileSystem.Path.Combine(runtimeFolder, $"{name}.dll");
            if (!seen.Add(name) || !fileSystem.File.Exists(path))
            {
                continue;
            }

            found.Add(name);
            foreach (var reference in ReferencesOf(fileSystem.File.ReadAllBytes(path)))
            {
                pending.Enqueue(reference);
            }
        }

        return found;
    }

    public static IReadOnlyList<string> ReferencesOf(byte[] assembly)
    {
        using var image = new PEReader(new MemoryStream(assembly, writable: false));
        var metadata = image.GetMetadataReader();
        return [.. metadata.AssemblyReferences.Select(handle => metadata.GetString(metadata.GetAssemblyReference(handle).Name))];
    }

    // the assemblies, and their resources in each language that has them.
    private void CopyRuntime(string folder, IReadOnlyList<string> closure)
    {
        foreach (var name in closure)
        {
            fileSystem.File.Copy(fileSystem.Path.Combine(runtimeFolder, $"{name}.dll"), fileSystem.Path.Combine(folder, $"{name}.dll"), overwrite: true);
        }

        foreach (var culture in fileSystem.Directory.GetDirectories(runtimeFolder))
        {
            foreach (var name in closure)
            {
                var resources = fileSystem.Path.Combine(culture, $"{name}.resources.dll");
                if (!fileSystem.File.Exists(resources))
                {
                    continue;
                }

                var target = fileSystem.Path.Combine(folder, fileSystem.Path.GetFileName(culture));
                _ = fileSystem.Directory.CreateDirectory(target);
                fileSystem.File.Copy(resources, fileSystem.Path.Combine(target, $"{name}.resources.dll"), overwrite: true);
            }
        }
    }
}
